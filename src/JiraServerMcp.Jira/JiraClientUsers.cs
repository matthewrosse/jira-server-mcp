using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Jira;

/// <summary>
/// Finding a user. On Jira Server the answer is a username, which is what every write that names
/// a person must send.
/// </summary>
public sealed partial class JiraClient
{
    /// <summary>
    /// Users matching part of a name. Jira Server keys users by <c>name</c> and <c>key</c>, not by
    /// the account identifier Cloud returns, and it leaves inactive users out unless asked.
    /// <paramref name="assignableTo"/> — an issue key, or a project key, which carries no hyphen —
    /// switches the read to the users this Jira will accept as an assignee there, a subset of the
    /// directory because the permission lives on the project. Jira never offers an inactive user as
    /// an assignee and ignores <c>includeInactive</c> on that read, so it is not sent; an absent
    /// <paramref name="query"/> lists everyone assignable, where the plain search answers nothing.
    /// </summary>
    public Task<IReadOnlyList<JiraUser>> SearchUsersAsync(
        string? query,
        string? assignableTo,
        int startAt,
        int maxResults,
        bool includeInactive,
        CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<JiraUser>>(
            (assignableTo is null
                ? $"rest/api/2/user/search?includeInactive={(includeInactive ? "true" : "false")}"
                : "rest/api/2/user/assignable/search"
                  + $"?{(assignableTo.Contains('-', StringComparison.Ordinal) ? "issueKey" : "project")}"
                  + $"={Uri.EscapeDataString(assignableTo)}")
            + (query is null ? string.Empty : $"&username={Uri.EscapeDataString(query)}")
            + $"&startAt={startAt}&maxResults={maxResults}",
            cancellationToken);
}
