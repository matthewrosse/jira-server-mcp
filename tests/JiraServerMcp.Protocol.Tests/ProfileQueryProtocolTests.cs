using ModelContextProtocol.Protocol;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// A profile's own canned queries across the protocol seam (ADR-0008). This is also where the
/// instance registration path is confirmed: every other tool this server offers is a type, and the
/// SDK's batch registration of types is known to mis-register a list — so what an agent actually
/// sees is asserted rather than assumed.
/// </summary>
public sealed class ProfileQueryProtocolTests : IAsyncLifetime
{
    private const string OnePage = """
        {
          "startAt": 0,
          "maxResults": 25,
          "total": 1,
          "issues": [
            {
              "key": "PROJ-42",
              "fields": {
                "summary": "It fell over",
                "status": { "id": "3", "name": "In Progress" }
              }
            }
          ]
        }
        """;

    private ProtocolSeam _seam = null!;

    public async ValueTask InitializeAsync() => _seam = await ProtocolSeam.StartAsync();

    public async ValueTask DisposeAsync() => await _seam.DisposeAsync();

    [Fact]
    public async Task A_declared_query_is_listed_as_a_tool_carrying_the_operators_description()
    {
        StubSearch();
        (await AddQueryAsync("sprint_bugs", "type = Bug AND sprint in openSprints()",
            "This team's bugs in the current sprint.")).ExitCode.ShouldBe(0);

        var client = await _seam.ConnectAsync();

        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var tool = tools.Single(entry => entry.Name is "jira_q_sprint_bugs");

        tool.Description.ShouldNotBeNull().ShouldContain("This team's bugs in the current sprint.");
        tool.ProtocolTool.Annotations.ShouldNotBeNull().ReadOnlyHint.ShouldBe(true);

        // The built-in tools are still all there: an instance registered beside them displaces
        // nothing.
        tools.Select(entry => entry.Name).ShouldContain("jira_search");
        tools.Select(entry => entry.Name).ShouldContain("jira_my_open_issues");
    }

    [Fact]
    public async Task Several_declared_queries_all_register_rather_than_the_last_one_winning()
    {
        StubSearch();

        foreach (var name in new[] { "sprint_bugs", "blocked", "waiting_on_review" })
        {
            (await AddQueryAsync(name, $"labels = {name}", $"The {name} query.")).ExitCode.ShouldBe(0);
        }

        var client = await _seam.ConnectAsync();

        var tools = (await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken))
            .Select(entry => entry.Name)
            .ToArray();

