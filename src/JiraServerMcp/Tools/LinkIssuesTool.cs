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

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = false)]
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
        [Description("A comment to add to 'from' as part of the link, in the same request.")]
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
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
            describeApiFailure: exception =>
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
        var matching = Matching(types, relation);

        if (matching.Count is 0)
        {
            return ToolCall.Error(Unmatched(relation, types));
        }

        // A Jira carrying custom link types can publish one phrase on two of them, and each would
        // put a different relation on the issue panel. Picking either invents the caller's intent.
        if (matching.Count > 1)
        {
            return ToolCall.Error(Ambiguous(relation, matching));
        }

        var (type, outward) = matching[0];

        // Jira reads the link as "outwardIssue <outward wording> inwardIssue", so a phrase matched
        // on the inward wording puts 'from' on the inward end.
        var (outwardKey, inwardKey) = outward ? (from, to) : (to, from);

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

                return Linked(from, to, relation);
            },
            cancellationToken,
            describeApiFailure: exception => Failed(exception, profile.Name, from, to));
    }

    /// <summary>
    /// The types whose wording the phrase matches, each paired with the end it matched. A
    /// symmetric type — <c>Relates</c> publishes "relates to" from both ends — matches once, on
    /// its outward end, because there is no direction to choose between.
    /// </summary>
    private static IReadOnlyList<(JiraIssueLinkType Type, bool Outward)> Matching(
        IReadOnlyList<JiraIssueLinkType> types,
        string relation) =>
    [
        .. types
            .Select(type => (Type: type, Outward: Same(type.Outward, relation)))
            .Where(match => match.Outward || Same(match.Type.Inward, relation)),
    ];

    private static bool Same(string wording, string relation) =>
        string.Equals(wording.Trim(), relation.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Jira answers a key it cannot see with the same 404 it answers a key that does not exist,
    /// and the body does not reliably say which key it meant. Two more reads to find out is worse
    /// value than naming both keys and being plain that nothing was written.
    /// </summary>
    private static string Failed(
        JiraApiException exception,
        string profileName,
        string from,
        string to) =>
        exception.StatusCode is HttpStatusCode.NotFound
            ? $"Jira answered 404 for the link between {from} and {to}. One of the two does not "
              + "exist or is not visible to this account, and Jira does not say which. Nothing "
              + "was linked."
            : JiraToolError.Describe(
                exception,
                profileName,
                $"linking {from} to {to}",
                advice: $"Nothing was linked: {from} and {to} are as they were.");

    private static string Linked(string from, string to, string relation) =>
        $"Linked {from} to {to}: {from} {relation.Trim()} {to}.";

    /// <summary>
    /// What an agent that guessed a phrase most needs: the phrases this Jira actually publishes.
    /// Every one of them was written in Jira, so the list is framed as data.
    /// </summary>
    private static string Unmatched(string relation, IReadOnlyList<JiraIssueLinkType> types) =>
        types.Count is 0
            ? $"'{relation}' is not a relation this Jira publishes, and neither is anything else: "
              + "no issue link types are configured, so no issues can be linked."
            : $"""
               '{relation}' is not a relation this Jira publishes, so nothing was linked. The ones
               it does follow, and either wording of a type may be used.
               {UntrustedContent.Preamble}
               {UntrustedContent.Delimit(Listed(types))}
               """;

    /// <summary>
    /// One phrase on two types. Naming both is what lets the caller ask again with the wording of
    /// the one it meant, where the two types word their other end differently.
    /// </summary>
    private static string Ambiguous(
        string relation,
        IReadOnlyList<(JiraIssueLinkType Type, bool Outward)> matching) =>
        $"""
         '{relation}' is the wording of {matching.Count} different link types on this Jira, so
         nothing was linked. They follow.
         {UntrustedContent.Preamble}
         {UntrustedContent.Delimit(Listed([.. matching.Select(match => match.Type)]))}
         """;

    private static string Listed(IReadOnlyList<JiraIssueLinkType> types) =>
        string.Join(
            "\n",
            types.Select(type => $"{type.Name}: {type.Outward} / {type.Inward}"));
}
