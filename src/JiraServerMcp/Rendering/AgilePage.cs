using JiraServerMcp.Jira;

namespace JiraServerMcp.Rendering;

/// <summary>
/// The header a software API listing carries. The software API says whether this is the last page
/// and never how many there are in all, so neither does this: a count nobody was given would be an
/// invention.
/// </summary>
internal static class AgilePage
{
    public static string Header<T>(JiraAgilePage<T> page)
    {
        if (page.Values.Count is 0)
        {
            return "nothing to show on this page.";
        }

        var shown = $"showing {page.StartAt + 1}-{page.StartAt + page.Values.Count}";

        return page.HasMore
            ? $"{shown} — more pages exist; ask for the next with startAt: {page.NextStartAt}."
            : $"{shown} — no more pages.";
    }
}
