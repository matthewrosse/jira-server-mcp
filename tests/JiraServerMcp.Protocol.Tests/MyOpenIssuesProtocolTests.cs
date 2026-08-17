using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// <c>jira_my_open_issues</c> across the protocol seam: what a real MCP client receives, and what
/// the faked Jira was asked for.
/// </summary>
public sealed class MyOpenIssuesProtocolTests : IAsyncLifetime
{
    private const string Token = "s3cr3t-personal-access-token";

    private const string Profile = "work";

    private const string BaseJql = "assignee = currentUser() AND resolution = Unresolved ORDER BY updated DESC";

    private const string MyselfPayload = """
        {
          "key": "JIRAUSER10100",
          "name": "mrosse",
          "emailAddress": "mrosse@example.com",
          "displayName": "Mateusz Różański",
          "active": true
        }
        """;

    private readonly WireMockServer _jira = WireMockServer.Start();

    private readonly ConfigurationHome _home = new();

    private McpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var added = await HostProcess.RunAsync(
            ["profile", "add", Profile, "--url", _jira.Url!],
            TestContext.Current.CancellationToken,
            _home.Environment);

        added.ExitCode.ShouldBe(0);

        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Json(MyselfPayload));

        var loggedIn = await HostProcess.RunAsync(
            ["auth", "login", Profile],
            TestContext.Current.CancellationToken,
            _home.Environment,
            standardInput: Token + "\n");

        loggedIn.ExitCode.ShouldBe(0);

        _jira.Reset();

        _client = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "jira-server-mcp",
                Command = HostProcess.Command,
                Arguments = HostProcess.ArgumentsFor("serve", "--profile", Profile),
                EnvironmentVariables = _home.Environment.ToDictionary(
                    entry => entry.Key,
                    entry => (string?)entry.Value),
            }),
            cancellationToken: TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _jira.Stop();
        _home.Dispose();
    }

    [Fact]
    public async Task The_client_sees_the_tool_as_read_only_with_no_required_parameters()
    {
        var tools = await _client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var tool = tools.Single(entry => entry.Name is "jira_my_open_issues");

        tool.ProtocolTool.Annotations.ShouldNotBeNull().ReadOnlyHint.ShouldBe(true);

        var properties = tool.JsonSchema.GetProperty("properties");

        properties.TryGetProperty("project", out _).ShouldBeTrue();
        properties.TryGetProperty("startAt", out _).ShouldBeTrue();
        properties.TryGetProperty("maxResults", out _).ShouldBeTrue();
        properties.TryGetProperty("fields", out _).ShouldBeTrue();

        if (tool.JsonSchema.TryGetProperty("required", out var required))
        {
            required.EnumerateArray().ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task With_no_project_the_query_is_the_bare_canned_jql()
    {
        StubSearch(Json(SearchPayload(total: 1, ("PROJ-12", "Login fails with a 401"))));

        var text = await MyOpenIssuesAsync(new Dictionary<string, object?>());

        text.ShouldContain($"jql: {BaseJql}");
        text.ShouldContain("PROJ-12");

        SingleRequest().Query.ShouldNotBeNull()["jql"].ShouldHaveSingleItem().ShouldBe(BaseJql);
    }

    [Fact]
    public async Task A_project_is_prefixed_onto_the_canned_jql()
    {
        StubSearch(Json(SearchPayload(total: 1, ("PROJ-12", "Login fails with a 401"))));

        var expected = $"project = PROJ AND {BaseJql}";

        var text = await MyOpenIssuesAsync(new Dictionary<string, object?> { ["project"] = "PROJ" });

        text.ShouldContain($"jql: {expected}");

        SingleRequest().Query.ShouldNotBeNull()["jql"].ShouldHaveSingleItem().ShouldBe(expected);
    }

    [Fact]
    public async Task A_project_key_that_fails_the_grammar_is_rejected_before_any_jira_call()
    {
        var result = await _client.CallToolAsync(
            "jira_my_open_issues",
            new Dictionary<string, object?> { ["project"] = "proj-1; DROP" },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true);

        result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem()
            .Text.ShouldContain("not a valid Jira project key");

        _jira.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_default_page_and_projection_match_search()
    {
        StubSearch(Json(SearchPayload(total: 1, ("PROJ-12", "Login fails with a 401"))));

        await MyOpenIssuesAsync(new Dictionary<string, object?>());

        var query = SingleRequest().Query.ShouldNotBeNull();

        query["startAt"].ShouldHaveSingleItem().ShouldBe("0");
        query["maxResults"].ShouldHaveSingleItem().ShouldBe("25");
        query["fields"].ShouldHaveSingleItem().ShouldBe(
            "summary,status,issuetype,priority,assignee,reporter,created,updated,parent,labels");
    }

    [Fact]
    public async Task A_request_for_more_than_a_hundred_results_is_clamped_rather_than_rejected()
    {
        StubSearch(Json(SearchPayload(total: 4_000, ("PROJ-12", "Login fails with a 401"))));

        await MyOpenIssuesAsync(new Dictionary<string, object?> { ["maxResults"] = 500 });

        SingleRequest().Query.ShouldNotBeNull()["maxResults"].ShouldHaveSingleItem().ShouldBe("100");
    }

    private async Task<string> MyOpenIssuesAsync(IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await _client.CallToolAsync(
            "jira_my_open_issues",
            arguments,
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true);

        return result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;
    }

    private static string SearchPayload(int total, params (string Key, string Summary)[] issues)
    {
        var rendered = issues.Select(issue => $$"""
            {
              "key": "{{issue.Key}}",
              "fields": {
                "summary": "{{issue.Summary}}",
                "status": { "name": "In Progress" },
                "issuetype": { "name": "Bug" },
                "assignee": { "name": "mrosse", "displayName": "Mateusz Różański" },
                "labels": ["api", "backend"]
              }
            }
            """);

        return JsonSerializer.Serialize(new
        {
            startAt = 0,
            maxResults = 100,
            total,
        }).TrimEnd('}')
           + ",\"issues\":[" + string.Join(",", rendered) + "]}";
    }

    private static IResponseBuilder Json(string body) =>
        Response.Create().WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(body);

    private IRequestMessage SingleRequest() =>
        _jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull().RequestMessage.ShouldNotBeNull();

    private void StubSearch(IResponseBuilder response) =>
        _jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
            .RespondWith(response);
}
