using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Resilience;

namespace JiraServerMcp.Jira;

/// <summary>
/// Typed client over the Jira Server platform API. One method per endpoint, split across
/// partial files by the resource each group of endpoints reads or writes (ADR-0006). This
/// file holds what the rest of them share: the request shapes, and the one limit that is a
/// property of the transport rather than of any endpoint.
/// </summary>
public sealed partial class JiraClient(HttpClient httpClient)
{
    /// <summary>
    /// The longest search URI worth attempting. Jira Server itself accepts more, but the proxies
    /// and load balancers in front of a corporate instance routinely cut off around 4 KB, and a
    /// truncated URI comes back as a bare 400 with nothing to act on.
    /// </summary>
    private const int LongestSearchUri = 2_000;

    /// <summary>
    /// A write posted as its body verbatim, rather than inside Jira's <c>fields</c> envelope, and
    /// sent exactly once.
    /// </summary>
    private async Task<T> PostAsync<T>(
        string path,
        IReadOnlyDictionary<string, object?> body,
        CancellationToken cancellationToken)
    {
        using var request = Write(HttpMethod.Post, path, body);

        using var response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        return await response.Content
                   .ReadFromJsonAsync<T>(cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException(
                   $"Jira returned an empty body for /{path}.");
    }

    /// <summary>
    /// A write, carrying Jira's <c>fields</c> envelope.
    /// </summary>
    private static HttpRequestMessage WriteFields(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, object?> fields) =>
        Write(method, path, new { fields });

    /// <summary>
    /// A write, carrying its body as it stands. The request is not marked as safe to repeat, so the
    /// resilience pipeline sends it exactly once.
    /// </summary>
    private static HttpRequestMessage Write(HttpMethod method, string path, object body) =>
        new(method, path)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"),
        };

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .GetAsync(path, cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        return await response.Content
                   .ReadFromJsonAsync<T>(cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException(
                   $"Jira returned an empty body for /{path}.");
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
