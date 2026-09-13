using JiraServerMcp.Rendering;

namespace JiraServerMcp.Tools;

/// <summary>
/// Recovery advice: what a caller does to find out what became of a write whose outcome is
/// unknown — read an expansion, read the issue, search for it, or nothing, because a repeat is
/// safe. Eight writes owe the caller that fact, in up to three frames, and a fact written by hand
/// at each of them is a fact that drifts. Here it is data a tool hands over, and the frame around
/// it is chosen by which renderer is called.
/// </summary>
/// <remarks>
/// Pure: it is handed a noun and a recovery and answers with a sentence, as
/// <see cref="Vocabulary"/> and <see cref="ProjectKey"/> do.
///
/// <see cref="RetrySafeWrite"/> is the obvious home and deliberately is not it. Four of the eight
/// writes take no key and never reach that module, and it passes <see cref="ToolCall"/>'s two
/// clauses straight through rather than inventing failure vocabulary of its own. So it renders
/// only the replay frame, from <see cref="AfterSpentKey"/>; the tool renders the timeout clause it
/// hands to <see cref="ToolCall.RunAsync"/>, and <see cref="ToolCall"/> stays the failure seam.
///
/// A frame is a method name rather than a flag: a flag at eight call sites is eight chances to
/// pass the wrong one silently. Only <see cref="AfterSpentKey"/> says "under a new key", because
/// only there has a key been spent, and only <see cref="AfterTimeout"/> accepts
/// <see cref="Safe"/>, because a write that is safe to repeat has no reason to take a key.
/// </remarks>
internal abstract record WriteRecovery
{
    private WriteRecovery()
    {
    }

    /// <summary>Read the issue with the expansion that would show the write.</summary>
    /// <param name="key">The issue the write was sent against.</param>
    /// <param name="expansion">The section of the issue the write would appear in.</param>
    /// <param name="warning">
    /// What a blind retry would cost, told in every frame — a caller replaying a spent key faces
    /// the same risk as one that saw the timeout.
    /// </param>
    public static Check ByExpansion(string key, Expansion expansion, string? warning = null)
    {
        var name = Expansions.Table.Single(row => row.Id == expansion).Name;

        return new Check(
            $"read {key} with jira_get_issues and the {name} expansion",
            $"read the issue with jira_get_issues and the {name} expansion",
            warning);
    }

    /// <summary>Read the issue, whose ordinary fields would show the write.</summary>
    public static Check ByReading(string key) =>
        new($"read {key} with jira_get_issues", "read the issue with jira_get_issues", null);

    /// <summary>
    /// Search for what was created. There is no key to read yet, and creating a second issue to
    /// find out is the failure a keyed write exists to prevent.
    /// </summary>
    public static Check BySearch()
    {
        const string Search =
            "the issue may or may not exist: search for the summary with jira_search";

        return new Check(Search, Search, null);
    }

    /// <summary>Nothing to check: Jira identifies the write itself, so a repeat duplicates nothing.</summary>
    /// <param name="reason">Why a repeat is safe, told after "Sending it again is safe —".</param>
    public static WriteRecovery Safe(string reason) => new Repeat(reason);

    /// <summary>The timeout clause of a write that takes no key.</summary>
    public static string AfterTimeout(string noun, WriteRecovery recovery) =>
        recovery switch
        {
            Check check => $"{SentOnce(noun)}, so {check.Named} to see whether it landed{check.Warned}.",
            Repeat repeat => $"{SentOnce(noun)}. Sending it again is safe — {repeat.Reason}.",
            _ => throw new InvalidOperationException($"No timeout clause for {recovery}."),
        };

    /// <summary>The timeout clause of a write that takes a key.</summary>
    public static string AfterKeyedTimeout(string noun, Check recovery) =>
        $"{SentOnce(noun)}, so {recovery.Named} before sending it again{recovery.Warned}.";

    /// <summary>
    /// A key spent by a write that was sent and never answered. The issue goes unnamed: the replay
    /// is told with this call's arguments, and a key reused against another issue would otherwise
    /// send the caller to read the wrong one.
    /// </summary>
    public static string AfterSpentKey(string noun, Check recovery) =>
        $"This key was already used by a {noun} whose outcome is unknown: it was sent once and no "
        + "answer came back. Nothing was written again. "
        + $"{char.ToUpperInvariant(recovery.Unnamed[0])}{recovery.Unnamed[1..]} before sending it "
        + $"again under a new key{recovery.Warned}.";

    private static string SentOnce(string noun) => $". The {noun} was sent once and was not repeated";

    /// <summary>A recovery that means reading Jira, so the only kind a keyed write can carry.</summary>
    /// <param name="Named">The check, naming the issue the write was sent against.</param>
    /// <param name="Unnamed">The same check without naming it, for a replay.</param>
    /// <param name="Warning">What a blind retry would cost, if anything beyond a duplicate.</param>
    public sealed record Check(string Named, string Unnamed, string? Warning) : WriteRecovery
    {
        internal string Warned => Warning is null ? string.Empty : $" — {Warning}";
    }

    private sealed record Repeat(string Reason) : WriteRecovery;
}
