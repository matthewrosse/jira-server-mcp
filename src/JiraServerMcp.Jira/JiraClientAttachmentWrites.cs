using System.Text;
using System.Text.Json;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Jira;

/// <summary>
/// Attaching a file, which is the one write in this client that is not JSON: Jira's attachment
/// servlet takes a multipart body and answers with an array of the attachments it created. In its
/// own partial file rather than beside the reads, as ADR-0006 asks.
/// </summary>
public sealed partial class JiraClient
{
    /// <summary>
    /// Attaches one text file to an issue and returns it as Jira stored it. Never retried: Jira's
    /// attachment endpoint appends rather than replaces, so a repeat is a second file under the
    /// same name.
    /// </summary>
    public async Task<JiraAttachment> AddAttachmentAsync(
        string key,
        string fileName,
        string content,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();

        // Always text, never a type derived from the name: an agent-authored .html or .svg stored
        // under its "real" type is a stored-cross-site-scripting shape, and the read side already
        // treats the media type as advisory and branches on nothing.
        using var file = new StringContent(content, Encoding.UTF8, "text/plain");

        form.Add(file, "file", fileName);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"rest/api/2/issue/{Uri.EscapeDataString(key)}/attachments")
        {
            Content = form,
        };

        // On this request and on no other. Jira's attachment servlet refuses an upload without it,
        // as a cross-site request forgery guard; every other endpoint this client uses accepts a
        // bearer token without one, and putting the header on the HttpClient would state something
        // false about all of them.
        request.Headers.Add("X-Atlassian-Token", "no-check");

        using var response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        using var document = await JsonDocument
            .ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // One file was sent, so Jira's array carries one element. Anything else is an answer this
        // client has no way to report against the one file the caller named.
        if (document.RootElement.ValueKind is not JsonValueKind.Array
            || document.RootElement.GetArrayLength() is not 1)
        {
            throw new InvalidOperationException(
                $"Jira answered the upload of {fileName} to {key} with something other than one "
                + "attachment.");
        }

        var created = document.RootElement[0];

        return IssueDetailReader.ReadAttachment(created, Identifier(created, key, fileName));
    }

    /// <summary>
    /// The identifier Jira gave the file, taken as a string whichever way Jira wrote it. Jira's own
    /// two shapes for this value disagree — an issue's attachment field quotes the id and the
    /// attachment resource numbers it — and an upload nobody can name is an upload nobody can read
    /// back.
    /// </summary>
    private static string Identifier(JsonElement created, string key, string fileName)
    {
        if (created.TryGetProperty("id", out var identifier))
        {
            switch (identifier.ValueKind)
            {
                case JsonValueKind.String when identifier.GetString() is { Length: > 0 } quoted:
                    return quoted;

                case JsonValueKind.Number:
                    return identifier.GetRawText();
            }
        }

        throw new InvalidOperationException(
            $"Jira did not give an identifier to {fileName} on {key}.");
    }
}
