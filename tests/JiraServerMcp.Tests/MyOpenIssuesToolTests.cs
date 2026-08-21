using System.Diagnostics;
using JiraServerMcp.Jira;
using JiraServerMcp.Profiles;
using JiraServerMcp.Tools;
using ModelContextProtocol.Protocol;

namespace JiraServerMcp.Tests;

/// <summary>
/// The JQL <c>jira_my_open_issues</c> builds, and the project-key grammar guarding the
/// interpolation seam — both pure, and so proven here under ADR-0008 clause 3 rather than through
/// a Jira the tool never needed to reach. What an agent observes when it calls the tool is proven
/// at the protocol seam, in <c>MyOpenIssuesProtocolTests</c>.
/// </summary>
public sealed class MyOpenIssuesToolTests
{
    [Fact]
    public void With_no_project_the_jql_is_the_bare_canned_query()
    {
        var jql = MyOpenIssues.Jql(null);

        jql.ShouldBe("assignee = currentUser() AND resolution = Unresolved ORDER BY updated DESC");
    }

    [Fact]
    public void A_project_narrows_the_canned_query_rather_than_replacing_it()
    {
        MyOpenIssues.Jql("PROJ").ShouldBe($"project = PROJ AND {MyOpenIssues.Jql(null)}");
    }

    [Fact]
    public void The_feed_is_ordered_by_the_most_recently_updated_issue()
    {
        MyOpenIssues.Jql("PROJ").ShouldEndWith("ORDER BY updated DESC");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1PROJ")]
    [InlineData("PROJ KEY")]
    [InlineData("PROJ-1")]
    [InlineData("PROJ; DROP TABLE")]
    public void A_project_key_outside_the_grammar_never_reaches_the_query(string project)
    {
        ProjectKey.IsValid(project).ShouldBeFalse();

        var rejected = ProjectKey.Rejected(project);

        rejected.ShouldContain("not a valid Jira project key");
        rejected.ShouldContain("jira_search");
    }

    [Fact]
    public async Task A_jira_that_never_answers_names_jira_search_as_a_narrower_fallback()
    {
        var tool = Tool(new NeverAnswers(), TimeSpan.FromMilliseconds(200));

        var result = await tool.MyOpenIssuesAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);
        Text(result).ShouldContain("did not answer");
        Text(result).ShouldContain("jira_search");
    }

    private static MyOpenIssuesTool Tool(HttpMessageHandler handler, TimeSpan timeout) =>
        new(
            new JiraClient(new HttpClient(handler)
            {
                BaseAddress = new Uri("https://jira.example.com", UriKind.Absolute),
                Timeout = timeout,
            }),
            new ServedProfile("work"),
            FieldAliases.None);

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;

    /// <summary>
    /// A socket that hangs, which is the one thing WireMock at the protocol seam cannot stage.
    /// ADR-0008 names its transport carve-out for <c>WhoamiToolTests</c> alone, so this case is
    /// kept on the same reasoning rather than under that name: it is the transport failure mode,
    /// and nothing about the tool's own branching is proven here.
    /// </summary>
    private sealed class NeverAnswers : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);

            throw new UnreachableException();
        }
    }
}
