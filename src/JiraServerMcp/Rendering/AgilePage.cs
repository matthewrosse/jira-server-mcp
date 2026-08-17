using JiraServerMcp.Jira;

namespace JiraServerMcp.Rendering;

/// <summary>
/// A page from the software API as compact text. It says whether this is the last page and never
/// how many rows there are in all, so neither does this: a count nobody was given would be an
/// invention. What it always carries is where to resume from, because that is the only way a
/// caller reaches the next page.
/// </summary>
internal static class AgilePage
{
    public static string Render<T>(JiraAgilePage<T> page, Func<T, string> line)
    {
        var lines = new List<string>();
        var used = 0;

        foreach (var value in page.Values)
        {
            var rendered = line(value);

            if (used + rendered.Length + 1 > ResponseBudget.SearchTextBudget - ResponseBudget.PageReserve)
            {
                break;
            }

            lines.Add(rendered);
            used += rendered.Length + 1;
        }

        return UntrustedContent.Envelope(Header(page, lines.Count), string.Join("\n", lines));
    }

    private static string Header<T>(JiraAgilePage<T> page, int rendered)
    {
        // Jira filters a page by permission after it has paged, so an empty page in the middle of
        // a listing is ordinary rather than the end of one.
        if (rendered is 0)
        {
            return page.HasMore
                ? $"nothing to show on this page — more pages exist; ask for the next with "
                  + $"startAt: {page.NextStartAt}."
                : "nothing to show on this page.";
        }

        var shown = $"showing {page.StartAt + 1}-{page.StartAt + rendered}";

        if (rendered < page.Values.Count)
        {
            return $"{shown} — the rest of this page did not fit the response budget; ask for it "
                   + $"with startAt: {page.StartAt + rendered}.";
        }

        return page.HasMore
            ? $"{shown} — more pages exist; ask for the next with startAt: {page.NextStartAt}."
            : $"{shown} — no more pages.";
    }
}
