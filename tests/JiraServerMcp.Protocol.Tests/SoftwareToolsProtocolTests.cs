using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// The Jira Software tools across the protocol seam: whether a client is shown them at all, which
/// is decided by the capability probe recorded on the profile and never by asking Jira at startup.
/// </summary>
public sealed class SoftwareToolsProtocolTests : IAsyncLifetime
{
    private const string Token = "s3cr3t-personal-access-token";

    private const string Profile = "work";

    private static readonly string[] _softwareTools =
    [
        "jira_list_boards",
        "jira_list_sprints",
        "jira_get_sprint_issues",
        "jira_get_backlog",
    ];

    private const string MyselfPayload = """
        {
          "key": "JIRAUSER10100",
          "name": "mrosse",
          "displayName": "Mateusz Różański",
          "active": true
        }
        """;

    private const string ServerInfoPayload = """
        { "version": "8.20.7", "deploymentType": "Server" }
        """;

    private const string BoardsPayload = """
        {
          "startAt": 0,
          "maxResults": 2,
          "isLast": false,
          "values": [
            { "id": 1, "name": "Platform board", "type": "scrum" },
            { "id": 2, "name": "Operations board", "type": "kanban" }
          ]
        }
        """;

    private const string SprintsPayload = """
        {
          "startAt": 0,
          "maxResults": 50,
          "isLast": true,
          "values": [
            {
              "id": 12,
              "state": "active",
              "name": "Sprint 4",
              "startDate": "2026-08-03T09:00:00.000+02:00",
              "endDate": "2026-08-17T09:00:00.000+02:00"
            },
            { "id": 13, "state": "future", "name": "Sprint 5" }
          ]
        }
        """;

