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
    /// smaller page usually helps."</c> <c>describeApiFailure</c> replaces the default
    /// profile/operation wording for a Jira API failure when a tool has something more specific to
    /// say, such as where to look after a rejected create.
    /// </remarks>
    public static async Task<CallToolResult> RunAsync(
        ServedProfile profile,
        string operation,
        string whenUnreachable,
        string whenTimedOut,
        Func<Task<string>> work,
        CancellationToken cancellationToken,
        Func<JiraApiException, string>? describeApiFailure = null)
    {
        var step = await StepAsync(
            profile,
            operation,
            whenUnreachable,
            whenTimedOut,
            work,
            cancellationToken,
            describeApiFailure);

        return step.Failed ? step.Error : Text(step.Value);
    }

    /// <summary>
    /// Runs one step of a multi-step tool call, handing back either its value or a finished error
    /// result. Exists for a tool such as transitioning an issue, which reads and then writes and
    /// says something different for each — the read's failure never implies the write was
    /// attempted.
    /// </summary>
    public static async Task<Step<T>> StepAsync<T>(
        ServedProfile profile,
        string operation,
        string whenUnreachable,
        string whenTimedOut,
        Func<Task<T>> work,
        CancellationToken cancellationToken,
        Func<JiraApiException, string>? describeApiFailure = null)
    {
        try
        {
            return Step<T>.Ok(await work());
        }
        catch (JiraApiException exception)
        {
            return Step<T>.Fail(
                Error(describeApiFailure?.Invoke(exception)
                    ?? JiraToolError.Describe(exception, profile.Name, operation)));
        }
        catch (HttpRequestException exception)
        {
            return Step<T>.Fail(Error($"Could not reach Jira{whenUnreachable}: {exception.Message}"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The client's own timeout, not the caller hanging up: the caller's cancellation is
            // left to propagate, because there is nobody waiting for an answer to it.
            return Step<T>.Fail(
                Error($"Jira did not answer for profile '{profile.Name}' in time{whenTimedOut}"));
        }
    }

    public static CallToolResult Text(string text) =>
        new() { Content = [new TextContentBlock { Text = text }] };

    public static CallToolResult Error(string text) =>
        new() { Content = [new TextContentBlock { Text = text }], IsError = true };

    /// <summary>
    /// The outcome of one <see cref="StepAsync{T}"/> call: the work's value, or a finished result
    /// carrying the failure's own wording.
    /// </summary>
    public readonly struct Step<T>
    {
        private readonly T? value;
        private readonly CallToolResult? error;

        private Step(bool failed, T? value, CallToolResult? error)
        {
            Failed = failed;
            this.value = value;
            this.error = error;
        }

        public bool Failed { get; }

        public T Value => value!;

        public CallToolResult Error => error!;

        public static Step<T> Ok(T value) => new(false, value, null);

        public static Step<T> Fail(CallToolResult error) => new(true, default, error);
    }
}
