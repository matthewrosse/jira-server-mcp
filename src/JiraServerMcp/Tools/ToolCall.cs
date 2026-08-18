using JiraServerMcp.Errors;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;
using ModelContextProtocol.Protocol;

namespace JiraServerMcp.Tools;

/// <summary>
/// What happens when a tool call fails. The agent cannot read this server's log, so a failure has
/// to say which failure it was in the result itself, or an expired token looks the same as a
/// wrong base URL. Owning the three arms here is what lets that vocabulary change in one place.
///
/// It owns the same three arms in the structured half (ADR-0009, rule 3): every result carries an
/// outcome, so "was this a permissions problem or a dead network" is a field to read rather than a
/// sentence to parse. A renderer's own structure rides beside it; a renderer that has none yet
/// still answers with the envelope.
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
        Func<Task<Rendered>> work,
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
                Failed(
                    describeApiFailure?.Invoke(exception)
                        ?? JiraToolError.Describe(exception, profile.Name, operation),
                    Outcomes.JiraApi,
                    (int)exception.StatusCode));
        }
        catch (HttpRequestException exception)
        {
            return Step<T>.Fail(
                Failed(
                    $"Could not reach Jira{whenUnreachable}: {exception.Message}",
                    Outcomes.Unreachable));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The client's own timeout, not the caller hanging up: the caller's cancellation is
            // left to propagate, because there is nobody waiting for an answer to it.
            return Step<T>.Fail(
                Failed(
                    $"Jira did not answer for profile '{profile.Name}' in time{whenTimedOut}",
                    Outcomes.TimedOut));
        }
    }

    /// <summary>
    /// A successful result: the renderer's prose, and its structure where it has one. A renderer
    /// that does not yet build a structured half still answers with the outcome, because rule 3
    /// promises structure on every result rather than on some of them.
    /// </summary>
    public static CallToolResult Text(Rendered rendered) =>
        new()
        {
            Content = [new TextContentBlock { Text = rendered.Text }],
            StructuredContent = rendered.Structure ?? ToolOutputs.Outcome(Outcomes.Ok),
        };

    /// <summary>
    /// A call this server refused before anything reached Jira: an empty comment, a duration Jira
    /// could not read, a key cap exceeded. Nothing was attempted, which is what the outcome says.
    /// </summary>
    public static CallToolResult Error(string text) => Failed(text, Outcomes.Refused);

    /// <summary>
    /// A refusal that carries the renderer's own structure — the bulk read's, whose shape must not
    /// appear and vanish with the number of bad keys.
    /// </summary>
    public static CallToolResult Error(Rendered rendered) =>
        new()
        {
            Content = [new TextContentBlock { Text = rendered.Text }],
            StructuredContent = rendered.Structure ?? ToolOutputs.Outcome(Outcomes.Refused),
            IsError = true,
        };

    private static CallToolResult Failed(string text, string outcome, int? statusCode = null) =>
        new()
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = ToolOutputs.Outcome(outcome, statusCode),
            IsError = true,
        };

    /// <summary>
    /// The outcome of one <see cref="StepAsync{T}"/> call: the work's value, or a finished result
    /// carrying the failure's own wording.
    /// </summary>
    public readonly record struct Step<T>
    {
        private T? RawValue { get; init; }

        private CallToolResult? RawError { get; init; }

        public bool Failed { get; private init; }

        public T Value => RawValue!;

        public CallToolResult Error => RawError!;

        public static Step<T> Ok(T value) => new() { RawValue = value };

        public static Step<T> Fail(CallToolResult error) => new() { Failed = true, RawError = error };
    }
}
