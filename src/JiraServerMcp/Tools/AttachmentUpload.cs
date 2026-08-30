namespace JiraServerMcp.Tools;

/// <summary>
/// What this server will accept as an attachment, and the sentence it refuses with. Six rules in
/// one tool is what ADR-0008 clause 3 calls a signal to extract a helper, and two of the six are
/// load-bearing rather than tidy: the file name lands in a multipart <c>Content-Disposition</c>
/// header, and a NUL in the content would create a file <c>jira_get_attachment</c> then refuses to
/// inline.
/// </summary>
internal static class AttachmentUpload
{
    /// <summary>
    /// The most content one call may carry, in characters.
    /// </summary>
    /// <remarks>
    /// A limit on a request rather than on an answer, so it lives here rather than with the
    /// response budget: by the time this server sees the content the agent has already spent those
    /// tokens, and what the cap buys is this server's own sentence in place of a Jira 413 or a
    /// silent four-megabyte success. 32,000 is the largest thing this server will ever say in one
    /// answer, so letting a caller send twice that is generous and still bounded, and it sits far
    /// below Jira's <c>jira.attachment.size</c> default of 10 MB — which leaves the operator's own
    /// configuration as the real ceiling.
    /// </remarks>
    public const int LongestContent = 64_000;

    /// <summary>
    /// The cap as the tool's own description states it. Written out here rather than in the
    /// description, one line from the number, because an attribute argument must be a constant and
    /// cannot interpolate one — and a test holds the two together.
    /// </summary>
    public const string ContentLimit = "At most 64000 characters in one call";

    /// <summary>The longest file name Jira's own attachment column holds.</summary>
    private const int LongestFileName = 255;

    /// <summary>
    /// Why this attachment will not be sent, or null where it will be. The file name is checked
    /// first: it is the one part of an upload that reaches a header rather than a body.
    /// </summary>
    public static string? Refuse(string key, string fileName, string content) =>
        RefuseFileName(key, fileName) ?? RefuseContent(key, fileName, content);

    /// <summary>
    /// A file name is a label a person reads on the issue, never a location — nothing here opens a
    /// file — so everything a human might reasonably type is accepted, spaces and Unicode
    /// included. What is refused is what would either break the header the name is written into or
    /// read as a path to someone downstream who does resolve one.
    /// </summary>
    private static string? RefuseFileName(string key, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return $"Nothing was attached to {key}: an attachment needs a file name, which is the "
                   + "label a person reads on the issue.";
        }

        if (fileName.Any(char.IsControl))
        {
            return $"Nothing was attached to {key}: a file name cannot contain a control "
                   + "character. It is written into the header that carries the file, and a line "
                   + "break or a NUL there would split it.";
        }

        if (fileName.Contains('/') || fileName.Contains('\\'))
        {
            return $"Nothing was attached to {key}: a file name cannot contain '/' or '\\'. It is "
                   + "a label on the issue rather than a path — send the name the file should "
                   + "carry, not where it sits on your disk.";
        }

        if (fileName is "." or "..")
        {
            return $"Nothing was attached to {key}: '{fileName}' names a directory rather than a "
                   + "file.";
        }

        if (fileName.Length > LongestFileName)
        {
            return $"Nothing was attached to {key}: a file name may be at most "
                   + $"{LongestFileName} characters, and this one is {fileName.Length}.";
        }

        return null;
    }

    /// <summary>
    /// Text only, and enough of it to be worth storing. The NUL rule is the one that is not
    /// arbitrary: it is precisely the marker the read side's sniffer uses to call bytes binary, so
    /// allowing it here would let this tool create files <c>jira_get_attachment</c> refuses to
    /// inline. Tabs, carriage returns and line feeds pass — a log has them.
    /// </summary>
    private static string? RefuseContent(string key, string fileName, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return $"An empty file was not attached to {key}. Jira would store it, and there would "
                   + "be nothing in it for anyone who downloaded it.";
        }

        if (content.Contains('\0'))
        {
            return $"{fileName} was not attached to {key}: its content carries a NUL, which is "
                   + "what marks a file as binary when one is read back. This tool writes text "
                   + "only.";
        }

        if (content.Length > LongestContent)
        {
            return $"{fileName} was not attached to {key}: it is {content.Length} characters, and "
                   + $"one attachment may carry at most {LongestContent}. Attach the part worth "
                   + "reading rather than the whole file.";
        }

        return null;
    }
}
