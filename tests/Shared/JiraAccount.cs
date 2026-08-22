namespace JiraServerMcp.Tests.Support;

/// <summary>
/// Jira's answer to <c>/rest/api/2/myself</c> — the account a stored token belongs to, declared as
/// fully as a real Jira declares one. A thinner payload teaches every later author a thinner Jira
/// than they will meet, which is why every test project reads the shape from here rather than
/// spelling its own.
/// </summary>
internal static class JiraAccount
{
    /// <summary>
    /// The account payload, with the fields Jira Server returns for the authenticated user.
    /// </summary>
    /// <param name="timeZone">
    /// The account's own zone, which is the one Jira reads a JQL date literal in. A builder rather
    /// than a constant because a Jira that reports no zone at all is a case one test is about, and
    /// the absence is that test's assertion. The default keeps the zone: an instance that answers
    /// without one is the unusual one.
    /// </param>
    public static string Payload(string? timeZone = "Europe/Warsaw") => $$"""
        {
          "self": "http://localhost/rest/api/2/user?username=ada",
          "key": "JIRAUSER10100",
          "name": "ada",
          "emailAddress": "ada@example.com",
          "displayName": "Ada Lovelace",
          "active": true{{(timeZone is null ? "" : $",\n  \"timeZone\": \"{timeZone}\"")}}
        }
        """;
}
