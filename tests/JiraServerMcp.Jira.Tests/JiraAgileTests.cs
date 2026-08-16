using Microsoft.Extensions.DependencyInjection;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// The software API reads — boards, sprints, sprint issues, backlog — against an HTTP double. The
/// software API pages by telling a caller whether it has reached the last page; the platform API
/// pages by counting everything. Both conventions are exercised here.
/// </summary>
public sealed class JiraAgileTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    private const string BoardsPayload = """
        {
          "maxResults": 2,
          "startAt": 0,
          "isLast": false,
          "values": [
            { "id": 1, "self": "http://jira/rest/agile/1.0/board/1", "name": "Platform board", "type": "scrum" },
            { "id": 2, "self": "http://jira/rest/agile/1.0/board/2", "name": "Operations board", "type": "kanban" }
          ]
        }
        """;

    private const string LastBoardsPayload = """
        {
          "maxResults": 50,
          "startAt": 0,
          "isLast": true,
          "values": [ { "id": 1, "name": "Platform board", "type": "scrum" } ]
        }
        """;

    private const string SprintsPayload = """
        {
          "maxResults": 50,
          "startAt": 0,
          "isLast": true,
          "values": [
            {
              "id": 12,
              "state": "active",
              "name": "Sprint 4",
              "startDate": "2026-08-03T09:00:00.000+02:00",
              "endDate": "2026-08-17T09:00:00.000+02:00",
              "originBoardId": 1
            },
            { "id": 13, "state": "future", "name": "Sprint 5" }
          ]
        }
        """;

    private const string IssuesPayload = """
        {
          "expand": "schema,names",
          "startAt": 0,
          "maxResults": 25,
          "total": 42,
          "issues": [
            {
              "id": "10000",
              "key": "PROJ-1",
              "fields": {
                "summary": "Serve the backlog",
                "status": { "name": "Open" }
              }
            }
          ]
        }
        """;

    private readonly WireMockServer _jira = WireMockServer.Start();
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _jira.Stop();
    }

    [Fact]
    public async Task A_board_carries_its_identifier_name_and_type()
    {
        Stub("/rest/agile/1.0/board", BoardsPayload);

        var page = await CreateClient().ListBoardsAsync(
            startAt: 0,
            maxResults: 2,
            TestContext.Current.CancellationToken);

        page.Values.Count.ShouldBe(2);
        page.Values[0].Id.ShouldBe(1);
        page.Values[0].Name.ShouldBe("Platform board");
        page.Values[0].Type.ShouldBe("scrum");
        page.Values[1].Type.ShouldBe("kanban");
    }

    [Fact]
    public async Task The_software_api_reports_the_last_page_rather_than_a_total()
    {
        Stub("/rest/agile/1.0/board", BoardsPayload);

        var page = await CreateClient().ListBoardsAsync(0, 2, TestContext.Current.CancellationToken);

        page.HasMore.ShouldBeTrue();

        // The page size, not the rows returned: Jira filters a page by permission after paging it.
        page.NextStartAt.ShouldBe(2);
    }

    [Fact]
    public async Task The_last_page_of_a_software_api_listing_says_there_is_no_more()
    {
        Stub("/rest/agile/1.0/board", LastBoardsPayload);

        var page = await CreateClient().ListBoardsAsync(0, 50, TestContext.Current.CancellationToken);

        page.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task A_board_listing_names_the_page_it_wants()
    {
        Stub("/rest/agile/1.0/board", BoardsPayload);

        await CreateClient().ListBoardsAsync(10, 5, TestContext.Current.CancellationToken);

        var query = SingleRequest().Query.ShouldNotBeNull();

        query["startAt"].ShouldHaveSingleItem().ShouldBe("10");
        query["maxResults"].ShouldHaveSingleItem().ShouldBe("5");
    }

    [Fact]
    public async Task A_sprint_carries_its_identifier_name_state_and_dates()
    {
        Stub("/rest/agile/1.0/board/1/sprint", SprintsPayload);

        var page = await CreateClient().ListSprintsAsync(
            boardId: 1,
            startAt: 0,
            maxResults: 50,
            TestContext.Current.CancellationToken);

        page.Values.Count.ShouldBe(2);
        page.Values[0].Id.ShouldBe(12);
        page.Values[0].Name.ShouldBe("Sprint 4");
        page.Values[0].State.ShouldBe("active");
        page.Values[0].StartDate.ShouldBe("2026-08-03T09:00:00.000+02:00");
        page.Values[0].EndDate.ShouldBe("2026-08-17T09:00:00.000+02:00");

        // A future sprint has no dates yet, and that is ordinary rather than missing data.
        page.Values[1].StartDate.ShouldBeNull();
        page.Values[1].EndDate.ShouldBeNull();
    }

    [Fact]
    public async Task The_issues_of_a_sprint_come_back_as_a_page_counted_the_platform_way()
    {
        Stub("/rest/agile/1.0/sprint/12/issue", IssuesPayload);

        var page = await CreateClient().GetSprintIssuesAsync(
            sprintId: 12,
            startAt: 0,
            maxResults: 25,
            ["summary", "status"],
            TestContext.Current.CancellationToken);

        page.Total.ShouldBe(42);
        page.HasMore.ShouldBeTrue();
        page.Issues.ShouldHaveSingleItem().Key.ShouldBe("PROJ-1");

        var query = SingleRequest().Query.ShouldNotBeNull();

        query["startAt"].ShouldHaveSingleItem().ShouldBe("0");
        query["maxResults"].ShouldHaveSingleItem().ShouldBe("25");
        query["fields"].ShouldHaveSingleItem().ShouldBe("summary,status");
    }

    [Fact]
    public async Task A_backlog_comes_back_as_a_page_counted_the_platform_way()
    {
        Stub("/rest/agile/1.0/board/1/backlog", IssuesPayload);

        var page = await CreateClient().GetBacklogAsync(
            boardId: 1,
            startAt: 0,
            maxResults: 25,
            ["summary", "status"],
            TestContext.Current.CancellationToken);

        page.Total.ShouldBe(42);
        page.Issues.ShouldHaveSingleItem().Key.ShouldBe("PROJ-1");

        SingleRequest().Path.ShouldBe("/rest/agile/1.0/board/1/backlog");
    }

    private void Stub(string path, string payload) =>
        _jira.Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(payload));

    private IRequestMessage SingleRequest() =>
        _jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull().RequestMessage.ShouldNotBeNull();

    private JiraClient CreateClient()
    {
        var services = new ServiceCollection();

        services.AddJiraClient();
        services.Configure<JiraClientOptions>(options =>
        {
            options.BaseUrl = new Uri(_jira.Url!, UriKind.Absolute);
            options.PersonalAccessToken = Token;
        });

        var provider = services.BuildServiceProvider();

        _providers.Add(provider);

        return provider.GetRequiredService<JiraClient>();
    }
}
