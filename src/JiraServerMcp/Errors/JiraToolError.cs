using System.Net;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Rendering;

namespace JiraServerMcp.Errors;

/// <summary>
/// Turns a Jira failure into something the caller can act on. An agent cannot read this server's
/// log and cannot ask a human, so every message says which failure it was and what to do next —
/// including, where it applies, that there is nothing to do and looping will not help.
///
/// Every message ends the same way: this server's own prose first, then — only when Jira said
/// anything at all — one <see cref="UntrustedContent"/>-framed region carrying every Jira-authored
/// word, error messages and field errors alike. Jira's words are never spliced into this server's
/// sentences: a validator message is admin-configurable content authored in Jira, exactly like a
/// description or a comment, and it does not stop being that because it arrived on a failure.
/// </summary>
internal static class JiraToolError
{
    public static string Describe(
        JiraApiException exception,
        string profileName,
        string operation,
        string? advice = null) =>
        Assembled(
            exception.StatusCode switch
            {
                HttpStatusCode.Unauthorized =>
                    $"The personal access token for profile '{profileName}' is invalid or "
                    + $"revoked. Run 'jira-server-mcp auth login {profileName}' to store a new "
                    + "one.",

                HttpStatusCode.Forbidden =>
                    $"Jira refused {operation}: the account this server is authenticated as does "
                    + $"not have permission for it on {exception.Endpoint}. The request was not "
                    + "retried, and repeating it will not help.",

                HttpStatusCode.NotFound when IsAnIssue(exception.Endpoint) =>
                    $"Jira has no issue at {exception.Endpoint} that this account can see. Jira "
                    + "answers the same way whether the issue does not exist and whether it "
                    + "exists but you cannot see it, so there is nothing to retry: check the "
                    + "issue key, or ask someone with access to it.",

                HttpStatusCode.NotFound when IsAProject(exception.Endpoint) =>
                    $"Jira has no project at {exception.Endpoint} that this account can see. "
                    + "Jira answers the same way whether the project does not exist and whether "
                    + "it exists but you cannot browse it, so there is nothing to retry: check "
                    + "the project key with jira_list_projects, or ask someone with access to it.",

                HttpStatusCode.NotFound when IsTheSoftwareApi(exception.Endpoint) =>
                    $"Jira answered {exception.Endpoint} with a 404. The whole software API "
                    + "answers that way where Jira Software is not licensed, so this instance "
                    + $"may have lost the licence the capability probe recorded. Run "
                    + $"'jira-server-mcp profile refresh {profileName}'; if it is still "
                    + "licensed, check the board or sprint identifier.",

                HttpStatusCode.NotFound =>
                    $"Jira has nothing at {exception.Endpoint} (404). Check the profile's base "
                    + "URL, including any context path such as /jira.",

                HttpStatusCode.BadRequest when exception.FieldErrors.Count > 0 =>
                    $"Jira rejected {operation}. Its own message for each field follows.",

                _ => $"{operation} failed.",
            },
            advice,
            exception);

    /// <summary>
    /// An endpoint naming one issue. The create metadata lives under the same path and names no
    /// issue at all, so telling its caller to check an issue key would send it looking for one it
    /// never sent.
    /// </summary>
    private static bool IsAnIssue(string endpoint) =>
        endpoint.Contains("/rest/api/2/issue/", StringComparison.Ordinal)
        && !endpoint.Contains("/rest/api/2/issue/createmeta", StringComparison.Ordinal);

    private static bool IsTheSoftwareApi(string endpoint) =>
        endpoint.Contains("/rest/agile/1.0/", StringComparison.Ordinal);

    private static bool IsAProject(string endpoint) =>
        endpoint.Contains("/rest/api/2/project/", StringComparison.Ordinal);

    /// <summary>
    /// This server's sentence, the caller's advice if it gave one, and the framed block of
    /// Jira's own words if Jira said anything — in that order, so trusted prose always reads
    /// before untrusted content and nothing appends past a closing marker.
    /// </summary>
    private static string Assembled(string sentence, string? advice, JiraApiException exception)
    {
        var parts = new List<string> { sentence };

        if (advice is not null)
        {
            parts.Add(advice);
        }

        if (FramedJiraWords(exception) is { } framed)
        {
            parts.Add(framed);
        }

        return string.Join("\n", parts);
    }

    /// <summary>
    /// The status line and, when Jira said anything, the delimited block of its own words —
    /// error messages and per-field errors joined whole, so a caller reading one block sees every
    /// word Jira wrote rather than half of it split across a trusted sentence.
    /// </summary>
    private static string? FramedJiraWords(JiraApiException exception)
    {
        var statusLine = $"Jira returned {(int)exception.StatusCode}.";

        if (JiraWords(exception) is not { } words)
        {
            return null;
        }

        return $"""
            {statusLine}
            {UntrustedContent.Preamble}
            {UntrustedContent.Delimit(Truncation.Error(words))}
            """;
    }

    private static string? JiraWords(JiraApiException exception)
    {
        var lines = new List<string>();

        lines.AddRange(exception.ErrorMessages);
        lines.AddRange(exception.FieldErrors.Select(field => $"{field.Key}: {field.Value}"));

        return lines.Count is 0 ? null : string.Join("\n", lines);
    }
}
