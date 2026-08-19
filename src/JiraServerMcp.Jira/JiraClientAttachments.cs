using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Jira;

/// <summary>
/// Attachments, which on a legacy Jira are regularly where the specification actually is. Reading
/// one is two requests: what Jira says the file is, and the bytes themselves, which are served
/// from a path of Jira's own composing rather than from the platform API.
/// </summary>
public sealed partial class JiraClient
{
    /// <summary>How much is read at a time while skipping to a window a caller asked to resume at.</summary>
    private const int SkipBuffer = 8_192;

    /// <summary>
    /// One attachment's metadata: what it is called, how large it is, and where Jira serves it.
    /// </summary>
    public async Task<JiraAttachment> GetAttachmentAsync(
        string id,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .GetAsync($"rest/api/2/attachment/{Uri.EscapeDataString(id)}", cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        using var document = await response.Content
                                 .ReadFromJsonAsync<JsonDocument>(cancellationToken)
                                 .ConfigureAwait(false)
                             ?? throw new InvalidOperationException(
                                 $"Jira returned an empty body for attachment {id}.");

        return IssueDetailReader.ReadAttachment(document.RootElement, id);
    }

    /// <summary>
    /// One window of an attachment's bytes, starting at <paramref name="offset"/>. A range is
    /// asked for so that resuming a large file does not fetch everything before it again, and the
    /// response is read only as far as the window needs — a Jira or a proxy that ignores the range
    /// header answers with the whole file, and there is nothing to gain by buffering the rest.
    /// </summary>
    /// <remarks>
    /// Jira composes the content URL from its own configured base URL, which on a Jira behind a
    /// proxy is regularly not the base this server was pointed at. Only the path is taken from it
    /// and resolved against the configured base: the request then carries this profile's
    /// credential over this profile's certificate authority bundle, and a misconfigured — or
    /// tampered-with — instance cannot direct the token somewhere else.
    /// </remarks>
    public async Task<byte[]> ReadAttachmentAsync(
        JiraAttachment attachment,
        long offset,
        int window,
        CancellationToken cancellationToken)
    {
        // Nothing to ask for past the end of a file whose size Jira gave: a range that starts
        // there is unsatisfiable, and asking is a round trip to be told so.
        if (attachment.Size > 0 && offset >= attachment.Size)
        {
            return [];
        }

        var content = Content(attachment);

        using var request = new HttpRequestMessage(HttpMethod.Get, content);

        // The range stops at the last byte of the file rather than at the width of the window.
        // Jira's attachment servlet answers a range that overruns the file by declaring the
        // *requested* length and then sending only what it has, which reads as a connection that
        // died mid-response. Asking only for bytes that exist avoids provoking it.
        request.Headers.Range = new RangeHeaderValue(offset, Last(attachment, offset, window));

        using var response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        // A range starting at or past the end of the file is unsatisfiable, and a server says so
        // with a 416 rather than with an empty body. That is the honest answer to "what is at byte
        // N of an N-byte file", so it is read as no bytes rather than raised as a failure.
        if (response.StatusCode is HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            return [];
        }

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            if (response.StatusCode is not HttpStatusCode.PartialContent)
            {
                await Skip(stream, offset, cancellationToken).ConfigureAwait(false);
            }

            return await Window(stream, window, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Where the bytes are, as a request this client will make. Jira composes the content URL from
    /// its own configured base URL, so only the path is taken from it and resolved against the base
    /// this profile was configured with — an instance behind a proxy regularly names a host this
    /// server was never pointed at, and a tampered-with one would name a host of its choosing.
    /// A relative path is resolved the same way rather than refused: what is used is the path
    /// either way.
    /// </summary>
    private Uri Content(JiraAttachment attachment)
    {
        if (attachment.Content is not { Length: > 0 } url)
        {
            throw new InvalidOperationException(
                $"Jira did not say where attachment {attachment.Id} is served.");
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            ? new Uri(httpClient.BaseAddress!, absolute.PathAndQuery)
            : new Uri(httpClient.BaseAddress!, url);
    }

    /// <summary>
    /// The last byte worth asking for: the end of the window, or the end of the file where Jira
    /// said how long it is.
    /// </summary>
    private static long Last(JiraAttachment attachment, long offset, int window) =>
        attachment.Size > 0
            ? Math.Min(offset + window - 1, attachment.Size - 1)
            : offset + window - 1;

    /// <summary>
    /// Reads past bytes the caller has already seen, for a Jira that answered with the whole file
    /// where a range was asked for.
    /// </summary>
    private static async Task Skip(Stream stream, long offset, CancellationToken cancellationToken)
    {
        var discard = new byte[SkipBuffer];

        while (offset > 0)
        {
            var read = await stream
                .ReadAsync(
                    discard.AsMemory(0, (int)Math.Min(offset, SkipBuffer)),
                    cancellationToken)
                .ConfigureAwait(false);

            if (read is 0)
            {
                return;
            }

            offset -= read;
        }
    }

    /// <summary>
    /// The window itself, or as much of it as the file had left. A short read is not the end of
    /// the stream, so this reads until the window is full or the file runs out.
    /// </summary>
    private static async Task<byte[]> Window(
        Stream stream,
        int window,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[window];
        var filled = 0;

        while (filled < window)
        {
            int read;

            try
            {
                read = await stream
                    .ReadAsync(buffer.AsMemory(filled, window - filled), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpIOException)
            {
                // A server that promised more bytes than it sent. The bytes that did arrive are
                // the file, and losing them over the promise would turn a readable attachment into
                // a failure the caller can do nothing about.
                break;
            }

            if (read is 0)
            {
                break;
            }

            filled += read;
        }

        return filled == window ? buffer : buffer[..filled];
    }
}
