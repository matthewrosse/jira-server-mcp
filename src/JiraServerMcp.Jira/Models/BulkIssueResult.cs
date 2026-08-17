namespace JiraServerMcp.Jira.Models;

/// <summary>
/// One key's outcome from a bulk issue read: the issue Jira returned, or the exception that
/// explains why it did not. Each key resolves on its own, so a bulk read's result is a list of
/// these rather than a single success or failure for the whole call.
/// </summary>
public sealed record BulkIssueResult(string Key, JiraIssueDetail? Issue, Exception? Failure)
{
    public bool Succeeded => Issue is not null;
}
