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
        + "alone. A field in the field map is replaced wholesale, so appending to a list field — "
        + "labels, components, fix versions — is what add and remove are for; reading the issue "
        + "with jira_get_issues first is the fallback for a field whose edit screen publishes set "
        + "alone.")]
    public async Task<CallToolResult> UpdateIssueAsync(
        [Description("The issue key, such as \"PROJ-42\".")]
        string key,
        [Description(
            "The fields to change, keyed by Jira's field identifier. A value of null clears the "
            + "field; a field not named here is left as it is.")]
        IReadOnlyDictionary<string, JsonElement>? fields = null,
        [Description(
            "Values to add to fields, keyed by field identifier as the field map is. One value or "
            + "a list of them, each in Jira's own shape for a single item of that field: "
            + "\"regression\" for labels, {\"name\": \"web\"} for components. Each item is added "
            + "beside what the field already carries rather than replacing it. "
            + "jira_get_edit_fields says which fields this issue publishes add for.")]
        IReadOnlyDictionary<string, JsonElement>? add = null,
        [Description(
            "Values to remove from fields, in the same shape as add. Removing a value the field "
            + "does not carry succeeds and changes nothing, so a call that seems to have done "
            + "nothing has done what was asked and is not worth repeating.")]
        IReadOnlyDictionary<string, JsonElement>? remove = null,
        [Description(
            "The Jira Server username to assign the issue to, as jira_search_users spells it. "
            + "Empty unassigns the issue; omitted leaves the assignee alone.")]
        string? assignee = null,
        CancellationToken cancellationToken = default)
    {
        if (fields is not { Count: > 0 }
            && add is not { Count: > 0 }
            && remove is not { Count: > 0 }
            && assignee is null)
        {
            return ToolCall.Error(
                $"Nothing was named to change on {key}, so nothing was sent to Jira. Name at "
                + "least one field to set, add to or remove from, or an assignee.");
        }

        // An alias names a field, never an operation, so it resolves in all three maps or it is a
        // trap: story_points meaning customfield_10010 in one argument and nothing in the next.
        if (!aliases.TryResolve(fields, out var resolved, out var collided))
        {
            return Collision(key, collided);
        }

        if (!aliases.TryResolve(add, out var added, out collided))
        {
            return Collision(key, collided);
        }

        if (!aliases.TryResolve(remove, out var removed, out collided))
        {
            return Collision(key, collided);
        }

        if (Elsewhere(key, added.Keys.Concat(removed.Keys)) is { } served)
        {
            return ToolCall.Error(served);
        }

        var both = InBothEnvelopes(key, resolved, added, "add")
                   ?? InBothEnvelopes(key, resolved, removed, "remove");

        if (both is not null)
        {
            return ToolCall.Error(both);
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
                    resolved,
                    added,
                    removed,
                    assignee is null
                        ? null
                        : new JiraAssignee(assignee.Length is 0 ? null : assignee),
                    cancellationToken);

                return Confirm(key, resolved, added, removed, assignee);
            },
            cancellationToken,
            describeApiFailure: (exception, permission) => JiraToolError.Describe(
                exception,
                profile.Name,
                $"updating {key}",
                advice: exception.StatusCode is HttpStatusCode.BadRequest
                    ? "Call jira_get_edit_fields for the identifiers this issue's fields have, "
                      + "which of them it will accept, and which operations — set, add, remove — "
                      + "each publishes." + FieldAliasAdvice.From(aliases)
                      + AssigneeAdvice(assignee)
                    : null,
                permission),
            claim: PermissionAdvice.OnIssue(jira, PermissionAdvice.EditIssues, key));
    }

    private static CallToolResult Collision(string key, string? collided) =>
        ToolCall.Error(
            $"{collided} name the same field on {key}, and they were given different values, "
            + "so nothing was sent. Name it once — either by its alias or by its identifier.");

    /// <summary>
    /// The two fields Jira does take through its update envelope and this server does not. Both
    /// refusals name the tool that serves the field instead, and say that Jira would have taken
    /// it — a refusal that reads as a Jira limitation sends an agent looking for another way round
    /// rather than at the tool one door along.
    /// </summary>
    private static string? Elsewhere(string key, IEnumerable<string> fields)
    {
        foreach (var field in fields)
        {
            var served = field.ToLowerInvariant() switch
            {
                "issuelinks" =>
                    "Call jira_link_issues, which takes the relation phrase this Jira publishes — "
                    + "\"blocks\", \"is blocked by\" — so the direction cannot be got backwards. "
                    + "Jira itself would take a link here, in the slots that make it possible to.",
                "comment" =>
                    "Call jira_add_comment, which returns the comment it added and sits behind the "
                    + "comments:write grant rather than this one. Jira itself would take a comment "
                    + "here.",
                _ => null,
            };

            if (served is not null)
            {
                return $"{field} is not written through {Name}, so nothing was sent to {key}. "
                       + served;
            }
        }

        return null;
    }

    /// <summary>
    /// A field named in the field map and in add or remove. Jira refuses the pairing itself, with
    /// a message naming the field and its own two envelopes; refusing it here names the two
    /// arguments the caller actually wrote, and costs no request.
    /// </summary>
    private static string? InBothEnvelopes(
        string key,
        IReadOnlyDictionary<string, JsonElement> fields,
        IReadOnlyDictionary<string, JsonElement> operated,
        string operation)
    {
        var both = operated.Keys.FirstOrDefault(fields.ContainsKey);

        return both is null
            ? null
            : $"{both} was named in both fields and {operation} on {key}, so nothing was sent. "
              + "Jira takes a field in one envelope or the other and refuses it in both, so name "
              + "it in one of them.";
    }

    /// <summary>
    /// The clause a rejected update earns only when it named somebody. Jira answers an assignee it
    /// will not accept with a 400 naming a field rather than a permission, so "this person does not
    /// exist" and "this person cannot be assigned here" arrive as the same sentence. This tool has a
    /// dedicated assignee parameter, so it is the one that knows a person was in play; clearing an
    /// assignee names nobody, and Jira accepts that from anyone who may edit.
    /// </summary>
    private static string AssigneeAdvice(string? assignee) =>
        assignee is null or ""
            ? string.Empty
            : $" If it was the assignee Jira rejected, '{assignee}' may exist without being "
              + "assignable here — call jira_search_users with assignableTo set to this issue's "
              + "key for the people it will accept.";

    /// <summary>
    /// What was sent, named as the prose names it. The structured half carries the same list, off
    /// the same traversal: it is the field ids the caller asked for, which is what a workflow
    /// checking its own write needs.
    /// </summary>
    /// <summary>
    /// What was changed, named as the caller would recognise it. The prose labels an aliased field
    /// with both names, so an agent that wrote "story_points" can match the answer to its own
    /// request, and says which of a field's operations carried the change; the structured half
    /// carries the identifiers alone, which is what rule 2 of ADR-0009 admits and what a follow-up
    /// call must send. The operation is left out of it deliberately: it was the caller's own
    /// argument, which the caller already holds.
    /// </summary>
    private Rendered Confirm(
        string key,
        IReadOnlyDictionary<string, JsonElement>? fields,
        IReadOnlyDictionary<string, JsonElement> added,
        IReadOnlyDictionary<string, JsonElement> removed,
        string? assignee)
    {
        var changed = new List<string>();
        var named = new List<string>();

        void Note(IEnumerable<string> operated, string? how)
        {
            foreach (var field in operated)
            {
                if (!changed.Contains(field, StringComparer.Ordinal))
                {
                    changed.Add(field);
                }

                named.Add(how is null ? aliases.Label(field) : $"{aliases.Label(field)} ({how})");
            }
        }

        Note(fields?.Keys ?? [], null);
        Note(added.Keys, "added");
        Note(removed.Keys, "removed");

        if (assignee is not null)
        {
            var how = assignee.Length is 0 ? "assignee (cleared)" : "assignee";

            changed.Add(how);
            named.Add(how);
        }

        return new Rendered(
            $"Updated {key}: {string.Join(", ", named)}.",
            ToolOutputs.Node(new UpdatedIssueOutput
            {
                Outcome = Outcomes.Ok,
                Key = key,
                Changed = changed,
            }));
    }

}
