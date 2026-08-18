using System.Text.Json;
using ModelContextProtocol.Client;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// The structured half as a real MCP client receives it (ADR-0008, ADR-0009). The promise the
/// README makes is that structure is present on every result, success and failure alike, and that
/// every tool declares the schema for it — so this asserts it of every registered tool rather than
/// of a sample, which under ADR-0008 makes it a fact test rather than a taste test.
/// </summary>
public sealed class StructuredContentProtocolTests : IAsyncLifetime
{
    private const string Token = "s3cr3t-personal-access-token";

    private const string Profile = "work";

    private const string MyselfPayload = """
        {
          "key": "JIRAUSER10100",
          "name": "mrosse",
          "displayName": "Mateusz Różański",
          "active": true
        }
        """;

    /// <summary>
    /// The arguments each tool needs to get as far as talking to Jira. The values do not matter —
    /// what matters is that the call reaches the failing Jira below rather than being refused
    /// before it, so that the failure being asserted is the one under test.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Dictionary<string, object?>> _arguments =
        new Dictionary<string, Dictionary<string, object?>>
        {
            ["jira_whoami"] = [],
            ["jira_search"] = new() { ["jql"] = "project = PROJ" },
            ["jira_my_open_issues"] = [],
            ["jira_get_issues"] = new() { ["keys"] = new[] { "PROJ-12" } },
            ["jira_list_projects"] = [],
            ["jira_get_project"] = new() { ["key"] = "PROJ" },
            ["jira_get_create_fields"] = new()
            {
                ["projectKey"] = "PROJ",
                ["issueType"] = "Bug",
            },
            ["jira_search_users"] = new() { ["query"] = "ada" },
            ["jira_list_boards"] = [],
            ["jira_list_sprints"] = new() { ["boardId"] = 1 },
            ["jira_get_sprint_issues"] = new() { ["sprintId"] = 1 },
            ["jira_get_backlog"] = new() { ["boardId"] = 1 },
            ["jira_create_issue"] = new()
            {
                ["projectKey"] = "PROJ",
                ["issueType"] = "Bug",
                ["summary"] = "It fell over",
            },
            ["jira_update_issue"] = new()
            {
                ["key"] = "PROJ-12",
                ["fields"] = new Dictionary<string, object?> { ["summary"] = "Renamed" },
            },
            ["jira_transition_issue"] = new()
            {
                ["key"] = "PROJ-12",
                ["transition"] = "Done",
            },
            ["jira_add_comment"] = new()
            {
                ["key"] = "PROJ-12",
                ["body"] = "Looked at it.",
            },
            ["jira_add_worklog"] = new()
            {
                ["key"] = "PROJ-12",
                ["timeSpent"] = "2h",
            },
            ["jira_link_issues"] = new()
            {
                ["from"] = "PROJ-1",
                ["to"] = "PROJ-2",
                ["relation"] = "blocks",
            },
            ["jira_add_remote_link"] = new()
            {
                ["key"] = "PROJ-12",
                ["url"] = "https://example.invalid/pr/1",
                ["title"] = "A pull request",
            },
        };

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
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(MyselfPayload));

        var loggedIn = await HostProcess.RunAsync(
            ["auth", "login", Profile],
            TestContext.Current.CancellationToken,
            _home.Environment,
            standardInput: Token + "\n");

        loggedIn.ExitCode.ShouldBe(0);

        // Every tool is registered, licence and grants included, so the sweeps below see the whole
        // surface rather than the read-only part of it. That takes a capability probe recording a
        // Jira Software licence, which is what these two stubs are.
        _jira.Given(Request.Create().WithPath("/rest/api/2/serverInfo").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "version": "8.20.7", "deploymentType": "Server" }"""));

        _jira.Given(Request.Create().WithPath("/rest/agile/1.0/board").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "startAt": 0, "maxResults": 1, "isLast": true, "values": [] }"""));

        var probed = await HostProcess.RunAsync(
            ["profile", "refresh", Profile],
            TestContext.Current.CancellationToken,
            _home.Environment);

        probed.ExitCode.ShouldBe(0);

        _jira.Reset();

        _client = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "jira-server-mcp",
                Command = HostProcess.Command,
                Arguments = HostProcess.ArgumentsFor(
                [
                    "serve",
                    "--profile",
                    Profile,
                    "--allow",
                    "issues:write,comments:write,worklogs:write,links:write",
                ]),
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
    public async Task Every_registered_tool_declares_an_output_schema()
    {
        var tools = await _client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        foreach (var tool in tools)
        {
            var schema = tool.ProtocolTool.OutputSchema.ShouldNotBeNull(
                $"{tool.Name} carries structured content, so it must say what shape it is.");

            // Only the outcome is promised on every result. Anything else is optional, because a
            // failed call carries the outcome alone and must still satisfy the schema.
            schema.GetProperty("required").EnumerateArray()
                .Select(required => required.GetString())
                .ShouldBe(["outcome"], $"{tool.Name}'s schema promises more than a failure can carry.");
        }
    }

    [Fact]
    public async Task Every_registered_tool_carries_the_outcome_when_jira_refuses()
    {
        // One Jira answering everything with a 403, so every tool fails the same way and the
        // sweep is about the envelope rather than about each tool's own path.
        _jira.Given(Request.Create().WithPath("/*").UsingAnyMethod())
            .RespondWith(Response.Create().WithStatusCode(403)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "errorMessages": ["You do not have permission"], "errors": {} }"""));

        var tools = await _client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        foreach (var tool in tools)
        {
            var structure = await CallAsync(tool.Name);

            structure.GetProperty("outcome").GetString()
                .ShouldBe("jira_api", $"{tool.Name} answered a refused call with the wrong outcome.");

            structure.GetProperty("statusCode").GetInt32()
                .ShouldBe(403, $"{tool.Name} lost the status Jira answered with.");
        }
    }

    [Fact]
    public async Task Every_registered_tool_carries_the_outcome_when_jira_cannot_be_reached()
    {
        _jira.Stop();

        var tools = await _client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        foreach (var tool in tools)
        {
            var structure = await CallAsync(tool.Name);

            // The branch an agent most needs and can least reliably recover from a sentence.
            structure.GetProperty("outcome").GetString()
                .ShouldBe("unreachable", $"{tool.Name} did not say it never reached Jira.");
        }
    }

    [Fact]
    public async Task A_refusal_this_server_made_itself_says_nothing_was_attempted()
    {
        var structure = await CallAsync(
            "jira_add_comment",
            new Dictionary<string, object?> { ["key"] = "PROJ-12", ["body"] = "   " });

        structure.GetProperty("outcome").GetString().ShouldBe("refused");
    }

    [Fact]
    public async Task A_search_carries_its_rows_and_its_position()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    {
                      "startAt": 0,
                      "maxResults": 25,
                      "total": 1,
                      "issues": [
                        {
                          "key": "PROJ-12",
                          "fields": {
                            "summary": "Login fails with a 401",
                            "status": { "id": "3", "name": "In Progress" },
                            "issuetype": { "name": "Bug" },
                            "assignee": { "name": "mrosse", "displayName": "Mateusz Różański" }
                          }
                        }
                      ]
                    }
                    """));

        var structure = await CallAsync(
            "jira_search",
            new Dictionary<string, object?> { ["jql"] = "project = PROJ" });

        structure.GetProperty("outcome").GetString().ShouldBe("ok");
        structure.GetProperty("total").GetInt32().ShouldBe(1);

        var row = structure.GetProperty("issues").EnumerateArray().ShouldHaveSingleItem();

        row.GetProperty("key").GetString().ShouldBe("PROJ-12");
        row.GetProperty("status").GetString().ShouldBe("In Progress");
        row.GetProperty("assignee").GetString().ShouldBe("mrosse");

        // The summary is prose. It is in the text half, where the delimiters are, and nowhere else.
        row.TryGetProperty("summary", out _).ShouldBeFalse();
        structure.GetRawText().ShouldNotContain("Login fails");
    }

    [Fact]
    public async Task A_write_carries_the_identifiers_it_created()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "id": "10412", "key": "PROJ-31" }"""));

        var structure = await CallAsync("jira_create_issue");

        structure.GetProperty("outcome").GetString().ShouldBe("ok");
        structure.GetProperty("key").GetString().ShouldBe("PROJ-31");
        structure.GetProperty("id").GetString().ShouldBe("10412");
        structure.GetProperty("projectKey").GetString().ShouldBe("PROJ");
    }

    [Fact]
    public async Task A_bulk_read_keeps_one_shape_whether_or_not_it_is_an_error()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-12").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    {
                      "key": "PROJ-12",
                      "fields": {
                        "summary": "Login fails with a 401",
                        "status": { "id": "3", "name": "In Progress" },
                        "issuetype": { "name": "Bug" }
                      }
                    }
                    """));

        _jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-99").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "errorMessages": ["Issue Does Not Exist"], "errors": {} }"""));

        var partial = await CallAsync(
            "jira_get_issues",
            new Dictionary<string, object?> { ["keys"] = new[] { "PROJ-12", "PROJ-99" } });

        partial.GetProperty("outcome").GetString().ShouldBe("ok");
        partial.GetProperty("asked").GetInt32().ShouldBe(2);
        partial.GetProperty("returned").GetInt32().ShouldBe(1);
        partial.GetProperty("failures").EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("outcome").GetString().ShouldBe("not_found");

        var whollyFailed = await CallAsync(
            "jira_get_issues",
            new Dictionary<string, object?> { ["keys"] = new[] { "PROJ-99" } });

        // isError is set on this one, and the shape is the same: a caller must not have to learn
        // two shapes to read one tool.
        whollyFailed.GetProperty("asked").GetInt32().ShouldBe(1);
        whollyFailed.GetProperty("returned").GetInt32().ShouldBe(0);
        whollyFailed.GetProperty("issues").EnumerateArray().ShouldBeEmpty();
    }

    /// <summary>
    /// The structured half of a call, whether or not the call reported an error. Its presence is
    /// the promise; a result carrying only prose fails here.
    /// </summary>
    private async Task<JsonElement> CallAsync(
        string tool,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        var result = await _client.CallToolAsync(
            tool,
            arguments ?? _arguments[tool],
            cancellationToken: TestContext.Current.CancellationToken);

        return result.StructuredContent.ShouldNotBeNull(
            $"{tool} answered with prose and no structured content.");
    }
}
