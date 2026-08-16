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

/// <summary>
/// What Jira answers with when a comment has been added. The body is deliberately not carried back:
/// the caller wrote it and does not need it echoed.
/// </summary>
/// <param name="Id">The identifier Jira gave the comment.</param>
/// <param name="Created">Jira's own timestamp, in the form Jira wrote it.</param>
public sealed record JiraAddedComment(string Id, string Created);

/// <summary>
/// What Jira answers with when work has been logged.
/// </summary>
/// <param name="Id">The identifier Jira gave the worklog entry.</param>
/// <param name="TimeSpent">
/// The duration as Jira recorded it, which is what says how it read the duration it was given.
/// </param>
public sealed record JiraAddedWorklog(string Id, string TimeSpent);
