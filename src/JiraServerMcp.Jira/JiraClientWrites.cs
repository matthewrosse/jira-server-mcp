using System.Net.Http.Json;
using System.Text.Json;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;
namespace JiraServerMcp.Jira;

/// <summary>
/// The writes, and the transition read that immediately precedes one. None of these is ever
/// repeated: a retried write is a second issue, a second comment, a second logged hour.
/// </summary>
public sealed partial class JiraClient
{
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
    /// <remarks>
    /// <paramref name="leaveRemainingEstimate"/> sends <c>adjustEstimate=leave</c>, which stops
    /// Jira reducing the issue's remaining estimate by the time logged. It is left off the wire
    /// entirely when false, so what Jira does then is Jira's own default and not a value chosen
    /// here.
    /// </remarks>
    public Task<JiraAddedWorklog> AddWorklogAsync(
        string key,
        string timeSpent,
        string? started,
        string? comment,
        bool leaveRemainingEstimate,
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

        var path = $"rest/api/2/issue/{Uri.EscapeDataString(key)}/worklog";

        return PostAsync<JiraAddedWorklog>(
            leaveRemainingEstimate ? path + "?adjustEstimate=leave" : path,
            body,
            cancellationToken);
    }
}
