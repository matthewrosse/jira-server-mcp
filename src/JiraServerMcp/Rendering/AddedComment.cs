using System.Text.Json.Serialization;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// A comment Jira added. The caller wrote the body, so handing it back would be context spent on
/// nothing: the answer names the comment and when Jira recorded it.
/// </summary>
internal static class AddedComment
{
    public static Rendered Render(string key, JiraAddedComment added) =>
        new(
            $"Added comment {added.Id} to {key} at {added.Created}.",
            ToolOutputs.Node(new AddedCommentOutput
            {
                Outcome = Outcomes.Ok,
                Key = key,
                CommentId = added.Id,
            }));
}

internal sealed record AddedCommentOutput : ToolOutput
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("commentId")]
    public string? CommentId { get; init; }
}
