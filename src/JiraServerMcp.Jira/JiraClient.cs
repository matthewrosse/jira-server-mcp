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

    /// <summary>
    /// Creates one issue and returns the key Jira gave it. Never retried: a repeated create is a
    /// second issue, and Jira offers nothing to make it idempotent.
    /// </summary>
    public async Task<JiraCreatedIssue> CreateIssueAsync(
        string projectKey,
        string issueTypeName,
        string summary,
        IReadOnlyDictionary<string, JsonElement> fields,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["project"] = new Dictionary<string, string> { ["key"] = projectKey },
            ["issuetype"] = new Dictionary<string, string> { ["name"] = issueTypeName },
            ["summary"] = summary,
        };

        foreach (var (name, value) in fields)
        {
            body[name] = value;
        }

        using var request = WriteFields(HttpMethod.Post, "rest/api/2/issue", body);

        using var response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        return await response.Content
                   .ReadFromJsonAsync<JiraCreatedIssue>(cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException(
                   "Jira returned an empty body for a created issue.");
    }

    /// <summary>
    /// Changes the named fields of one issue, and its assignee in the same request. A field whose
    /// value is JSON null is cleared; a field not named is left alone. Never retried.
    /// </summary>
    public async Task UpdateIssueAsync(
        string key,
        IReadOnlyDictionary<string, JsonElement> fields,
        JiraAssignee? assignee,
        CancellationToken cancellationToken)
    {
        var body = fields.ToDictionary(
            field => field.Key,
            field => (object?)field.Value,
            StringComparer.Ordinal);

        if (assignee is { } assigned)
        {
            body["assignee"] = assigned.Name is { } name
                ? new Dictionary<string, string> { ["name"] = name }
                : null;
        }

        using var request = WriteFields(
            HttpMethod.Put,
            $"rest/api/2/issue/{Uri.EscapeDataString(key)}",
            body);

        using var response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The transitions this account can make on this issue right now, named and numbered. Read at
    /// the moment of transitioning: what was available when the issue was read may not be
    /// available now. The screens are not asked for — the issue read's transitions expansion is
    /// where those belong, and they are the largest part of the response.
    /// </summary>
    public async Task<IReadOnlyList<JiraTransition>> ListTransitionsAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var query = $"rest/api/2/issue/{Uri.EscapeDataString(key)}/transitions";

        using var response = await httpClient
            .GetAsync(query, cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        using var document = await response.Content
                                 .ReadFromJsonAsync<JsonDocument>(cancellationToken)
                                 .ConfigureAwait(false)
                             ?? throw new InvalidOperationException(
                                 $"Jira returned an empty body for the transitions of {key}.");

        return IssueDetailReader.ReadTransitions(document.RootElement);
    }

    /// <summary>
    /// Performs one transition, carrying its screen's fields and a comment in the same request so
    /// that a transition demanding either succeeds in one call. Never retried.
    /// </summary>
    public async Task TransitionIssueAsync(
        string key,
        string transitionId,
        IReadOnlyDictionary<string, JsonElement> fields,
        string? comment,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["transition"] = new Dictionary<string, string> { ["id"] = transitionId },
        };

        if (fields.Count > 0)
        {
            body["fields"] = fields;
        }

        if (comment is not null)
        {
            body["update"] = new Dictionary<string, object?>
            {
                ["comment"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["add"] = new Dictionary<string, string> { ["body"] = comment },
                    },
                },
            };
        }

        using var request = Write(
            HttpMethod.Post,
            $"rest/api/2/issue/{Uri.EscapeDataString(key)}/transitions",
            body);

        using var response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds one comment and returns the identifier and timestamp Jira gave it. Never retried: a
    /// repeated comment is a second comment.
    /// </summary>
    public Task<JiraAddedComment> AddCommentAsync(
        string key,
        string body,
        CancellationToken cancellationToken) =>
        PostAsync<JiraAddedComment>(
            $"rest/api/2/issue/{Uri.EscapeDataString(key)}/comment",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["body"] = body },
            cancellationToken);

    /// <summary>
    /// Logs work against one issue. <paramref name="timeSpent"/> is Jira's own duration syntax, in
    /// which Jira alone decides how long a day is. Never retried.
    /// </summary>
    public Task<JiraAddedWorklog> AddWorklogAsync(
        string key,
        string timeSpent,
        string? started,
        string? comment,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["timeSpent"] = timeSpent,
        };

        if (started is not null)
        {
            body["started"] = started;
        }

        if (comment is not null)
        {
            body["comment"] = comment;
        }

        return PostAsync<JiraAddedWorklog>(
            $"rest/api/2/issue/{Uri.EscapeDataString(key)}/worklog",
            body,
            cancellationToken);
    }

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
