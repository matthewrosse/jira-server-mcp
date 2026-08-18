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
    /// <summary>
    /// The page as text and as structure, off one traversal: the row projector sits beside the
    /// line renderer so the page mechanics stay here and the two halves cannot admit different
    /// rows or disagree on where to resume.
    /// </summary>
    public static Rendered Render<T, TOutput>(
        JiraAgilePage<T> page,
        Func<T, string> line,
        Func<IReadOnlyList<T>, AgilePagePosition, TOutput> structure)
        where TOutput : ToolOutput
    {
        var lines = new List<string>();
        var shown = new List<T>();
        var used = 0;

        foreach (var value in page.Values)
        {
            var rendered = line(value);

            if (used + rendered.Length + 1 > ResponseBudget.SearchTextBudget - ResponseBudget.PageReserve)
            {
                break;
            }

            lines.Add(rendered);
            shown.Add(value);
            used += rendered.Length + 1;
        }

        return new Rendered(
            UntrustedContent.Envelope(Header(page, lines.Count), string.Join("\n", lines)),
            ToolOutputs.Node(structure(shown, Position(page, lines.Count))));
    }

    /// <summary>
    /// Where the caller resumes, saying exactly what the header says. A page the budget cut
    /// resumes at the row it stopped on; anything else resumes where Jira's page ends, which
    /// advances by the page size rather than by the rows returned — the software API filters a
    /// page by permission after paging it, so advancing by rows would ask for the same page for
    /// ever. A last page offers nothing.
    /// </summary>
    private static AgilePagePosition Position<T>(JiraAgilePage<T> page, int rendered)
    {
        var cutByBudget = rendered > 0 && rendered < page.Values.Count;

        return new AgilePagePosition(
            page.StartAt,
            rendered,
            cutByBudget ? page.StartAt + rendered
            : page.HasMore ? page.NextStartAt
            : null);
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

/// <summary>
/// A page's position in its listing: where it began, how many rows it carried, and where a caller
/// resumes — or null where there is nowhere to resume to.
/// </summary>
internal readonly record struct AgilePagePosition(int StartAt, int Count, int? NextStartAt);
