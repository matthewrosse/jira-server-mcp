using JiraServerMcp.Rendering;

namespace JiraServerMcp.Tests;

public class ResponseBudgetTests
{
    [Fact]
    public void The_response_budget_names_the_limits_that_bound_an_issue_read_and_its_pages()
    {
        ResponseBudget.LineText.ShouldBe(200);
        ResponseBudget.Prose.ShouldBe(1_000);
        ResponseBudget.IssueSection.ShouldBe(20);
        ResponseBudget.DefaultPageSize.ShouldBe(25);
        ResponseBudget.LargestPageSize.ShouldBe(100);
    }
}
