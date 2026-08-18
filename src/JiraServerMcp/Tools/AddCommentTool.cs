using System.ComponentModel;
using JiraServerMcp.Jira;
using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tools;

/// <summary>
/// Registered only under the <c>comments:write</c> grant (ADR-0005). There is no tool that edits or
/// deletes a comment, in this grant or any other.
/// </summary>
[McpServerToolType]
internal sealed class AddCommentTool(JiraClient jira, ServedProfile profile)
{
    private const string Name = "jira_add_comment";

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(AddedCommentOutput))]
    [Description(
        "Add one comment to an issue. The body is Jira wiki markup, written as Jira stores it and "
        + "not converted. The comment cannot be edited or removed afterwards by this server. "
        + "Returns the comment's identifier and the time Jira recorded.")]
    public async Task<CallToolResult> AddCommentAsync(
        [Description("The issue key, such as \"PROJ-42\".")]
        string key,
        [Description("The comment's text, in Jira wiki markup.")]
        string body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return ToolCall.Error(
                $"An empty comment was not added to {key}. Jira refuses one, and there would be "
                + "nothing in it for anyone reading the issue.");
        }

        return await ToolCall.RunAsync(
            profile,
            $"commenting on {key}",
            whenUnreachable: $", and {key} was not commented on",
            whenTimedOut:
                $". The comment was sent once and was not repeated, so read {key} with "
                + "jira_get_issues and the comments expansion before sending it again.",
            async () =>
            {
                var added = await jira.AddCommentAsync(key, body, cancellationToken);

                // The caller wrote the body; handing it back would be context spent on nothing.
                return new Rendered(
                    $"Added comment {added.Id} to {key} at {added.Created}.",
                    ToolOutputs.Node(new AddedCommentOutput
                    {
                        Outcome = Outcomes.Ok,
                        Key = key,
                        CommentId = added.Id,
                    }));
            },
            cancellationToken);
    }
}
