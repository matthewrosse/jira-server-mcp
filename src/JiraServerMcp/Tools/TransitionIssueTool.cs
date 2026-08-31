using System.ComponentModel;
using System.Text.Json;
using JiraServerMcp.Errors;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tools;

/// <summary>
/// Registered only under the <c>issues:write</c> grant (ADR-0005). The transition is named rather
/// than numbered: the identifier is Jira's, differs per workflow, and is not something an agent
/// should have to carry around.
/// </summary>
[McpServerToolType]
internal sealed class TransitionIssueTool(JiraClient jira, ServedProfile profile)
{
    private const string Name = "jira_transition_issue";

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TransitionedIssueOutput))]
    [Description(
        "Move one issue along its workflow, naming the transition rather than its identifier — "
        + "\"Done\", not \"31\". Matching ignores casing and surrounding spaces, and a name this "
        + "issue does not offer comes back with the ones it does. A transition whose screen "
        + "demands a field, a resolution most often, takes it in the field map here and succeeds "
        + "in one call; jira_get_issues with the transitions expansion says which fields a "
        + "transition will ask for.")]
    public async Task<CallToolResult> TransitionIssueAsync(
        [Description("The issue key, such as \"PROJ-42\".")]
        string key,
        [Description("The transition's name, such as \"Start Progress\".")]
        string transition,
        [Description(
            "The fields the transition's screen asks for, keyed by Jira's field identifier. "
            + "Values take Jira's own shape: {\"name\": \"Fixed\"} for a resolution.")]
        IReadOnlyDictionary<string, JsonElement>? fields = null,
        [Description("A comment to add as part of the transition, in the same request.")]
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        // Resolved now rather than from whatever an earlier read handed over: the issue may have
        // moved since, and a stale identifier transitions nothing. This is a read, and it is kept
        // apart from the write below so that a failure here is never described as a write that
        // may have landed.
        var listed = await ToolCall.StepAsync(
            profile,
            $"reading the transitions available on {key}",
            whenUnreachable: $", and {key} was not transitioned",
            whenTimedOut:
                $", and it was asked only which transitions {key} has. Nothing was transitioned.",
            () => jira.ListTransitionsAsync(key, cancellationToken),
            cancellationToken,
            describeApiFailure: (exception, _) =>
                JiraToolError.Describe(
                    exception,
                    profile.Name,
                    $"reading the transitions available on {key}",
                    advice: $"Nothing was transitioned: {key} is as it was."));

        if (listed.Failed)
        {
            return listed.Error;
        }

        var available = listed.Value;

        // Jira lets one status offer two transitions of the same name — a global one and a local
        // one — going to different statuses. Picking either would move the issue somewhere the
        // agent did not ask for and report it as success, so the ambiguous case is a refusal.
        var resolved = Vocabulary.Resolve(
            available,
            candidate => new[] { candidate.Name },
            transition);

        if (resolved is Vocabulary.Ambiguous<JiraTransition> ambiguous)
        {
            return ToolCall.Error(Ambiguous(key, transition, ambiguous.Rows));
        }

        if (resolved is not Vocabulary.Matched<JiraTransition> match)
        {
            return ToolCall.Error(Unmatched(key, transition, available));
        }

        var matched = match.Row;

        return await ToolCall.RunAsync(
            profile,
            $"transitioning {key}",
            whenUnreachable: $", and {key} was not transitioned",
            whenTimedOut:
                $". The transition was sent once and was not repeated, so read {key} with "
                + "jira_get_issues to see whether it landed.",
            async () =>
            {
                await jira.TransitionIssueAsync(
                    key,
                    matched.Id,
                    fields ?? new Dictionary<string, JsonElement>(),
                    comment,
                    cancellationToken);

                return Transitioned(key, matched);
            },
            cancellationToken,
            claim: PermissionAdvice.OnIssue(jira, PermissionAdvice.TransitionIssues, key));
    }

    /// <summary>
    /// A workflow names its transitions, so the confirmation carries Jira-authored text and is
    /// framed as such (ADR-0005's grant is the bound on what a transition can do; the framing is
    /// the bound on what its name can talk the model into).
    /// </summary>
    /// <remarks>
    /// The structured half carries the transition id and the status name, the second only where
    /// Jira reported a destination — matching the prose's conditional "now in:" line rather than
    /// promising a field the prose does not have. A status name is admin-authored and so is
    /// untrusted in provenance, but it is a value Jira enumerates rather than prose someone typed,
    /// which is where ADR-0009 draws the line.
    /// </remarks>
    private static Rendered Transitioned(string key, JiraTransition matched) =>
        new(
            UntrustedContent.Envelope(
                $"Transitioned {key}. The transition and the status it led to are named below.",
                matched.ToStatus is { } status
                    ? $"transition: {matched.Name}\nnow in: {status}"
                    : $"transition: {matched.Name}"),
            ToolOutputs.Node(new TransitionedIssueOutput
            {
                Outcome = Outcomes.Ok,
                Key = key,
                TransitionId = matched.Id,
                Status = matched.ToStatus,
            }));

    /// <summary>
    /// What an agent that guessed a transition name most needs: the names it could have used. The
    /// list is this account's, on this issue, right now, which is the only list that means
    /// anything — and every name in it was written in Jira, so it is framed as data.
    /// </summary>
    private static string Unmatched(
        string key,
        string transition,
        IReadOnlyList<JiraTransition> available) =>
        available.Count is 0
            ? $"'{transition}' is not a transition on {key}, and neither is anything else: this "
              + "account cannot move that issue from the status it is in."
            : UntrustedContent.Envelope(
                $"'{transition}' is not a transition available on {key}. The ones that are follow.",
                Listed(available));

    /// <summary>
    /// Two transitions of one name, which a workflow may legitimately offer. Naming their target
    /// statuses is what lets the caller ask for the one it meant, by transitioning from a status
    /// where the name is unambiguous or by saying which status it wants.
    /// </summary>
    private static string Ambiguous(
        string key,
        string transition,
        IReadOnlyList<JiraTransition> matching) =>
        UntrustedContent.Envelope(
            $"""
             '{transition}' names {matching.Count} different transitions on {key}, going to different
             statuses, so none was made. They follow.
             """,
            Listed(matching));

    private static string Listed(IReadOnlyList<JiraTransition> transitions) =>
        string.Join(
            "\n",
            transitions.Select(
                candidate => candidate.ToStatus is { } status
                    ? $"{candidate.Name} (to {status})"
                    : candidate.Name));
}
