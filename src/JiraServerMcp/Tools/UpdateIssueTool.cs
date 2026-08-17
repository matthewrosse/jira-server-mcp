using System.ComponentModel;
using System.Text.Json;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Profiles;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tools;

/// <summary>
/// Registered only under the <c>issues:write</c> grant (ADR-0005). There is deliberately no
/// separate assign tool: reassignment should not cost two operations.
/// </summary>
[McpServerToolType]
internal sealed class UpdateIssueTool(JiraClient jira, ServedProfile profile)
{
    private const string Name = "jira_update_issue";

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = true)]
    [Description(
        "Change fields of one issue, and its assignee, in a single call. Values in the field map "
        + "take Jira's own shape: a string for a text field, {\"id\": \"10300\"} for a select, a "
        + "list for labels. A field given the value null is cleared; a field not named is left "
        + "alone. Whatever is written replaces what was there, so read the issue with "
        + "jira_get_issue first when appending rather than overwriting is meant.")]
    public async Task<CallToolResult> UpdateIssueAsync(
        [Description("The issue key, such as \"PROJ-42\".")]
        string key,
        [Description(
            "The fields to change, keyed by Jira's field identifier. A value of null clears the "
            + "field; a field not named here is left as it is.")]
        IReadOnlyDictionary<string, JsonElement>? fields = null,
        [Description(
            "The Jira Server username to assign the issue to, as jira_search_users spells it. "
            + "Empty unassigns the issue; omitted leaves the assignee alone.")]
        string? assignee = null,
        CancellationToken cancellationToken = default)
    {
        if (fields is not { Count: > 0 } && assignee is null)
        {
            return ToolCall.Error(
                $"Nothing was named to change on {key}, so nothing was sent to Jira. Name at "
                + "least one field, or an assignee.");
        }

        return await ToolCall.RunAsync(
            profile,
            $"updating {key}",
            whenUnreachable: $", and {key} was not changed",
            whenTimedOut:
                $". The update was sent once and was not repeated, so read {key} with "
                + "jira_get_issue to see whether it landed.",
            async () =>
            {
                await jira.UpdateIssueAsync(
                    key,
                    fields ?? new Dictionary<string, JsonElement>(),
                    assignee is null
                        ? null
                        : new JiraAssignee(assignee.Length is 0 ? null : assignee),
                    cancellationToken);

                return Confirm(key, fields, assignee);
            },
            cancellationToken);
    }

    private static string Confirm(
        string key,
        IReadOnlyDictionary<string, JsonElement>? fields,
        string? assignee)
    {
        var changed = new List<string>(fields?.Keys ?? []);

        if (assignee is not null)
        {
            changed.Add(assignee.Length is 0 ? "assignee (cleared)" : "assignee");
        }

        return $"Updated {key}: {string.Join(", ", changed)}.";
    }
}
