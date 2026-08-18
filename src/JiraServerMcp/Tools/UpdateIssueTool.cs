using System.ComponentModel;
using System.Net;
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
/// Registered only under the <c>issues:write</c> grant (ADR-0005). There is deliberately no
/// separate assign tool: reassignment should not cost two operations.
/// </summary>
[McpServerToolType]
internal sealed class UpdateIssueTool(
    JiraClient jira,
    ServedProfile profile,
    FieldAliases aliases)
{
    private const string Name = "jira_update_issue";

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(UpdatedIssueOutput))]
    [Description(
        "Change fields of one issue, and its assignee, in a single call. Values in the field map "
        + "take Jira's own shape: a string for a text field, {\"id\": \"10300\"} for a select, a "
        + "list for labels. A field given the value null is cleared; a field not named is left "
        + "alone. Whatever is written replaces what was there, so read the issue with "
        + "jira_get_issues first when appending rather than overwriting is meant.")]
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
                + "jira_get_issues to see whether it landed.",
            async () =>
            {
                await jira.UpdateIssueAsync(
                    key,
                    Resolved(fields),
                    assignee is null
                        ? null
                        : new JiraAssignee(assignee.Length is 0 ? null : assignee),
                    cancellationToken);

                return Confirm(key, Resolved(fields), assignee);
            },
            cancellationToken,
            describeApiFailure: exception => JiraToolError.Describe(
                exception,
                profile.Name,
                $"updating {key}",
                advice: exception.StatusCode is HttpStatusCode.BadRequest
                    ? "Call jira_get_create_fields for the identifiers this project's fields "
                      + "have." + FieldAliasAdvice.From(aliases)
                    : null));
    }

    /// <summary>
    /// What was sent, named as the prose names it. The structured half carries the same list, off
    /// the same traversal: it is the field ids the caller asked for, which is what a workflow
    /// checking its own write needs.
    /// </summary>
    private static Rendered Confirm(
        string key,
        IReadOnlyDictionary<string, JsonElement>? fields,
        string? assignee)
    {
        var changed = new List<string>(fields?.Keys ?? []);

        if (assignee is not null)
        {
            changed.Add(assignee.Length is 0 ? "assignee (cleared)" : "assignee");
        }

        return new Rendered(
            $"Updated {key}: {string.Join(", ", changed)}.",
            ToolOutputs.Node(new UpdatedIssueOutput
            {
                Outcome = Outcomes.Ok,
                Key = key,
                Changed = changed,
            }));
    }

    /// <summary>
    /// The field map with this profile's aliases resolved to the identifiers Jira knows. A key
    /// that is not an alias is passed through: the field catalogue lives in Jira, not here, and a
    /// name this server does not recognise is far more likely to be a real identifier.
    /// </summary>
    private IReadOnlyDictionary<string, JsonElement> Resolved(
        IReadOnlyDictionary<string, JsonElement>? fields)
    {
        if (fields is not { Count: > 0 })
        {
            return new Dictionary<string, JsonElement>();
        }

        var resolved = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            resolved[aliases.Resolve(field.Key)] = field.Value;
        }

        return resolved;
    }
}
