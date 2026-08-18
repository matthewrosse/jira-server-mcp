using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Jira.Resilience;
namespace JiraServerMcp.Jira;

/// <summary>
/// Reading issues: a JQL page, one issue with its sections, and the concurrent bulk read
/// ADR-0007 describes.
/// </summary>
public sealed partial class JiraClient
{
    /// <summary>
    /// One page of the issues a JQL query matches, carrying only the fields asked for.
    /// </summary>
    public async Task<JiraSearchPage> SearchAsync(
        string jql,
        int startAt,
        int maxResults,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken)
    {
        using var request = SearchRequest(jql, startAt, maxResults, fields);

        using var response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        return await response.Content
                   .ReadFromJsonAsync<JiraSearchPage>(cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException(
                   "Jira returned an empty body for /rest/api/2/search.");
    }

    /// <summary>
    /// One issue, carrying the fields asked for and the sections named in <paramref name="expand"/>.
    /// Both are named by the caller and sent in a single request, because Jira's expand mechanism
    /// covers every section this client needs and a second round trip buys nothing.
    /// </summary>
    public async Task<JiraIssueDetail> GetIssueAsync(
        string key,
        IReadOnlyList<string> fields,
        IReadOnlyList<string> expand,
        bool remoteLinks,
        CancellationToken cancellationToken)
    {
        var query = $"rest/api/2/issue/{Uri.EscapeDataString(key)}"
                    + $"?fields={Uri.EscapeDataString(string.Join(",", fields))}";

        if (expand.Count > 0)
        {
            query += $"&expand={Uri.EscapeDataString(string.Join(",", expand))}";
        }

        using var response = await httpClient
            .GetAsync(query, cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        using var document = await response.Content
                                 .ReadFromJsonAsync<JsonDocument>(cancellationToken)
                                 .ConfigureAwait(false)
                             ?? throw new InvalidOperationException(
                                 $"Jira returned an empty body for issue {key}.");

        var issue = IssueDetailReader.Read(document.RootElement);

        return remoteLinks
            ? issue with { RemoteLinks = await RemoteLinksAsync(key, cancellationToken) }
            : issue;
    }

    /// <summary>
    /// The issue's links out of Jira, which are not a field on the issue and so cost a request of
    /// their own. A refusal answers null rather than throwing: the caller opted into an extra
    /// section, and losing the whole issue read because this account may not see that section
    /// punishes it for asking for more.
    /// </summary>
    /// <remarks>
    /// A 404 degrades alongside the 403: Jira answers this endpoint that way both where the issue
    /// is invisible and where issue linking is switched off instance-wide, and the issue itself
    /// having just been read says the key is fine. A 401 is left to propagate, because a dead
    /// token is never a per-section outcome.
    /// </remarks>
    private async Task<IReadOnlyList<JiraRemoteLink>?> RemoteLinksAsync(
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient
                .GetAsync(
                    $"rest/api/2/issue/{Uri.EscapeDataString(key)}/remotelink",
                    cancellationToken)
                .ConfigureAwait(false);

            await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

            using var document = await response.Content
                .ReadFromJsonAsync<JsonDocument>(cancellationToken)
                .ConfigureAwait(false);

            return document is null
                ? []
                : IssueDetailReader.ReadRemoteLinks(document.RootElement);
        }
        catch (JiraApiException exception) when (exception.StatusCode is
            HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// The most GETs this client will have in flight for one bulk read. Twenty keys finish in four
    /// waves; an ageing Jira behind a reverse proxy is not the thing that breaks, and a wider burst
    /// would only provoke the retries <see cref="JiraRetryHandler"/> already performs.
    /// </summary>
    private const int BulkConcurrency = 5;

    /// <summary>
    /// Several issues, fetched as concurrent single-issue GETs rather than one JQL search: each
    /// key succeeds or fails on its own, and expansion behaviour cannot drift between a one-key
    /// call and a twenty-key one because both run the same code path. The key cap lives with the
    /// caller — this client fans out whatever list it is given.
    /// </summary>
    /// <remarks>
    /// A profile-level auth failure (401/403) is not a per-key outcome: if the token is dead,
    /// every key is doomed, and returning it as the failure of whichever key happened to hit it
    /// first would hide that fact behind an arbitrary key. It propagates instead, the same way a
    /// single-issue read's does.
    /// </remarks>
    public async Task<IReadOnlyList<BulkIssueResult>> GetIssuesAsync(
        IReadOnlyList<string> keys,
        IReadOnlyList<string> fields,
        IReadOnlyList<string> expand,
        bool remoteLinks,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(BulkConcurrency);

        return await Task.WhenAll(keys.Select(async key =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var issue = await GetIssueAsync(key, fields, expand, remoteLinks, cancellationToken)
                    .ConfigureAwait(false);

                return new BulkIssueResult(key, issue, null);
            }
            catch (JiraApiException exception) when (exception.StatusCode is not (
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
            {
                return new BulkIssueResult(key, null, exception);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                // This key's own 30s HttpClient.Timeout, not the caller hanging up: a slow key
                // degrades into a per-key timeout line while the rest still render.
                return new BulkIssueResult(key, null, exception);
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);
    }
}
