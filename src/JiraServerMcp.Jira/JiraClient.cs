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
    /// One issue, carrying the fields asked for and the sections named in <paramref name="expand"/>.
    /// Both are named by the caller and sent in a single request, because Jira's expand mechanism
    /// covers every section this client needs and a second round trip buys nothing.
    /// </summary>
    public async Task<JiraIssueDetail> GetIssueAsync(
        string key,
        IReadOnlyList<string> fields,
        IReadOnlyList<string> expand,
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

        return IssueDetailReader.Read(document.RootElement);
    }

    /// <summary>
    /// Every project this account can see. Jira answers with all of them at once — the platform API
    /// offers no page here — so the caller decides what to do with a very large instance.
    /// </summary>
    public Task<IReadOnlyList<JiraProject>> ListProjectsAsync(CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<JiraProject>>("rest/api/2/project", cancellationToken);

    /// <summary>
    /// One project with its issue types, their statuses, its components, and its versions. Jira
    /// keeps them behind four endpoints; an agent preparing a write needs all four, and four tool
    /// calls to collect them is exactly the mechanical REST mapping this server is not.
    /// </summary>
    public async Task<JiraProjectDetail> GetProjectAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var path = $"rest/api/2/project/{Uri.EscapeDataString(key)}";

        var project = await GetAsync<JiraProjectResponse>(path, cancellationToken)
            .ConfigureAwait(false);

        var issueTypes = await GetAsync<IReadOnlyList<JiraIssueTypeStatuses>>(
            $"{path}/statuses",
            cancellationToken).ConfigureAwait(false);

        var components = await GetAsync<IReadOnlyList<JiraProjectComponent>>(
            $"{path}/components",
            cancellationToken).ConfigureAwait(false);

        var versions = await GetAsync<IReadOnlyList<JiraProjectVersion>>(
            $"{path}/versions",
            cancellationToken).ConfigureAwait(false);

        return new JiraProjectDetail(
            Project: new JiraProject(
                project.Key,
                project.Name,
                project.Id,
                project.ProjectTypeKey),
            Description: project.Description,
            Lead: project.Lead?.DisplayName,
            IssueTypes: issueTypes,
            Components: components,
            Versions: versions);
    }

    /// <summary>
    /// What Jira will accept when an issue of one type is created in one project, or null when it
    /// knows neither the project nor the type.
    /// </summary>
    public async Task<JiraCreateFields?> GetCreateFieldsAsync(
        string projectKey,
        string issueTypeName,
        CancellationToken cancellationToken)
    {
        var query = "rest/api/2/issue/createmeta"
                    + $"?projectKeys={Uri.EscapeDataString(projectKey)}"
                    + $"&issuetypeNames={Uri.EscapeDataString(issueTypeName)}"
                    + "&expand=projects.issuetypes.fields";

        using var response = await httpClient
            .GetAsync(query, cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        using var document = await response.Content
                                 .ReadFromJsonAsync<JsonDocument>(cancellationToken)
                                 .ConfigureAwait(false)
                             ?? throw new InvalidOperationException(
                                 "Jira returned an empty body for /rest/api/2/issue/createmeta.");

        return CreateFieldsReader.Read(document.RootElement);
    }

    /// <summary>
    /// Users matching part of a name. Jira Server keys users by <c>name</c> and <c>key</c>, not by
    /// the account identifier Cloud returns, and it leaves inactive users out unless asked.
    /// </summary>
    public Task<IReadOnlyList<JiraUser>> SearchUsersAsync(
        string query,
        int startAt,
        int maxResults,
        bool includeInactive,
        CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<JiraUser>>(
            $"rest/api/2/user/search?username={Uri.EscapeDataString(query)}"
            + $"&startAt={startAt}&maxResults={maxResults}"
            + $"&includeInactive={(includeInactive ? "true" : "false")}",
            cancellationToken);

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
