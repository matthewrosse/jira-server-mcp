using System.Text.Json.Serialization;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// One window of an attachment as an agent reads it. A file uploaded by anyone with a Jira account
/// is the least trustworthy text on a ticket, so the question of provenance needs no case
/// analysis: attachment content is untrusted content, always.
/// </summary>
internal static class AttachmentContent
{
    /// <summary>
    /// The window, framed and delimited, or a description of a window whose bytes are not text. A
    /// binary is described rather than inlined — its bytes would cost the caller its context to
    /// learn nothing, and there is nothing an agent can do with a truncated PNG.
    /// </summary>
    public static Rendered Render(JiraAttachment attachment, ReadOnlySpan<byte> window, long offset)
    {
        if (!AttachmentText.IsText(window))
        {
            return Binary(attachment, window, offset);
        }

        var usable = AttachmentText.Usable(window);
        var read = offset + usable;
        var remaining = Remaining(attachment, read);
        var more = usable > 0 && More(attachment, window, remaining);

        return new Rendered(
            UntrustedContent.Envelope(
                Header(attachment, offset, usable, remaining, more),
                AttachmentText.Read(window[..usable])),
            ToolOutputs.Node(new AttachmentOutput
            {
                Outcome = Outcomes.Ok,
                AttachmentId = attachment.Id,
                FileName = attachment.FileName,
                MediaType = attachment.MimeType,
                Size = Size(attachment),
                Binary = false,
                Offset = offset,
                Bytes = usable,
                NextOffset = more ? read : null,
                BytesRemaining = remaining,
            }));
    }

    /// <summary>
    /// What is known about bytes that cannot be read as text. The media type is Jira's claim and is
    /// named as such: it played no part in this answer, which was decided by the bytes.
    /// </summary>
    /// <remarks>
    /// A window past the start of the file is described as the window it is, not as the file. The
    /// caller has already been handed readable text from earlier in this file, and telling it the
    /// file is not text would contradict what it is holding. No resume position is offered either
    /// way: nothing here can say where readable text picks up again, and inventing an offset would
    /// send the caller paging through a binary a window at a time.
    /// </remarks>
    private static Rendered Binary(JiraAttachment attachment, ReadOnlySpan<byte> window, long offset)
    {
        var what = offset is 0
            ? $"{attachment.FileName} is not text"
            : $"{attachment.FileName} stops being text at byte {offset}";

        var claim = attachment.MimeType is { Length: > 0 } claimed
            ? $"Jira claims it is {claimed}."
            : "Jira claims no media type for it.";

        var size = attachment.Size > 0 ? $"It is {attachment.Size} bytes. " : "";

        return new Rendered(
            $"{what} — those bytes do not read as UTF-8, so they are described rather than read. "
            + $"{size}{claim} Nothing was decoded, and no more of this file can be read.",
            ToolOutputs.Node(new AttachmentOutput
            {
                Outcome = Outcomes.Ok,
                AttachmentId = attachment.Id,
                FileName = attachment.FileName,
                MediaType = attachment.MimeType,
                Size = Size(attachment),
                Binary = true,
                Offset = offset,
                Bytes = 0,
                BytesRemaining = Remaining(attachment, offset + window.Length),
            }));
    }

    /// <summary>
    /// Jira's size, where Jira gave one. A missing or unreadable <c>size</c> reaches this module as
    /// zero, and under ADR-0009 as amended a paging field is carried only where the server was
    /// actually given the number — absence means unknown, where zero would mean an empty file.
    /// </summary>
    private static long? Size(JiraAttachment attachment) =>
        attachment.Size > 0 ? attachment.Size : null;

    private static long? Remaining(JiraAttachment attachment, long read) =>
        attachment.Size > 0 ? Math.Max(attachment.Size - read, 0) : null;

    /// <summary>
    /// Whether more of the file remains. Where Jira gave a size, that answers it. Where it did not,
    /// a window that came back full is the evidence available: a file that ended inside this window
    /// would have come back short. Guessing "complete" instead would tell a caller it holds a whole
    /// file when it holds the first sixteen kilobytes of one.
    /// </summary>
    private static bool More(JiraAttachment attachment, ReadOnlySpan<byte> window, long? remaining) =>
        remaining is { } left ? left > 0 : window.Length >= ResponseBudget.AttachmentWindow;

    private static string Header(
        JiraAttachment attachment,
        long offset,
        int usable,
        long? remaining,
        bool more)
    {
        if (usable is 0)
        {
            return attachment.Size > 0
                ? $"{attachment.FileName}: nothing to read at byte {offset} — the file is "
                  + $"{attachment.Size} bytes."
                : $"{attachment.FileName}: nothing to read at byte {offset}.";
        }

        var of = attachment.Size > 0 ? $" of {attachment.Size}" : "";
        var read = $"{attachment.FileName}, bytes {offset}-{offset + usable - 1}{of}";

        if (!more)
        {
            return $"{read} — the rest of the file.";
        }

        return remaining is { } left
            ? $"{read} — {left} bytes remain; ask for them with offset: {offset + usable}."
            : $"{read} — Jira did not say how large this file is, and this window came back full, "
              + $"so there is probably more; ask for it with offset: {offset + usable}.";
    }
}

/// <summary>
/// One window of an attachment. The file name is a selection label under ADR-0009's amended rule
/// 2 — an attachment id names nothing, and the name is the only basis an agent has for knowing
/// which file it is holding. The bytes themselves are not here: they are the least trustworthy
/// text on a ticket, and they belong in the delimited region and nowhere else.
/// </summary>
internal sealed record AttachmentOutput : ToolOutput
{
    [JsonPropertyName("attachmentId")]
    public string? AttachmentId { get; init; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; init; }

    /// <summary>
    /// What Jira claims the file is. Advisory, and named so in the prose too: nothing branches on
    /// it, because legacy instances report it wrongly often enough that a caller which trusted it
    /// would skip readable files and try to read unreadable ones.
    /// </summary>
    [JsonPropertyName("mediaType")]
    public string? MediaType { get; init; }

    /// <summary>The whole file's size in bytes, as Jira records it.</summary>
    [JsonPropertyName("size")]
    public long? Size { get; init; }

    /// <summary>
    /// Whether the bytes were decided to be unreadable, by inspecting them rather than by
    /// believing <see cref="MediaType"/>. A binary is described and never inlined.
    /// </summary>
    [JsonPropertyName("binary")]
    public bool? Binary { get; init; }

    /// <summary>Where this window started. Absent on a binary, which has no window.</summary>
    [JsonPropertyName("offset")]
    public long? Offset { get; init; }

    /// <summary>How many bytes this window carried.</summary>
    [JsonPropertyName("bytes")]
    public int? Bytes { get; init; }

    /// <summary>Where the next window starts. Absent once the file is read out.</summary>
    [JsonPropertyName("nextOffset")]
    public long? NextOffset { get; init; }

    [JsonPropertyName("bytesRemaining")]
    public long? BytesRemaining { get; init; }
}
