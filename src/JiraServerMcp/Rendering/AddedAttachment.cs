using System.Text.Json.Serialization;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// An attachment Jira stored. The caller wrote the content, so handing it back would be context
/// spent on nothing: the answer names the file, its identifier and the size Jira counted.
/// </summary>
internal static class AddedAttachment
{
    public static Rendered Render(string key, JiraAttachment added) =>
        new(
            $"Attached {added.FileName} to {key} as attachment {added.Id} "
            + $"({added.Size} bytes).",
            ToolOutputs.Node(new AddedAttachmentOutput
            {
                Outcome = Outcomes.Ok,
                Key = key,
                AttachmentId = added.Id,
                FileName = added.FileName,
                Size = added.Size,
            }));
}

/// <summary>
/// An attachment as Jira stored it. Modelled on <see cref="AddedCommentOutput"/> rather than
/// reusing <see cref="AttachmentOutput"/>, whose <c>binary</c> and <c>nextOffset</c> are
/// read-paging semantics that would be null here forever. <see cref="Size"/> is Jira's own count
/// rather than this server's, so it reports what was stored.
/// </summary>
internal sealed record AddedAttachmentOutput : ToolOutput
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("attachmentId")]
    public string? AttachmentId { get; init; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; init; }

    [JsonPropertyName("size")]
    public long? Size { get; init; }
}
