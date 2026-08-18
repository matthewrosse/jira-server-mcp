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
}
