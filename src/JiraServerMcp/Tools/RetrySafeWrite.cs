using JiraServerMcp.Errors;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;
using ModelContextProtocol.Protocol;

namespace JiraServerMcp.Tools;

/// <summary>
/// One write made safe to repeat. A tool that takes an idempotency key owes the caller five steps
/// in one order — decide whether there is a key at all, claim it before anything is sent, answer a
/// spent key with what became of the first attempt, send, record the ending — and the order is the
/// design rather than an implementation detail. Stating it here is what stops the fourth keyed
/// write from having to know it.
/// </summary>
/// <remarks>
/// It sits <em>around</em> <see cref="ToolCall.RunAsync"/> rather than inside it, which is the
/// opposite of <see cref="IssuePage"/>. The replay path returns before any I/O happens, and its
/// error flag and outcome describe a spent key rather than a failed write, so it cannot live
/// inside that call's work delegate. Around is also the only shape in which <em>record before you
/// send</em> cannot be got wrong: handing a tool a claimed attempt and letting it call
/// <see cref="ToolCall.RunAsync"/> itself leaves the load-bearing ordering to each call site
/// again.
///
/// <see cref="ToolCall"/> stays the failure seam: the operation, the two clauses and the API
/// description are passed straight through, and no failure vocabulary is invented here.
///
/// A tool's own refusals — an empty comment, an unreadable duration, an alias collision — happen
/// before this is called. A call this server refuses outright must not spend the key.
/// </remarks>
internal static class RetrySafeWrite
{
    /// <summary>
    /// What every keyed write says about its <c>idempotencyKey</c> parameter. Each tool still
    /// declares the parameter itself; the sentence is stated once so the wording cannot drift
    /// between them.
    /// </summary>
    public const string KeyDescription =
        "An optional idempotency key of the caller's choosing. A second call carrying a key "
        + "this server has already seen writes nothing and reports what became of the first, "
        + "which is what makes a retry after a timeout safe. The record lasts as long as this "
        + "server process and no longer, so a restarted loop is back to reading Jira to find "
        + "out. A key names one attempt: a corrected call after a rejection needs a new one.";

    /// <param name="attempts">What this process has already written under a key.</param>
    /// <param name="keyed">
    /// The write as a replay names it: the tool a key is claimed under, its noun, and how the
    /// caller finds out what became of it when the outcome is unknown.
    /// </param>
    /// <param name="idempotencyKey">The caller's key, if it sent one. Whitespace is no key.</param>
    /// <param name="profile">The profile being served, for the failure vocabulary.</param>
    /// <param name="operation">What was being done, as <see cref="ToolCall"/> tells it.</param>
    /// <param name="whenUnreachable">Passed through to <see cref="ToolCall.RunAsync"/>.</param>
    /// <param name="whenTimedOut">Passed through to <see cref="ToolCall.RunAsync"/>.</param>
    /// <param name="write">
    /// The write itself, answering with the rendering and with what it produced.
    /// </param>
    /// <param name="cancellationToken">The caller's cancellation.</param>
    /// <param name="describeApiFailure">Passed through to <see cref="ToolCall.RunAsync"/>.</param>
    /// <param name="claim">Passed through to <see cref="ToolCall.RunAsync"/>.</param>
    public static async Task<CallToolResult> RunAsync(
        WriteAttempts attempts,
        KeyedWrite keyed,
        string? idempotencyKey,
        ServedProfile profile,
        string operation,
        string whenUnreachable,
        string whenTimedOut,
        Func<Task<Written>> write,
        CancellationToken cancellationToken,
        Func<JiraApiException, PermissionDiagnosis?, string>? describeApiFailure = null,
        PermissionClaim? claim = null)
    {
        // Claimed before anything is sent: a key that arrives twice must find the first attempt
        // recorded even when that attempt is what timed out.
        WriteAttempt? attempt = null;

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            if (!attempts.TryBegin(keyed.Tool, idempotencyKey, out var claimed))
            {
                return Replayed(claimed, keyed);
            }

            attempt = claimed;
        }

        return await ToolCall.RunAsync(
            profile,
            operation,
            whenUnreachable,
            whenTimedOut,
            async () =>
            {
                var written = await WriteAttempts.SendAsync(attempt, write);

                attempt?.Succeeded(written.Detail, written.Rendered.Structure);

                return written.Rendered;
            },
            cancellationToken,
            describeApiFailure,
            claim);
    }

    /// <summary>
    /// A key this process has already spent. What the caller may do next differs per ending, so
    /// the three are told apart rather than collapsed into one refusal.
    /// </summary>
    private static CallToolResult Replayed(WriteAttempt prior, KeyedWrite keyed) =>
        prior.Ended switch
        {
            WriteAttempt.OkEnding ok => ToolCall.Text(new Rendered(Ok(keyed.Noun, ok.Detail), ok.Structure)),
            WriteAttempt.RejectedEnding => ToolCall.Error(Rejected(keyed.Noun)),
            // Sent, never answered, and possibly committed: a repeat is the duplicate a key prevents.
            _ => ToolCall.Error(WriteRecovery.AfterSpentKey(keyed.Noun, keyed.Recovery)),
        };

    /// <summary>
    /// The write already happened and this process saw it happen. Answered as a success: an
    /// unattended loop repeating a step wants "that is already done", not an error to handle.
    /// </summary>
    private static string Ok(string what, string detail) =>
        $"This key was already used by a {what} that succeeded: {detail}. Nothing was written "
        + "again.";

    /// <summary>
    /// Jira refused, so nothing was written then either. The key is spent all the same — it names
    /// one attempt, not one intention — and a corrected call is a new one.
    /// </summary>
    private static string Rejected(string what) =>
        $"This key was already used by a {what} that Jira rejected, so nothing was written then "
        + "and nothing has been written now. A key names one attempt: send the corrected call "
        + "under a new key.";
}

/// <summary>
/// What a keyed write produced: the answer the caller reads, and the detail a replay names it by —
/// "comment 10200 on PROJ-42", "PROJ-123". The detail belongs to the write rather than to the
/// rendering, which is why it rides back beside it rather than being read out of it.
/// </summary>
internal readonly record struct Written(Rendered Rendered, string Detail);

/// <summary>
/// A keyed write as a replay names it. Built per call, because the recovery advice needs the
/// call's own arguments.
/// </summary>
/// <param name="Tool">The tool's protocol name, which is half of what a key is claimed under.</param>
/// <param name="Noun">What the write is, in the words a replay uses: "comment", "worklog", "create".</param>
/// <param name="Recovery">
/// How the caller finds out what became of a write whose outcome is unknown. A write that is safe
/// to repeat has no reason to take a key, so this is never <see cref="WriteRecovery.Safe"/>.
/// </param>
internal readonly record struct KeyedWrite(string Tool, string Noun, WriteRecovery.Check Recovery);
