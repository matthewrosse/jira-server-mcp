using System.ComponentModel;
using System.Net;
using JiraServerMcp.Errors;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tools;

/// <summary>
/// Registered only under the <c>links:write</c> grant (ADR-0005). The link is asked for as a
/// relation phrase rather than as a type paired with a direction, so which issue ends up on which
/// end of the link cannot be got wrong (ADR-0010).
/// </summary>
[McpServerToolType]
internal sealed class LinkIssuesTool(JiraClient jira, ServedProfile profile)
{
    private const string Name = "jira_link_issues";

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(LinkedIssuesOutput))]
    [Description(
        "Link one issue to another, naming the relation the way Jira words it — "
        + "jira_link_issues(from: \"PROJ-1\", to: \"PROJ-2\", relation: \"blocks\") reads as the "
        + "English sentence it makes. The phrase decides the direction, so there is no inward or "
        + "outward to get wrong. Matching ignores casing and surrounding spaces, and a phrase this "
        + "Jira does not publish comes back with the ones it does. There is no unlink tool.")]
    public async Task<CallToolResult> LinkIssuesAsync(
        [Description("The issue the relation is about, such as \"PROJ-1\" in \"PROJ-1 blocks PROJ-2\".")]
        string from,
        [Description("The issue on the other end, such as \"PROJ-2\" in \"PROJ-1 blocks PROJ-2\".")]
        string to,
        [Description(
            "Jira's own wording for the relation, such as \"blocks\", \"is blocked by\", "
            + "\"duplicates\" or \"relates to\".")]
        string relation,
        [Description(
            "A comment explaining the link, added in the same request. Jira puts it on the issue "
            + "it treats as the link's source, which the relation phrase decides.")]
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(relation))
        {
            return ToolCall.Error(
                $"{from} was not linked to {to}: the relation is what says how the two are "
                + "related and which way round, and an empty one says neither. Name Jira's own "
                + "wording, such as \"blocks\" or \"is blocked by\".");
        }

        // Not a safety pre-flight: Jira's endpoint takes a type name and a direction, and this is
        // the only thing that turns the phrase the caller wrote into either of them.
        var listed = await ToolCall.StepAsync(
            profile,
            "reading the issue link types this Jira publishes",
            whenUnreachable: $", and {from} was not linked to {to}",
            whenTimedOut: $", and it was asked only which link types exist. {from} and {to} are "
                          + "not linked.",
            () => jira.ListIssueLinkTypesAsync(cancellationToken),
            cancellationToken,
            describeApiFailure: (exception, _) =>
                JiraToolError.Describe(
                    exception,
                    profile.Name,
                    "reading the issue link types this Jira publishes",
                    advice: $"Nothing was linked: {from} and {to} are as they were."));

        if (listed.Failed)
        {
            return listed.Error;
        }

        var types = listed.Value;

        // A Jira carrying custom link types can publish one phrase on two of them, and each would
        // put a different relation on the issue panel. Picking either invents the caller's intent,
        // so the ambiguous case is a refusal. A symmetric type — Relates publishes "relates to"
        // from both ends — is one candidate, matched on its outward wording.
        var resolved = Vocabulary.Resolve(
            types,
            candidate => new[] { candidate.Outward, candidate.Inward },
            relation);

        if (resolved is Vocabulary.Ambiguous<JiraIssueLinkType> ambiguous)
        {
            return ToolCall.Error(Ambiguous(relation, ambiguous.Rows));
        }

        if (resolved is not Vocabulary.Matched<JiraIssueLinkType> match)
        {
            return ToolCall.Error(Unmatched(relation, types));
        }

        var type = match.Row;

        // Jira reads the link as "outwardIssue <outward wording> inwardIssue", and the outward
        // wording is published first, so a phrase matched at any later index puts 'from' on the
        // inward end.
        var (outwardKey, inwardKey) = match.WordIndex is 0 ? (from, to) : (to, from);

        return await ToolCall.RunAsync(
            profile,
            $"linking {from} to {to}",
            whenUnreachable: $", and {from} was not linked to {to}",
            whenTimedOut:
                $". The link was sent once and was not repeated, so read {from} with "
                + "jira_get_issues and the links expansion to see whether it landed.",
            async () =>
            {
                await jira.LinkIssuesAsync(type.Name, outwardKey, inwardKey, comment, cancellationToken);

                return Linked(from, to, relation, type);
            },
            cancellationToken,
            describeApiFailure: (exception, permission) =>
                Failed(exception, profile.Name, from, to, permission),
            // The 'from' issue, and only it: a refusal names one endpoint, so it gets one scope.
            claim: PermissionAdvice.OnIssue(jira, PermissionAdvice.LinkIssues, from));
    }

    /// <summary>
    /// Jira answers a key it cannot see with the same 404 it answers a key that does not exist,
    /// and the body does not reliably say which key it meant. Two more reads to find out is worse
    /// value than naming both keys and being plain that nothing was written.
    /// </summary>
    private static string Failed(
        JiraApiException exception,
        string profileName,
        string from,
        string to,
        PermissionAnswer? permission) =>
        exception.StatusCode is HttpStatusCode.NotFound
            ? $"Jira answered 404 for the link between {from} and {to}. One of the two does not "
              + "exist or is not visible to this account, and Jira does not say which. Nothing "
              + "was linked."
            : JiraToolError.Describe(
                exception,
                profileName,
                $"linking {from} to {to}",
                advice: $"Nothing was linked: {from} and {to} are as they were.",
                permission);

    /// <summary>
    /// The structured half carries the phrase and the type name both. They are different strings —
    /// "is blocked by" is stored under <c>Blocks</c> — and each answers a question the other
    /// cannot: the phrase is what a repeat call would send and what reads as English, the type name
    /// is what the issue panel and Jira's own payloads say. Carrying the identifier beside the
    /// enumerated name is what the issue row already does with <c>statusId</c> and <c>status</c>.
    /// The two keys are the caller's, unswapped: the phrase decided the direction (ADR-0010), so
    /// reporting the ends the way Jira slots them would hand back a sentence nobody wrote.
    /// </summary>
    private static Rendered Linked(
        string from,
        string to,
        string relation,
        JiraIssueLinkType type) =>
        new(
            $"Linked {from} to {to}: {from} {relation.Trim()} {to}.",
            ToolOutputs.Node(new LinkedIssuesOutput
            {
                Outcome = Outcomes.Ok,
                From = from,
                To = to,
                Relation = relation.Trim(),
                TypeName = type.Name,
            }));

    /// <summary>
    /// What an agent that guessed a phrase most needs: the phrases this Jira actually publishes.
    /// Every one of them was written in Jira, so the list is framed as data.
    /// </summary>
    private static string Unmatched(string relation, IReadOnlyList<JiraIssueLinkType> types) =>
        types.Count is 0
            ? $"'{relation}' is not a relation this Jira publishes, and neither is anything else: "
              + "no issue link types are configured, so no issues can be linked."
            : UntrustedContent.Envelope(
                $"""
                 '{relation}' is not a relation this Jira publishes, so nothing was linked. The ones
                 it does follow, and either wording of a type may be used.
                 """,
                Listed(types));

    /// <summary>
    /// One phrase on two types. Naming both is what lets the caller ask again with the wording of
    /// the one it meant, where the two types word their other end differently.
    /// </summary>
    private static string Ambiguous(
        string relation,
        IReadOnlyList<JiraIssueLinkType> matching) =>
        UntrustedContent.Envelope(
            $"""
             '{relation}' is the wording of {matching.Count} different link types on this Jira, so
             nothing was linked. They follow.
             """,
            Listed(matching));

    private static string Listed(IReadOnlyList<JiraIssueLinkType> types) =>
        string.Join(
            "\n",
            types.Select(type => $"{type.Name}: {type.Outward} / {type.Inward}"));
}
