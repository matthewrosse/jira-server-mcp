namespace JiraServerMcp.Tools;

/// <summary>
/// The query behind <c>jira_my_open_issues</c>: everything this account has open, most recently
/// updated first, optionally narrowed to one project. Pure, as <see cref="ChangeFeed"/>'s JQL is —
/// the query a canned tool authors is the part worth proving, and proving it should not need a
/// Jira behind it.
/// </summary>
internal static class MyOpenIssues
{
    private const string BaseJql =
        "assignee = currentUser() AND resolution = Unresolved ORDER BY updated DESC";

    /// <summary>
    /// The query, narrowed to <paramref name="project"/> where one was given. The key is checked
    /// against <see cref="ProjectKey"/> before it reaches here; this is the interpolation, not the
    /// grammar.
    /// </summary>
    public static string Jql(string? project) =>
        project is null ? BaseJql : $"project = {project} AND {BaseJql}";
}
