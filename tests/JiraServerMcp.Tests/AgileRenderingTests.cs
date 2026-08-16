using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Rendering;

namespace JiraServerMcp.Tests;

/// <summary>
/// Rendering a page the software API's way: it says whether this is the last page and never how
/// many rows there are, and a caller has to be able to resume from what it is told.
/// </summary>
public class AgileRenderingTests
{
    [Fact]
    public void A_page_that_is_not_the_last_says_where_to_resume()
    {
        var text = BoardList.Render(Page(isLast: false, Board(1, "Platform"), Board(2, "Ops")));

        text.ShouldContain("startAt: 2");
    }

    [Fact]
    public void The_last_page_says_there_is_no_more()
    {
        var text = BoardList.Render(Page(isLast: true, Board(1, "Platform")));

        text.ShouldContain("no more pages");
    }

    [Fact]
    public void An_empty_page_that_is_not_the_last_still_says_where_to_resume()
    {
        // Jira filters a page by permission after it has paged, so an empty page in the middle of
        // a listing is ordinary. Stopping there would hide every board after it.
        var text = BoardList.Render(new JiraAgilePage<JiraBoard>(20, 10, IsLast: false, []));

        // Past this page, not at it: asking for startAt 20 again would return the same nothing.
        text.ShouldContain("startAt: 30");
    }

    [Fact]
    public void An_empty_last_page_offers_nothing_to_resume_from()
    {
        var text = BoardList.Render(new JiraAgilePage<JiraBoard>(0, 50, IsLast: true, []));

        text.ShouldContain("nothing");
        text.ShouldNotContain("startAt:");
    }

    [Fact]
    public void A_page_of_verbose_boards_is_cut_to_the_response_budget_with_the_place_to_resume()
    {
        var boards = Enumerable.Range(1, 100)
            .Select(number => Board(number, new string('n', Truncation.BodyBudget)))
            .ToArray();

        var text = BoardList.Render(Page(isLast: true, boards));

        text.Length.ShouldBeLessThanOrEqualTo(SearchResults.ResponseBudget);
        text.ShouldContain("did not fit the response budget");
        text.ShouldContain("startAt: ");
    }

    [Fact]
    public void A_sprint_carries_its_state_and_only_the_dates_it_has()
    {
        var text = SprintList.Render(new JiraAgilePage<JiraSprint>(0, 50, IsLast: true, [
            new JiraSprint(12, "Sprint 4", "active", "2026-08-03T09:00:00.000+02:00", "2026-08-17T09:00:00.000+02:00"),
            new JiraSprint(13, "Sprint 5", "future", null, null),
        ]));

        text.ShouldContain("12 | Sprint 4 | active | start 2026-08-03T09:00:00.000+02:00");
        text.ShouldContain("13 | Sprint 5 | future");
        text.ShouldNotContain("13 | Sprint 5 | future | start");
    }

    private static JiraBoard Board(int id, string name) => new(id, name, "scrum");

    private static JiraAgilePage<JiraBoard> Page(bool isLast, params JiraBoard[] boards) =>
        new(0, boards.Length, isLast, boards);
}
