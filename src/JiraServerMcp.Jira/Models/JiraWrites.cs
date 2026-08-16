namespace JiraServerMcp.Jira.Models;

/// <summary>
/// What Jira answers with when an issue has been created. Deliberately small: the caller asked for
/// an issue to exist, and the key is what it needs to say so or to read it back.
/// </summary>
public sealed record JiraCreatedIssue(string Key, string Id);

/// <summary>
/// Who an issue is to be assigned to. A null <paramref name="Name"/> unassigns it; not passing a
/// <see cref="JiraAssignee"/> at all leaves the assignee alone.
/// </summary>
/// <param name="Name">The Jira Server username — <c>name</c>, not Cloud's account identifier.</param>
public sealed record JiraAssignee(string? Name);
