using System.Globalization;
using System.Text.RegularExpressions;

namespace JiraServerMcp.Tools;

/// <summary>
/// What a worklog says about itself, checked before anything reaches Jira. A duration Jira cannot
/// read comes back as a bare 400 several hundred milliseconds later, which tells an agent nothing;
/// refusing it here can name the syntax and give an example.
/// </summary>
internal static partial class WorklogInput
{
    /// <summary>
    /// Jira's own duration syntax: a run of amounts, each naming its unit — weeks, days, hours,
    /// minutes. A bare number is deliberately not one: Jira reads it as whatever the instance's
    /// default unit is set to, which the caller cannot know.
    /// </summary>
    public static bool IsDuration(string duration) => DurationPattern().IsMatch(duration.Trim());

    /// <summary>
    /// A start time in the form Jira accepts — its milliseconds and its offset without a colon —
    /// from any ISO-8601 timestamp that carries an offset. One without an offset is refused rather
    /// than read as this machine's, which would log the work at the wrong time.
    /// </summary>
    public static bool TryStartTime(string started, out string jiraFormat)
    {
        jiraFormat = "";

        if (!DateTime.TryParse(
                started,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var withoutOffset)
            || withoutOffset.Kind is DateTimeKind.Unspecified
            || !DateTimeOffset.TryParse(
                started,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var value))
        {
            return false;
        }

        var sign = value.Offset < TimeSpan.Zero ? "-" : "+";

        jiraFormat = value.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)
                     + sign
                     + value.Offset.ToString("hhmm", CultureInfo.InvariantCulture);

        return true;
    }

    [GeneratedRegex(
        @"^\d+(\.\d+)?[wdhm](\s+\d+(\.\d+)?[wdhm])*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationPattern();
}
