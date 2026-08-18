using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Jira;

/// <summary>
/// Links: to another issue, typed and directional, and to a URL outside Jira, identified by the
/// URL itself so that attaching one twice updates one link.
/// </summary>
public sealed partial class JiraClient
{
    /// <summary>
    /// Every link type this Jira publishes, with the wording for both ends of each. Read before a
    /// link is made, because Jira's own endpoint takes a type name and a direction and the
    /// relation phrase is what resolves into them.
    /// </summary>
    public async Task<IReadOnlyList<JiraIssueLinkType>> ListIssueLinkTypesAsync(
        CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .GetAsync("rest/api/2/issueLinkType", cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        using var document = await response.Content
                                 .ReadFromJsonAsync<JsonDocument>(cancellationToken)
                                 .ConfigureAwait(false)
                             ?? throw new InvalidOperationException(
                                 "Jira returned an empty body for /rest/api/2/issueLinkType.");

        return IssueDetailReader.ReadIssueLinkTypes(document.RootElement);
    }

    /// <summary>
    /// Links two issues, carrying an optional comment in the same request. Which key is which is
    /// settled by the caller: Jira reads the link as <paramref name="outwardKey"/> doing the type's
    /// outward wording to <paramref name="inwardKey"/>. Never retried.
    /// </summary>
    public async Task LinkIssuesAsync(
        string typeName,
        string outwardKey,
        string inwardKey,
        string? comment,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = new Dictionary<string, string> { ["name"] = typeName },
            ["outwardIssue"] = new Dictionary<string, string> { ["key"] = outwardKey },
            ["inwardIssue"] = new Dictionary<string, string> { ["key"] = inwardKey },
        };

        if (comment is not null)
        {
            body["comment"] = new Dictionary<string, string> { ["body"] = comment };
        }

        using var request = Write(HttpMethod.Post, "rest/api/2/issueLink", body);

        using var response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Attaches a URL to an issue, keyed by the URL itself, so attaching the same URL twice
    /// updates the one link rather than making a second. Answers whether the link was created,
    /// which Jira says by status code — 201 for a new link, 200 for one it already had.
    /// </summary>
    public async Task<bool> AddRemoteLinkAsync(
        string key,
        string url,
        string title,
        string? relationship,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["globalId"] = url,
            ["object"] = new Dictionary<string, string> { ["url"] = url, ["title"] = title },
        };

        if (relationship is not null)
        {
            body["relationship"] = relationship;
        }

        using var request = Write(
            HttpMethod.Post,
            $"rest/api/2/issue/{Uri.EscapeDataString(key)}/remotelink",
            body);

        using var response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        return response.StatusCode is HttpStatusCode.Created;
    }
}
