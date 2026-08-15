using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Jira.Resilience;

namespace JiraServerMcp.Jira;

/// <summary>
/// Typed client over the Jira Server platform API.
/// </summary>
public sealed class JiraClient(HttpClient httpClient)
{
    /// <summary>
    /// The longest search URI worth attempting. Jira Server itself accepts more, but the proxies
    /// and load balancers in front of a corporate instance routinely cut off around 4 KB, and a
    /// truncated URI comes back as a bare 400 with nothing to act on.
    /// </summary>
    private const int LongestSearchUri = 2_000;

    /// <summary>
    /// The Jira account the configured personal access token belongs to.
    /// </summary>
    public async Task<JiraUser> GetMyselfAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .GetAsync("rest/api/2/myself", cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        return await response.Content
                   .ReadFromJsonAsync<JiraUser>(cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException(
                   "Jira returned an empty body for /rest/api/2/myself.");
    }

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
    /// A GET while the query fits in a URL, and the POST form once it does not. Jira's own limit
    /// is whatever sits in front of it — a proxy rejecting a long URI — so the switch happens well
    /// before any of them complain.
    /// </summary>
    private HttpRequestMessage SearchRequest(
        string jql,
        int startAt,
        int maxResults,
        IReadOnlyList<string> fields)
    {
        var query =
            $"rest/api/2/search?jql={Uri.EscapeDataString(jql)}&startAt={startAt}"
            + $"&maxResults={maxResults}&fields={Uri.EscapeDataString(string.Join(",", fields))}";

        if (new Uri(httpClient.BaseAddress!, query).AbsoluteUri.Length <= LongestSearchUri)
        {
            return new HttpRequestMessage(HttpMethod.Get, query);
        }

        var body = JsonSerializer.Serialize(new
        {
            jql,
            startAt,
            maxResults,
            fields,
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "rest/api/2/search")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        // The one POST in this client that may be repeated: it reads, and Jira offers it only
        // because a long JQL does not fit in a URL.
        request.Options.Set(JiraRequestOptions.RetrySafe, true);

        return request;
    }
}
