using JiraServerMcp.Errors;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Profiles;
using ModelContextProtocol.Protocol;

namespace JiraServerMcp.Tools;

/// <summary>
/// What happens when a tool call fails. The agent cannot read this server's log, so a failure has
/// to say which failure it was in the result itself, or an expired token looks the same as a
/// wrong base URL. Owning the three arms here is what lets that vocabulary change in one place.
/// </summary>
internal static class ToolCall
{
    /// <summary>
    /// Runs the work and returns its text as a result, or the failure as one.
    /// </summary>
    /// <remarks>
    /// Two arms take a clause from the tool, because the advice worth giving differs per tool
    /// while the sentence around it does not. <c>whenUnreachable</c> follows "Could not reach
    /// Jira" and says what was therefore not done — <c>", and ABC-1 was not changed"</c> — and is
    /// empty for a read, which left nothing half-finished. <c>whenTimedOut</c> follows "Jira did
    /// not answer for profile 'x' in time" — <c>", and the request was given up. Asking for a
    /// smaller page usually helps."</c>
    /// </remarks>
    public static async Task<CallToolResult> RunAsync(
        ServedProfile profile,
        string operation,
        string whenUnreachable,
        string whenTimedOut,
        Func<Task<string>> work,
        CancellationToken cancellationToken)
    {
        try
        {
            return Text(await work());
        }
        catch (JiraApiException exception)
        {
            return Error(JiraToolError.Describe(exception, profile.Name, operation));
        }
        catch (HttpRequestException exception)
        {
            return Error($"Could not reach Jira{whenUnreachable}: {exception.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The client's own timeout, not the caller hanging up: the caller's cancellation is
            // left to propagate, because there is nobody waiting for an answer to it.
            return Error(
                $"Jira did not answer for profile '{profile.Name}' in time{whenTimedOut}");
        }
    }

    private static CallToolResult Text(string text) =>
        new() { Content = [new TextContentBlock { Text = text }] };

    private static CallToolResult Error(string text) =>
        new() { Content = [new TextContentBlock { Text = text }], IsError = true };
}
