using System.Text;

namespace JiraServerMcp.Rendering;

/// <summary>
/// Whether an attachment's bytes are text an agent can read, decided by inspecting the bytes
/// rather than by believing the media type Jira reports. Legacy instances label files wrongly
/// often enough — every plain text file uploaded through some browsers arrives as
/// <c>application/octet-stream</c>, and a <c>.log</c> renamed from a screenshot arrives as
/// <c>text/plain</c> — that a rule built on the label fails on exactly the deployments this
/// project exists for.
/// </summary>
internal static class AttachmentText
{
    /// <summary>
    /// A NUL byte, which no UTF-8 text carries and nearly every binary format does. The other
    /// control characters are deliberately allowed: a log full of form feeds or escape sequences
    /// is still a log, and refusing it would cost the caller the file it actually asked for.
    /// </summary>
    private const byte Nul = 0;

    /// <summary>
    /// Whether this window of bytes reads as UTF-8 text. The window is the leading bytes of the
    /// file, so a sequence cut in half at its end is not evidence of anything — what is checked is
    /// whether the bytes decode, with a partial character at the boundary allowed for.
    /// </summary>
    public static bool IsText(ReadOnlySpan<byte> window)
    {
        if (window.IndexOf(Nul) >= 0)
        {
            return false;
        }

        // An empty file is readable text: it is a file with nothing in it, not a binary, and
        // telling the caller "this is a binary" would be a wrong answer to a real question.
        if (window.IsEmpty)
        {
            return true;
        }

        return Decodes(window) || Decodes(window[..Trailing(window)]);
    }

    /// <summary>
    /// How much of the window is whole text. A window is a fixed number of bytes, so it routinely
    /// ends in the middle of a multi-byte character; that character belongs to the next window,
    /// which is where the caller will resume from, rather than being decoded here as a replacement
    /// mark the caller cannot tell from one the file really contains.
    /// </summary>
    public static int Usable(ReadOnlySpan<byte> window) =>
        Decodes(window) ? window.Length : Trailing(window);

    /// <summary>
    /// Reads the window as UTF-8. Called with what <see cref="Usable"/> admitted, so there is no
    /// half character at the end to replace.
    /// </summary>
    public static string Read(ReadOnlySpan<byte> window) => Encoding.UTF8.GetString(window);

    private static bool Decodes(ReadOnlySpan<byte> window)
    {
        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(window);

            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// The length of the window with any character its end cut in half removed. UTF-8 marks a
    /// continuation byte as <c>10xxxxxx</c>, so the start of the last character is the last byte
    /// that is not one, and a character is at most four bytes long.
    /// </summary>
    private static int Trailing(ReadOnlySpan<byte> window)
    {
        for (var index = window.Length - 1; index >= window.Length - 4 && index >= 0; index--)
        {
            if ((window[index] & 0b1100_0000) != 0b1000_0000)
            {
                return index;
            }
        }

        return window.Length;
    }
}
