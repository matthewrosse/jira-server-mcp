using System.Net;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Rendering;

namespace JiraServerMcp.Errors;

/// <summary>
/// Turns a Jira failure into something the caller can act on. An agent cannot read this server's
/// log and cannot ask a human, so every message says which failure it was and what to do next —
/// including, where it applies, that there is nothing to do and looping will not help.
///
/// Every message ends the same way: this server's own prose first, then the caller's advice if it
/// gave one, then — everywhere but a bare 404, where Jira has nothing further to say — the status
/// line and, when Jira said anything at all, one <see cref="UntrustedContent"/>-framed region
/// carrying every Jira-authored word, error messages and field errors alike. Jira's words are
/// never spliced into this server's sentences: a validator message is admin-configurable content
/// authored in Jira, exactly like a description or a comment, and it does not stop being that
/// because it arrived on a failure.
/// </summary>
internal static class JiraToolError
{
    /// <remarks>
    /// <c>permission</c> is what Jira said about the key a refused write claimed, where it was
    /// asked and answered. This module stays a pure formatter: the lookup is
    /// <see cref="Tools.ToolCall"/>'s to orchestrate, and the answer arrives here as a parameter.
    /// </remarks>
    public static string Describe(
        JiraApiException exception,
        string profileName,
        string operation,
        string? advice = null,
        PermissionAnswer? permission = null) =>
        exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                Assembled(
                    $"The personal access token for profile '{profileName}' is invalid or "
                    + $"revoked. Run 'jira-server-mcp auth login {profileName}' to store a new "
                    + "one.",
                    advice,
                    exception),

            HttpStatusCode.Forbidden =>
                Assembled(Refused(operation, exception, permission), advice, exception),

            // Jira answers the same way whether an issue or a project does not exist and whether
            // it exists but is not visible, so the bare 404 already says everything Jira has to
            // say — a status line here would only repeat "404".
            HttpStatusCode.NotFound when IsAnIssue(exception.Endpoint) =>
                Bare(
                    $"Jira has no issue at {exception.Endpoint} that this account can see. Jira "
                    + "answers the same way whether the issue does not exist and whether it "
                    + "exists but you cannot see it, so there is nothing to retry: check the "
                    + "issue key, or ask someone with access to it.",
                    advice),

            HttpStatusCode.NotFound when IsAProject(exception.Endpoint) =>
                Bare(
                    $"Jira has no project at {exception.Endpoint} that this account can see. "
                    + "Jira answers the same way whether the project does not exist and whether "
                    + "it exists but you cannot browse it, so there is nothing to retry: check "
                    + "the project key with jira_list_projects, or ask someone with access to it.",
                    advice),

            HttpStatusCode.NotFound when IsTheSoftwareApi(exception.Endpoint) =>
                Bare(
                    $"Jira answered {exception.Endpoint} with a 404. The whole software API "
                    + "answers that way where Jira Software is not licensed, so this instance "
                    + $"may have lost the licence the capability probe recorded. Run "
                    + $"'jira-server-mcp profile refresh {profileName}'; if it is still "
                    + "licensed, check the board or sprint identifier.",
                    advice),

            HttpStatusCode.NotFound =>
                Bare(
                    $"Jira has nothing at {exception.Endpoint} (404). Check the profile's base "
                    + "URL, including any context path such as /jira.",
                    advice),

            HttpStatusCode.BadRequest when exception.FieldErrors.Count > 0 =>
                Assembled(
                    $"Jira rejected {operation}. Its own message for each field follows.",
                    advice,
                    exception),

            _ => Assembled($"{operation} failed.", advice, exception),
        };

    /// <summary>
    /// What Jira refused, and then why. Cause before consequence, and both before the caller's
    /// state clause — a refusal reads as one account of one failure rather than as two.
    ///
    /// The two openings differ on purpose. Where nothing was asked, or the lookup itself failed,
    /// this is today's sentence to the character: it is all this server knows. Where Jira did
    /// answer, the opening stops asserting a missing permission, because the next line says
    /// whether there is one — and on the branch where the account holds what it claimed, the old
    /// opening would contradict it.
    /// </summary>
    private static string Refused(
        string operation,
        JiraApiException exception,
        PermissionAnswer? permission) =>
        permission is null
            ? $"Jira refused {operation}: the account this server is authenticated as does not "
              + $"have permission for it on {exception.Endpoint}. The request was not retried, "
              + "and repeating it will not help."
            : $"Jira refused {operation} on {exception.Endpoint}. The request was not retried, "
              + "and repeating it will not help.\n"
              + PermissionAdvice.Sentence(permission);

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
    /// This server's sentence, the caller's advice if it gave one, and the status line — framed
    /// around Jira's own words when it said anything — in that order, so trusted prose always
    /// reads before untrusted content and nothing appends past a closing marker.
    /// </summary>
    private static string Assembled(string sentence, string? advice, JiraApiException exception)
    {
        var statusLine = $"Jira returned {(int)exception.StatusCode}.";

        var withStatus = Bare(
            sentence,
            advice,
            JiraWords(exception) is { } words
                ? UntrustedContent.Envelope(statusLine, Truncation.Error(words))
                : statusLine);

        return withStatus;
    }

    /// <summary>
    /// This server's sentence and the caller's advice, with no status line and no framed block —
    /// what a bare 404 gets, because Jira said everything it had to say already.
    /// </summary>
    private static string Bare(string sentence, string? advice, string? trailer = null)
    {
        var parts = new List<string> { sentence };

        if (advice is not null)
        {
            parts.Add(advice);
        }

        if (trailer is not null)
        {
            parts.Add(trailer);
        }

        return string.Join("\n", parts);
    }

    /// <summary>
    /// Jira's own error messages and field errors, joined as lines, or null if it said nothing.
    /// Shared with <see cref="Rendering.BulkIssueDetail"/>, which attributes the same words to a
    /// failed key rather than to a whole failed call.
    /// </summary>
    internal static string? JiraWords(JiraApiException exception)
    {
        var lines = new List<string>();

        lines.AddRange(exception.ErrorMessages);
        lines.AddRange(exception.FieldErrors.Select(field => $"{field.Key}: {field.Value}"));

        return lines.Count is 0 ? null : string.Join("\n", lines);
    }
}
