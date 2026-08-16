using System.ComponentModel;
using JiraServerMcp.Errors;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Profiles;
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

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = false)]
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
        try
        {
            var added = await jira.AddCommentAsync(key, body, cancellationToken);

            // The caller wrote the body; handing it back would be context spent on nothing.
            return Text($"Added comment {added.Id} to {key} at {added.Created}.");
        }
        catch (JiraApiException exception)
        {
            return Error(JiraToolError.Describe(exception, profile.Name, $"commenting on {key}"));
        }
        catch (HttpRequestException exception)
        {
            return Error($"Could not reach Jira, and {key} was not commented on: "
                         + exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error(
                $"Jira did not answer for profile '{profile.Name}' in time. The comment was sent "
                + $"once and was not repeated, so read {key} with jira_get_issue and the comments "
                + "expansion before sending it again.");
        }
    }

    private static CallToolResult Text(string text) =>
        new() { Content = [new TextContentBlock { Text = text }] };

    private static CallToolResult Error(string text) =>
        new() { Content = [new TextContentBlock { Text = text }], IsError = true };
}
