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
    /// The window, framed and delimited, or a description of a file whose bytes are not text. A
    /// binary is described rather than inlined — its bytes would cost the caller its context to
    /// learn nothing, and there is nothing an agent can do with a truncated PNG.
    /// </summary>
    public static Rendered Render(JiraAttachment attachment, ReadOnlySpan<byte> window, long offset)
    {
        var sniff = window[..Math.Min(window.Length, ResponseBudget.AttachmentSniff)];

        if (!AttachmentText.IsText(sniff))
        {
            return Binary(attachment);
        }

        var usable = AttachmentText.Usable(window);
        var read = offset + usable;
        var remaining = Math.Max(attachment.Size - read, 0);
        var more = usable > 0 && remaining > 0;

        return new Rendered(
            UntrustedContent.Envelope(Header(attachment, offset, usable, remaining, more),
                AttachmentText.Read(window[..usable])),
            ToolOutputs.Node(new AttachmentOutput
            {
                Outcome = Outcomes.Ok,
                AttachmentId = attachment.Id,
                FileName = attachment.FileName,
                MediaType = attachment.MimeType,
                Size = attachment.Size,
                Binary = false,
                Offset = offset,
                Bytes = usable,
                NextOffset = more ? read : null,
                BytesRemaining = remaining,
            }));
    }

    /// <summary>
    /// What is known about a file that cannot be read as text. The media type is Jira's claim and
    /// is named as such: it played no part in this answer, which was decided by the bytes.
    /// </summary>
    private static Rendered Binary(JiraAttachment attachment) =>
        new(
            $"{attachment.FileName} is not text — its bytes do not read as UTF-8 — so it is "
            + $"described rather than read. It is {attachment.Size} bytes"
            + (attachment.MimeType is { Length: > 0 } claimed
                ? $", and Jira claims it is {claimed}."
                : ", and Jira claims no media type for it.")
            + " Nothing was decoded, and nothing below this line came out of the file.",
            ToolOutputs.Node(new AttachmentOutput
            {
                Outcome = Outcomes.Ok,
                AttachmentId = attachment.Id,
                FileName = attachment.FileName,
                MediaType = attachment.MimeType,
                Size = attachment.Size,
                Binary = true,
            }));

    private static string Header(
        JiraAttachment attachment,
        long offset,
        int usable,
        long remaining,
        bool more)
    {
        if (usable is 0)
        {
            return $"{attachment.FileName}: nothing to read at byte {offset} — the file is "
                   + $"{attachment.Size} bytes.";
        }

        var read = $"{attachment.FileName}, bytes {offset}-{offset + usable - 1} of "
                   + $"{attachment.Size}";

        return more
            ? $"{read} — {remaining} bytes remain; ask for them with offset: {offset + usable}."
            : $"{read} — the rest of the file.";
    }
}
