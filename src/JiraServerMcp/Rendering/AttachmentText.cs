using System.Text;

namespace JiraServerMcp.Rendering;

/// <summary>
/// Whether an attachment's bytes are text an agent can read, decided by inspecting the bytes
/// rather than by believing the media type Jira reports. Legacy instances label files wrongly
/// often enough — plain text uploaded through some browsers arrives as
/// <c>application/octet-stream</c>, and a screenshot renamed to <c>.log</c> arrives as
/// <c>text/plain</c> — that a rule built on the label fails on exactly the deployments this
/// project exists for.
/// </summary>
/// <remarks>
/// Every decision here is made over the whole window that will be rendered, never a sample of it.
/// A sample would pass a file whose first pages are a text preamble — a SQL dump before its first
/// blob, an mbox before its first attachment — and the bytes after it would be decoded and handed
/// to the agent as text, which is the one thing this module exists to prevent.
/// </remarks>
internal static class AttachmentText
{
    /// <summary>
    /// A NUL byte, which no UTF-8 text carries and nearly every binary format does. Other control
    /// characters are deliberately allowed: a log full of form feeds or escape sequences is still
    /// a log, and refusing it would cost the caller the file it actually asked for.
    /// </summary>
    private const byte Nul = 0;

    /// <summary>
    /// Whether this window reads as UTF-8 text. The window is a fixed number of bytes cut out of a
    /// file, so a character its end cut in half is not evidence of anything — that character
    /// belongs to the next window. Anything else that will not decode is.
    /// </summary>
    public static bool IsText(ReadOnlySpan<byte> window) =>
        window.IndexOf(Nul) < 0 && Usable(window) == Whole(window);

    /// <summary>
    /// How much of the window is whole, valid text: the length of the longest prefix that decodes.
    /// For a window that is text throughout, that is all of it bar a character the end cut in
    /// half, which the next window will carry.
    /// </summary>
    public static int Usable(ReadOnlySpan<byte> window)
    {
        if (Decodes(window))
        {
            return window.Length;
        }

        var trimmed = Trailing(window);

        return Decodes(window[..trimmed]) ? trimmed : Valid(window);
    }

    /// <summary>
    /// Reads the window as UTF-8. Called with what <see cref="Usable"/> admitted, so there is no
    /// half character at the end and nothing invalid inside to replace.
    /// </summary>
    public static string Read(ReadOnlySpan<byte> window) => Encoding.UTF8.GetString(window);

    /// <summary>
    /// The whole of the window bar a character its end cut in half. What <see cref="IsText"/>
    /// compares against: a window is text when everything except that half character decodes.
    /// </summary>
    private static int Whole(ReadOnlySpan<byte> window) =>
        Decodes(window) ? window.Length : Trailing(window);

    /// <summary>
    /// The longest prefix that decodes, found by bisection rather than by walking: a window is
    /// sixteen thousand bytes, and this runs on every fetch.
    /// </summary>
    /// <remarks>
    /// Reached only for a window that is not text, where it answers "how far in does the text
    /// stop" — which is what lets a caller be told where the readable part ended instead of being
    /// handed bytes that were never text.
    /// </remarks>
    private static int Valid(ReadOnlySpan<byte> window)
    {
        var (low, high) = (0, window.Length);

        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);

            if (Decodes(window[..middle]))
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

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
