namespace JiraServerMcp.Jira.Resilience;

/// <summary>
/// Per-request instructions to the resilience pipeline.
/// </summary>
public static class JiraRequestOptions
{
    /// <summary>
    /// Marks a request that changes nothing in Jira and may therefore be repeated, whatever its
    /// HTTP method. Only the POST form of search sets it: that endpoint is a read that Jira
    /// exposes as a POST solely because a long JQL does not fit in a URL. Nothing that writes may
    /// ever carry this — a repeated write creates the same issue twice.
    /// </summary>
    public static readonly HttpRequestOptionsKey<bool> RetrySafe = new("JiraRetrySafe");
}
