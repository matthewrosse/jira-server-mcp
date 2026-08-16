using System.ComponentModel;
using System.Text.Json;
using JiraServerMcp.Errors;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Profiles;
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

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = true)]
    [Description(
        "Move one issue along its workflow, naming the transition rather than its identifier — "
        + "\"Done\", not \"31\". Matching ignores casing and surrounding spaces, and a name this "
        + "issue does not offer comes back with the ones it does. A transition whose screen "
        + "demands a field, a resolution most often, takes it in the field map here and succeeds "
        + "in one call; jira_get_issue with the transitions expansion says which fields a "
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
        try
        {
            // Resolved now rather than from whatever an earlier read handed over: the issue may
            // have moved since, and a stale identifier transitions nothing.
            var available = await jira.ListTransitionsAsync(key, cancellationToken);

            if (Match(available, transition) is not { } matched)
            {
                return Error(Unmatched(key, transition, available));
            }

            await jira.TransitionIssueAsync(
                key,
                matched.Id,
                fields ?? new Dictionary<string, JsonElement>(),
                comment,
                cancellationToken);

            return Text(
                $"Transitioned {key} by '{matched.Name}'"
                + (matched.ToStatus is { } status ? $", now in {status}." : "."));
        }
        catch (JiraApiException exception)
        {
            return Error(JiraToolError.Describe(exception, profile.Name, $"transitioning {key}"));
        }
        catch (HttpRequestException exception)
        {
            return Error(
                $"Could not reach Jira, and {key} was not transitioned: {exception.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error(
                $"Jira did not answer for profile '{profile.Name}' in time. The transition was "
                + $"sent once and was not repeated, so read {key} with jira_get_issue to see "
                + "whether it landed.");
        }
    }

    private static JiraTransition? Match(
        IReadOnlyList<JiraTransition> available,
        string transition) =>
        available.FirstOrDefault(
            candidate => string.Equals(
                candidate.Name.Trim(),
                transition.Trim(),
                StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What an agent that guessed a transition name most needs: the names it could have used. The
    /// list is this account's, on this issue, right now, which is the only list that means
    /// anything.
    /// </summary>
    private static string Unmatched(
        string key,
        string transition,
        IReadOnlyList<JiraTransition> available) =>
        available.Count is 0
            ? $"'{transition}' is not a transition on {key}, and neither is anything else: this "
              + "account cannot move that issue from the status it is in."
            : $"'{transition}' is not a transition available on {key}. These are:\n"
              + string.Join(
                  "\n",
                  available.Select(
                      candidate => candidate.ToStatus is { } status
                          ? $"{candidate.Name} (to {status})"
                          : candidate.Name));

    private static CallToolResult Text(string text) =>
        new() { Content = [new TextContentBlock { Text = text }] };

    private static CallToolResult Error(string text) =>
        new() { Content = [new TextContentBlock { Text = text }], IsError = true };
}
