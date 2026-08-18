using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Jira;

/// <summary>
/// The software API: boards, sprints and backlogs. Absent on a Jira Core instance, which is
/// normal rather than an error — the capability probe is what says which.
/// </summary>
public sealed partial class JiraClient
{
    /// <summary>
    /// One page of the boards this account can see. Reached over the software API, so a caller
    /// that has not read the capability probe first will meet a 404 on a Jira Core instance.
    /// </summary>
    public Task<JiraAgilePage<JiraBoard>> ListBoardsAsync(
        int startAt,
        int maxResults,
        CancellationToken cancellationToken) =>
        GetAsync<JiraAgilePage<JiraBoard>>(
            $"rest/agile/1.0/board?startAt={startAt}&maxResults={maxResults}",
            cancellationToken);

    /// <summary>
    /// One page of a board's sprints, whatever their state: an agent asking what to work on needs
    /// the active one, and an agent planning needs the future ones.
    /// </summary>
    public Task<JiraAgilePage<JiraSprint>> ListSprintsAsync(
        int boardId,
        int startAt,
        int maxResults,
        CancellationToken cancellationToken) =>
        GetAsync<JiraAgilePage<JiraSprint>>(
            $"rest/agile/1.0/board/{boardId}/sprint?startAt={startAt}&maxResults={maxResults}",
            cancellationToken);

    /// <summary>
    /// One page of the issues in a sprint. The software API answers this one the platform API's
    /// way — with a total — so it comes back as the same page type a JQL search does.
    /// </summary>
    public Task<JiraSearchPage> GetSprintIssuesAsync(
        int sprintId,
        int startAt,
        int maxResults,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken) =>
        GetAsync<JiraSearchPage>(
            $"rest/agile/1.0/sprint/{sprintId}/issue"
            + $"?startAt={startAt}&maxResults={maxResults}"
            + $"&fields={Uri.EscapeDataString(string.Join(",", fields))}",
            cancellationToken);

    /// <summary>
    /// One page of a board's backlog — the issues on the board that no sprint has taken.
    /// </summary>
    public Task<JiraSearchPage> GetBacklogAsync(
        int boardId,
        int startAt,
        int maxResults,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken) =>
        GetAsync<JiraSearchPage>(
            $"rest/agile/1.0/board/{boardId}/backlog"
            + $"?startAt={startAt}&maxResults={maxResults}"
            + $"&fields={Uri.EscapeDataString(string.Join(",", fields))}",
            cancellationToken);
}
