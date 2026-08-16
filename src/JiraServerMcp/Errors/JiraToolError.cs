using System.Net;
using JiraServerMcp.Jira.Errors;

namespace JiraServerMcp.Errors;

/// <summary>
/// Turns a Jira failure into something the caller can act on. An agent cannot read this server's
/// log and cannot ask a human, so every message says which failure it was and what to do next —
/// including, where it applies, that there is nothing to do and looping will not help.
/// </summary>
internal static class JiraToolError
{
    public static string Describe(JiraApiException exception, string profileName, string operation)
        => exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                $"The personal access token for profile '{profileName}' is invalid or revoked. "
                + $"Run 'jira-server-mcp auth login {profileName}' to store a new one. "
                + Jira(exception),

            HttpStatusCode.Forbidden =>
                $"Jira refused {operation}: the account this server is authenticated as does not "
                + $"have permission for it on {exception.Endpoint}. The request was not retried, "
                + $"and repeating it will not help. {Jira(exception)}",

            HttpStatusCode.NotFound when IsAnIssue(exception.Endpoint) =>
                $"Jira has no issue at {exception.Endpoint} that this account can see. Jira "
                + "answers the same way whether the issue does not exist and whether it exists "
                + "but you cannot see it, so there is nothing to retry: check the issue key, or "
                + "ask someone with access to it.",

            HttpStatusCode.NotFound when IsAProject(exception.Endpoint) =>
                $"Jira has no project at {exception.Endpoint} that this account can see. Jira "
                + "answers the same way whether the project does not exist and whether it exists "
                + "but you cannot browse it, so there is nothing to retry: check the project key "
                + "with jira_list_projects, or ask someone with access to it.",

            HttpStatusCode.NotFound =>
                $"Jira has nothing at {exception.Endpoint} (404). Check the profile's base URL, "
                + "including any context path such as /jira.",

            HttpStatusCode.BadRequest when exception.FieldErrors.Count > 0 =>
                $"Jira rejected {operation}. Its own message for each field follows.\n"
                + string.Join(
                    "\n",
                    exception.FieldErrors.Select(field => $"{field.Key}: {field.Value}")),

            _ => $"{operation} failed. {exception.Message}",
        };

    /// <summary>
    /// An endpoint naming one issue. The create metadata lives under the same path and names no
    /// issue at all, so telling its caller to check an issue key would send it looking for one it
    /// never sent.
    /// </summary>
    private static bool IsAnIssue(string endpoint) =>
        endpoint.Contains("/rest/api/2/issue/", StringComparison.Ordinal)
        && !endpoint.Contains("/rest/api/2/issue/createmeta", StringComparison.Ordinal);

    private static bool IsAProject(string endpoint) =>
        endpoint.Contains("/rest/api/2/project/", StringComparison.Ordinal);

    private static string Jira(JiraApiException exception) =>
        exception.ErrorMessages.Count is 0
            ? $"Jira returned {(int)exception.StatusCode}."
            : $"Jira returned {(int)exception.StatusCode} and said: "
              + string.Join(" ", exception.ErrorMessages);
}
