using System.Globalization;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Tools;

/// <summary>
/// The arithmetic behind the change feed: the JQL a window of changes is asked with, and the
/// watermark the next call resumes from. Pure, because the timezone handling is the part most
/// likely to be quietly wrong and a wrong window is invisible in a response that looks fine.
/// </summary>
internal static class ChangeFeed
{
    /// <summary>
    /// Jira reads a date literal in a JQL clause in the zone of the account running the query and
    /// offers no way to write an offset into one, so the moment the caller gave is restated in
    /// that offset before it is written. This format is the one Jira Server documents for a JQL
    /// date.
    /// </summary>
    private const string JqlMoment = "yyyy/MM/dd HH:mm";

    /// <summary>
    /// A watermark is ISO-8601 with an explicit offset rather than an opaque token: there is no
    /// hidden state for a token to stand for, a human debugging a stuck loop can read a timestamp,
    /// and the offset makes the timezone this is measured in visible rather than assumed.
    /// </summary>
    private const string Watermark = "yyyy-MM-ddTHH:mm:sszzz";

    /// <summary>
    /// Oldest change first. A feed ordered newest first and paged would hand back a watermark from
    /// the first page while older changes sat unread on the second, and the caller would move the
    /// window past them.
    /// </summary>
    private const string Ordering = "ORDER BY updated ASC";

    /// <summary>
    /// The moment the caller asked to resume from. An offset is required rather than assumed, as
    /// a worklog's start time is: a bare local time is exactly the timezone mistake this tool
    /// exists to take off the caller, and reading it as this machine's zone would make that
    /// mistake silently instead of refusing it.
    /// </summary>
    public static bool TryReadSince(string since, out DateTimeOffset moment)
    {
        moment = default;

        return DateTime.TryParse(
                   since,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out var withoutOffset)
               && withoutOffset.Kind is not DateTimeKind.Unspecified
               && DateTimeOffset.TryParse(
                   since,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out moment);
    }

    /// <summary>
    /// The offset Jira will read this account's date literals in: the account's own zone at the
    /// moment in question, so that a window spanning a daylight-saving change is stated in the
    /// offset that was actually in force. False where Jira named a zone this machine cannot
    /// resolve, which leaves the caller to fall back to something it can.
    /// </summary>
    public static bool TryZoneOffset(string? timeZone, DateTimeOffset at, out TimeSpan offset)
    {
        offset = default;

        if (timeZone is null || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone))
        {
            return false;
        }

        offset = zone.GetUtcOffset(at);

        return true;
    }

    /// <summary>
    /// The query for everything visible that changed at or after <paramref name="since"/>, stated
    /// in the offset Jira reads this account's queries in.
    /// </summary>
    public static string Jql(DateTimeOffset since, TimeSpan zoneOffset, string? project)
    {
        var moment = Floor(since, zoneOffset).ToString(JqlMoment, CultureInfo.InvariantCulture);
        var changed = $"updated >= \"{moment}\"";

        return project is null
            ? $"{changed} {Ordering}"
            : $"project = {project} AND {changed} {Ordering}";
    }

    /// <summary>
    /// The watermark to pass to the next call: the start of the last-seen minute, in the same
    /// offset the window was asked in. Jira Server records <c>updated</c> to the minute on some versions, so a
    /// watermark taken from the timestamp itself would exclude every other change made in that
    /// same minute. Flooring makes the feed repeat rather than skip — a caller holds the keys it
    /// has already seen and can recognise a repeat, and cannot recognise something it never got.
    /// </summary>
    /// <remarks>
    /// The rows are the ones the response budget admitted, not the ones Jira sent: a watermark
    /// taken from a row the caller never saw would move the window past it. Nothing to read a
    /// timestamp from hands back the window that was given. The watermark never moves backwards.
    /// </remarks>
    public static string NextSince(
        IReadOnlyList<JiraIssue> issues,
        DateTimeOffset since,
        TimeSpan zoneOffset)
    {
        var next = Floor(since, zoneOffset);

        foreach (var issue in issues)
        {
            if (issue.Updated is not { } updated
                || !DateTimeOffset.TryParse(
                    updated,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                continue;
            }

            var floored = Floor(parsed, zoneOffset);

            if (floored > next)
            {
                next = floored;
            }
        }

        return next.ToString(Watermark, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset Floor(DateTimeOffset moment, TimeSpan zoneOffset)
    {
        var inZone = moment.ToOffset(zoneOffset);

        return inZone.AddTicks(-(inZone.Ticks % TimeSpan.TicksPerMinute));
    }
}
