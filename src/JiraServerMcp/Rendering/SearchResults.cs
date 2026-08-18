using System.Text;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// A page of search results as compact text: one line per issue, the key first so a follow-up
/// call is cheap, and Jira's own wiki markup passed through unconverted — models read it, and
/// converting risks corrupting text that will be written back.
/// </summary>
internal static class SearchResults
{
    public static Rendered Render(JiraSearchPage page)
    {
        var lines = new List<string>();
        var used = 0;

        foreach (var issue in page.Issues)
        {
            var line = Line(issue);

            if (used + line.Length + 1 > ResponseBudget.SearchTextBudget - ResponseBudget.PageReserve)
            {
                break;
            }

            lines.Add(line);
            used += line.Length + 1;
        }

        var cutByBudget = lines.Count < page.Issues.Count;

        // Both halves off one traversal (ADR-0009, rule 4): the rows the budget admitted are the
        // rows the structure carries, so the two can never disagree on their count.
        return new Rendered(
            UntrustedContent.Envelope(
                Header(page, lines.Count, cutByBudget),
                string.Join("\n", lines)),
            ToolOutputs.Node(Structure(page, lines.Count, cutByBudget)));
    }

    /// <summary>
    /// The page's structured half. <c>nextStartAt</c> is where the caller resumes — the position
    /// after the last row actually rendered, which a budget cut moves back from Jira's own.
    /// </summary>
    private static IssuePageOutput Structure(JiraSearchPage page, int rendered, bool cutByBudget)
    {
        var resumeAt = page.StartAt + rendered;
        var more = cutByBudget || page.HasMore;

        return new IssuePageOutput
        {
            Outcome = Outcomes.Ok,
            Total = page.Total,
            StartAt = page.StartAt,
            Count = rendered,
            NextStartAt = more ? resumeAt : null,
            CutByBudget = cutByBudget,
            Issues =
            [
                .. page.Issues.Take(rendered).Select(issue => new IssueRowOutput
                {
                    Key = issue.Key,
                    StatusId = issue.StatusId,
                    Status = issue.Status,
                    TypeName = issue.TypeName,
                    Assignee = issue.Assignee,
                }),
            ],
        };
    }

    private static string Header(JiraSearchPage page, int rendered, bool cutByBudget)
    {
        if (rendered is 0)
        {
            return $"total: {page.Total} — nothing to show on this page.";
        }

        var shown = $"total: {page.Total} — showing {page.StartAt + 1}-{page.StartAt + rendered}";

        if (cutByBudget)
        {
            return $"{shown} — the rest of this page did not fit the response budget; ask for it "
                   + $"with startAt: {page.StartAt + rendered}.";
        }

        return page.HasMore
            ? $"{shown} — more pages exist; ask for the next with startAt: {page.NextStartAt}."
            : $"{shown} — no more pages.";
    }

    private static string Line(JiraIssue issue)
    {
        var line = new StringBuilder(issue.Key);

        foreach (var field in issue.Fields)
        {
            if (FieldValue.Read(field.Value) is not { Length: > 0 } value)
            {
                continue;
            }

            line.Append(" | ").Append(field.Key).Append(": ")
                .Append(Truncation.Apply(value, issue.Key));
        }

        return line.ToString();
    }
}