        // The SDK mis-registers a batch of types handed over in one call, which is why the server
        // registers one at a time. This is the assertion that would catch the same fault on the
        // instance path.
        tools.ShouldContain("jira_q_sprint_bugs");
        tools.ShouldContain("jira_q_blocked");
        tools.ShouldContain("jira_q_waiting_on_review");
    }

    [Fact]
    public async Task Calling_one_runs_its_jql_and_answers_as_a_built_in_query_does()
    {
        StubSearch();
        (await AddQueryAsync("sprint_bugs", "type = Bug AND sprint in openSprints()",
            "This team's bugs in the current sprint.")).ExitCode.ShouldBe(0);

        var client = await _seam.ConnectAsync();

        var result = await client.CallToolAsync(
            "jira_q_sprint_bugs",
            new Dictionary<string, object?>(),
            cancellationToken: TestContext.Current.CancellationToken);

        var text = result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;

        result.IsError.ShouldNotBe(true, text);

        text.ShouldContain("jql: type = Bug AND sprint in openSprints()");
        text.ShouldContain("PROJ-42");

        // The same structured half a built-in page of issues carries (ADR-0009), through the same
        // renderer and the same budget.
        var structure = result.StructuredContent.ShouldNotBeNull();

        structure.GetProperty("outcome").GetString().ShouldBe("ok");
        structure.GetProperty("total").GetInt32().ShouldBe(1);
        structure.GetProperty("issues").EnumerateArray()
            .Select(issue => issue.GetProperty("key").GetString())
            .ShouldBe(["PROJ-42"]);
    }

    [Fact]
    public async Task A_query_that_goes_bad_later_fails_at_call_time_through_the_ordinary_refusal()
    {
        StubSearch();
        (await AddQueryAsync("deleted_project", "project = GONE",
            "Everything in a project that will be deleted.")).ExitCode.ShouldBe(0);

        // The project is deleted after the query was declared, which is the case add-time checking
        // cannot catch and does not claim to.
        _seam.Jira.Reset();
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
            .RespondWith(JiraResponse.Json(400, """
                {"errorMessages":["The value 'GONE' does not exist for the field 'project'."],"errors":{}}
                """));

        var client = await _seam.ConnectAsync();

        var result = await client.CallToolAsync(
            "jira_q_deleted_project",
            new Dictionary<string, object?>(),
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        var structure = result.StructuredContent.ShouldNotBeNull();

        structure.GetProperty("outcome").GetString().ShouldBe("jira_api");
        structure.GetProperty("statusCode").GetInt32().ShouldBe(400);
    }

    [Fact]
    public async Task Jql_jira_will_not_run_is_refused_when_it_is_declared()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
            .RespondWith(JiraResponse.Json(400, """
                {"errorMessages":["Error in the JQL Query: Expecting operator but got 'bugs'."],"errors":{}}
                """));

        var added = await AddQueryAsync("bad", "this is not jql", "A query that does not parse.");

        added.ExitCode.ShouldBe(1);
        added.StandardError.ShouldContain("would not run that query");

        // Nothing was stored, so nothing is offered.
        var client = await _seam.ConnectAsync();

        (await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Select(entry => entry.Name)
            .ShouldNotContain("jira_q_bad");
    }

    [Fact]
    public async Task A_jira_that_hangs_leaves_the_query_unchecked_rather_than_crashing_the_verb()
    {
        // A laptop off the VPN, or an address that black-holes. The client gives up after its own
        // timeout, which arrives as a cancellation nobody asked for.
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(OnePage)
                .WithDelay(TimeSpan.FromSeconds(40)));

        var added = await AddQueryAsync("slow", "labels = slow", "A query against a hung Jira.");

        added.ExitCode.ShouldBe(1);
        added.StandardError.ShouldContain("did not answer in time");
        added.StandardError.ShouldNotContain("Unhandled exception");
    }

    [Fact]
    public async Task An_empty_jql_is_refused_because_jira_reads_it_as_every_issue_it_has()
    {
        StubSearch();

        var added = await AddQueryAsync("everything", "   ", "A quoting slip.");

        added.ExitCode.ShouldBe(1);
        added.StandardError.ShouldContain("every issue on the instance");

        // Refused before Jira was troubled with it, since Jira would have accepted it.
        _seam.Jira.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_eleventh_query_is_refused_with_the_reason()
    {
        StubSearch();

        for (var number = 1; number <= 10; number++)
        {
            (await AddQueryAsync($"q{number}", $"labels = q{number}", $"Query {number}."))
                .ExitCode.ShouldBe(0);
        }

        var eleventh = await AddQueryAsync("q11", "labels = q11", "One too many.");

        eleventh.ExitCode.ShouldBe(1);
        eleventh.StandardError.ShouldContain("which is the limit");

        // Every registered tool costs an agent context in every conversation, which is the reason
        // the cap exists rather than a README warning.
        eleventh.StandardError.ShouldContain("costs an agent context");
    }

    private async Task<HostProcessResult> AddQueryAsync(string name, string jql, string description) =>
        await HostProcess.RunAsync(
            ["profile", "query", "add", ProtocolSeam.Profile, name, "--jql", jql, "--description", description],
            TestContext.Current.CancellationToken,
            _seam.Home.Environment);

    private void StubSearch() =>
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
            .RespondWith(JiraResponse.Json(200, OnePage));
}