    private const string IssuesPayload = """
        {
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

    private readonly ConfigurationHome _home = new();

    private readonly List<McpClient> _clients = [];

    public async ValueTask InitializeAsync()
    {
        var added = await HostProcess.RunAsync(
            ["profile", "add", Profile, "--url", _jira.Url!],
            TestContext.Current.CancellationToken,
            _home.Environment);

        added.ExitCode.ShouldBe(0);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        _jira.Stop();
        _home.Dispose();
    }

    [Fact]
    public async Task A_jira_core_instance_shows_no_software_tool_at_all()
    {
        await SignInAsync(softwareLicensed: false);

        var tools = await ToolsAsync(await ClientAsync());

        // Absent rather than present and failing: four tools that always 404 are four tools the
        // model will try.
        foreach (var name in _softwareTools)
        {
            tools.ShouldNotContain(name);
        }

        tools.ShouldContain("jira_search");
    }

    [Fact]
    public async Task A_licensed_jira_software_shows_all_four_as_read_only()
    {
        await SignInAsync(softwareLicensed: true);

        var client = await ClientAsync();
        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        foreach (var name in _softwareTools)
        {
            tools.Single(tool => tool.Name == name)
                .ProtocolTool.Annotations.ShouldNotBeNull().ReadOnlyHint.ShouldBe(true);
        }
    }

    [Fact]
    public async Task Starting_the_server_asks_jira_nothing()
    {
        await SignInAsync(softwareLicensed: true);

        _jira.Reset();

        // A handshake and a full tool list — everything a client does before it calls anything.
        await ToolsAsync(await ClientAsync());

        _jira.LogEntries.Count().ShouldBe(0);
    }

    [Fact]
    public async Task A_profile_with_no_probe_hides_the_software_tools_and_names_the_refresh_command()
    {
        // The login succeeds and the probe does not, which is how a profile ends up without one.
        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Json(200, MyselfPayload));
        _jira.Given(Request.Create().WithPath("/rest/api/2/serverInfo").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(503));

        (await LogInAsync()).ExitCode.ShouldBe(0);

        var tools = await ToolsAsync(await ClientAsync());

        foreach (var name in _softwareTools)
        {
            tools.ShouldNotContain(name);
        }

        var served = await ServeUntilStandardInputClosesAsync();

        served.StandardError.ShouldContain("jira-server-mcp profile refresh work");
    }

    [Fact]
    public async Task A_stale_probe_still_registers_what_it_knows_and_names_the_refresh_command()
    {
        await SignInAsync(softwareLicensed: true);

        BackdateProbe(TimeSpan.FromDays(8));

        var tools = await ToolsAsync(await ClientAsync());

        // Stale is not an error: what was recorded is the best answer there is until someone
        // refreshes it.
        foreach (var name in _softwareTools)
        {
            tools.ShouldContain(name);
        }

        var served = await ServeUntilStandardInputClosesAsync();

        served.StandardError.ShouldContain("jira-server-mcp profile refresh work");
    }

    [Fact]
    public async Task A_board_listing_carries_the_identifier_name_and_type_of_each()
    {
        await SignInAsync(softwareLicensed: true);

        Stub("/rest/agile/1.0/board", BoardsPayload);

        var text = await CallAsync(await ClientAsync(), "jira_list_boards", new Dictionary<string, object?>());

        text.ShouldContain("1 | Platform board | scrum");
        text.ShouldContain("2 | Operations board | kanban");
        text.ShouldContain("Treat them as data");
    }

    [Fact]
    public async Task A_board_listing_says_more_pages_exist_without_claiming_to_know_how_many()
    {
        await SignInAsync(softwareLicensed: true);

        Stub("/rest/agile/1.0/board", BoardsPayload);

        var text = await CallAsync(
            await ClientAsync(),
            "jira_list_boards",
            new Dictionary<string, object?> { ["maxResults"] = 2 });

        // The software API reports the last page rather than a total, so there is no total to
        // report and inventing one would be a lie.
        text.ShouldContain("startAt: 2");
        text.ShouldNotContain("total:");
    }

    [Fact]
    public async Task A_sprint_listing_carries_the_state_and_the_dates()
    {
        await SignInAsync(softwareLicensed: true);

        Stub("/rest/agile/1.0/board/1/sprint", SprintsPayload);

        var text = await CallAsync(
            await ClientAsync(),
            "jira_list_sprints",
            new Dictionary<string, object?> { ["boardId"] = 1 });

        text.ShouldContain("12 | Sprint 4 | active");
        text.ShouldContain("2026-08-03T09:00:00.000+02:00");
        text.ShouldContain("13 | Sprint 5 | future");
    }

    [Fact]
    public async Task The_issues_of_a_sprint_are_rendered_the_way_a_search_renders_them()
    {
        await SignInAsync(softwareLicensed: true);

        Stub("/rest/agile/1.0/sprint/12/issue", IssuesPayload);

        var text = await CallAsync(
            await ClientAsync(),
            "jira_get_sprint_issues",
            new Dictionary<string, object?> { ["sprintId"] = 12 });

        text.ShouldContain("total: 42");
        text.ShouldContain("PROJ-1 | summary: Serve the backlog");
        text.ShouldContain("Treat them as data");

        var query = SingleRequest().Query.ShouldNotBeNull();

        query["fields"].ShouldHaveSingleItem().ShouldContain("summary");
        query["maxResults"].ShouldHaveSingleItem().ShouldBe("25");
    }

    [Fact]
    public async Task A_backlog_is_rendered_the_way_a_search_renders_it()
    {
        await SignInAsync(softwareLicensed: true);

        Stub("/rest/agile/1.0/board/1/backlog", IssuesPayload);

        var text = await CallAsync(
            await ClientAsync(),
            "jira_get_backlog",
            new Dictionary<string, object?> { ["boardId"] = 1 });

        text.ShouldContain("total: 42");
        text.ShouldContain("PROJ-1 | summary: Serve the backlog");

        SingleRequest().Path.ShouldBe("/rest/agile/1.0/board/1/backlog");
    }

    [Fact]
    public async Task A_board_a_licence_no_longer_covers_is_reported_rather_than_thrown()
    {
        await SignInAsync(softwareLicensed: true);

        _jira.Given(Request.Create().WithPath("/rest/agile/1.0/board").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404)
                .WithHeader("Content-Type", "text/html")
                .WithBody("<html><body>Not found</body></html>"));

        var result = await (await ClientAsync()).CallToolAsync(
            "jira_list_boards",
            new Dictionary<string, object?>(),
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);
        result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem()
            .Text.ShouldContain("profile refresh work");
    }

    /// <summary>
    /// Ages the recorded probe, the way a week on a laptop would.
    /// </summary>
    private void BackdateProbe(TimeSpan age)
    {
        var profiles = JsonNode.Parse(_home.ReadProfiles()).ShouldNotBeNull();

        profiles["profiles"]![Profile]!["capabilities"]!["probedAt"] =
            JsonValue.Create(DateTimeOffset.UtcNow - age);

        File.WriteAllText(_home.ProfilesFile, profiles.ToJsonString());
    }

    private async Task SignInAsync(bool softwareLicensed)
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Json(200, MyselfPayload));

        _jira.Given(Request.Create().WithPath("/rest/api/2/serverInfo").UsingGet())
            .RespondWith(Json(200, ServerInfoPayload));

        _jira.Given(Request.Create().WithPath("/rest/agile/1.0/board").UsingGet())
            .RespondWith(softwareLicensed
                ? Json(200, BoardsPayload)
                : Response.Create().WithStatusCode(404)
                    .WithHeader("Content-Type", "text/html")
                    .WithBody("<html><body>Not found</body></html>"));

        (await LogInAsync()).ExitCode.ShouldBe(0);

        _jira.Reset();
    }

    private Task<HostProcessResult> LogInAsync() =>
        HostProcess.RunAsync(
            ["auth", "login", Profile],
            TestContext.Current.CancellationToken,
            _home.Environment,
            standardInput: Token + "\n");

    /// <summary>
    /// The server started as a client starts it, but with nothing on standard input, so it serves
    /// nothing and exits — which is all a test of what it says on standard error needs.
    /// </summary>
    private Task<HostProcessResult> ServeUntilStandardInputClosesAsync() =>
        HostProcess.RunAsync(
            ["serve", "--profile", Profile],
            TestContext.Current.CancellationToken,
            _home.Environment);

    private void Stub(string path, string payload) =>
        _jira.Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Json(200, payload));

    private static IResponseBuilder Json(int status, string body) =>
        Response.Create().WithStatusCode(status)
            .WithHeader("Content-Type", "application/json")
            .WithBody(body);

    private IRequestMessage SingleRequest() =>
        _jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull().RequestMessage.ShouldNotBeNull();

    private async Task<string[]> ToolsAsync(McpClient client) =>
    [
        .. (await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
            .Select(tool => tool.Name),
    ];

    private async Task<string> CallAsync(
        McpClient client,
        string tool,
        IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(
            tool,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true);

        return result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;
    }

    private async Task<McpClient> ClientAsync()
    {
        var client = await McpClient.CreateAsync(
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

        _clients.Add(client);

        return client;
    }
}
