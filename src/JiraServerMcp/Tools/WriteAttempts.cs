using System.Collections.Concurrent;

namespace JiraServerMcp.Tools;

/// <summary>
/// What this process has already tried to write, keyed by the tool and the caller's idempotency
/// key. It exists so that an agent whose write timed out does not have to invent a recovery
/// procedure in the worst possible context — after a timeout, mid-workflow — and does not have to
/// choose between duplicating the write and stalling.
/// </summary>
/// <remarks>
/// An attempt is recorded before the write is sent, which is the whole design. Recording the
/// outcome afterwards would help only when the first call came back, and a duplicate arises
/// precisely when it did not: Jira committed the write and the answer never arrived. Recording on
/// the way out means a repeat knows an attempt was made even when nothing is known about how it
/// ended.
///
/// The record lives as long as the process and no longer. Durable state is something this server
/// has deliberately avoided, and this is not the feature to acquire it for — a restarted loop is
/// back to reading Jira to find out what happened, which is why the tools still say so. Nothing
/// here is ever evicted: an entry is two short strings, and forgetting one would silently turn a
/// key back into no protection at all.
/// </remarks>
internal sealed class WriteAttempts
{
    private readonly ConcurrentDictionary<(string Tool, string Key), WriteAttempt> _attempts = new();

    /// <summary>
    /// Claims a key for one write, or hands back the attempt that already holds it. True means the
    /// caller may write and should report what happened through <paramref name="attempt"/>; false
    /// means this key has been used and <paramref name="attempt"/> says what is known about it.
    /// </summary>
    /// <remarks>
    /// The tool name is part of the key, so the same string used on a create and on a comment is
    /// two claims rather than a collision. An agent numbering its steps should not have to know
    /// which of them happen to be writes.
    /// </remarks>
    public bool TryBegin(string tool, string key, out WriteAttempt attempt)
    {
        var claimed = new WriteAttempt();

        attempt = _attempts.GetOrAdd((tool, key.Trim()), claimed);

        return ReferenceEquals(attempt, claimed);
    }
}

/// <summary>
/// One write this process has attempted under a key, and what is known about how it ended.
/// </summary>
internal sealed class WriteAttempt
{
    /// <summary>
    /// What became of the write. <see cref="WriteOutcome.Unknown"/> until the call comes back, and
    /// that is the value that matters: it is what a timeout leaves behind.
    /// </summary>
    public WriteOutcome Outcome { get; private set; } = WriteOutcome.Unknown;

    /// <summary>
    /// What the write produced, in the tool's own words — "PROJ-42", a comment identifier. Present
    /// only where the write came back and succeeded.
    /// </summary>
    public string? Detail { get; private set; }

    public void Succeeded(string detail) => (Outcome, Detail) = (WriteOutcome.Ok, detail);

    /// <summary>
    /// Jira answered and refused. This is the one ending that says the write certainly did not
    /// happen, which is why it is worth telling apart from silence.
    /// </summary>
    public void Rejected() => Outcome = WriteOutcome.Rejected;
}

internal enum WriteOutcome
{
    /// <summary>The call has not come back, or did not come back at all. A repeat must not write.</summary>
    Unknown,

    Ok,

    Rejected,
}

/// <summary>
/// What a caller is told when it reuses a key. The three endings differ in what they license the
/// caller to do next, which is the only reason they are told apart at all.
/// </summary>
internal static class WriteAttemptAnswers
{
    /// <summary>
    /// The write already happened and this process saw it happen. Answered as a success: an
    /// unattended loop repeating a step wants "that is already done", not an error to handle.
    /// </summary>
    public static string Ok(string what, string detail) =>
        $"This key was already used by a {what} that succeeded: {detail}. Nothing was written "
        + "again.";

    /// <summary>
    /// The case the whole feature exists for. The write was sent, no answer came back, and Jira
    /// may or may not have committed it — so a repeat would be exactly the duplicate a key is
    /// there to prevent.
    /// </summary>
    public static string Unknown(string what, string howToCheck) =>
        $"This key was already used by a {what} whose outcome is unknown: it was sent once and no "
        + $"answer came back. Nothing was written again. {howToCheck}";

    /// <summary>
    /// Jira refused, so nothing was written then either. The key is spent all the same — it names
    /// one attempt, not one intention — and a corrected call is a new one.
    /// </summary>
    public static string Rejected(string what) =>
        $"This key was already used by a {what} that Jira rejected, so nothing was written then "
        + "and nothing has been written now. A key names one attempt: send the corrected call "
        + "under a new key.";
}
