using System.Text.Json;
using ModelContextProtocol.Client;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// The structured half as a real MCP client receives it (ADR-0008, ADR-0009). The promise the
/// README makes is that structure is present on every result, success and failure alike, and that
/// every tool declares the schema for it — so this asserts it of every registered tool rather than
/// of a sample, which under ADR-0008 makes it a fact test rather than a taste test.
/// </summary>
public sealed class StructuredContentProtocolTests : IAsyncLifetime
{
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
            ["jira_changed_since"] = new() { ["since"] = "2026-08-18T09:00:00+02:00" },
            ["jira_get_issues"] = new() { ["keys"] = new[] { "PROJ-12" } },
            ["jira_get_attachment"] = new() { ["attachmentId"] = "10100" },
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

    private ProtocolSeam _seam = null!;

    private McpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _seam = await ProtocolSeam.StartAsync();

        // Every tool is registered, licence and grants included, so the sweeps below see the whole
        // surface rather than the read-only part of it. That takes a capability probe recording a
        // Jira Software licence, which is what these two stubs are.
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/serverInfo").UsingGet())
            .RespondWith(JiraResponse.Json(200,
                """{ "version": "8.20.7", "deploymentType": "Server" }"""));

        _seam.Jira.Given(Request.Create().WithPath("/rest/agile/1.0/board").UsingGet())
            .RespondWith(JiraResponse.Json(200,
                """{ "startAt": 0, "maxResults": 1, "isLast": true, "values": [] }"""));

        await _seam.RunAsync(["profile", "refresh", ProtocolSeam.Profile]);

        _seam.Jira.Reset();

        _client = await _seam.ConnectAsync(
            "issues:write,comments:write,worklogs:write,links:write");
    }

    public async ValueTask DisposeAsync() => await _seam.DisposeAsync();

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
        _seam.Jira.Given(Request.Create().WithPath("/*").UsingAnyMethod())
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
        _seam.Jira.Stop();

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
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
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
                            "assignee": { "name": "ada", "displayName": "Ada Lovelace" }
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
        row.GetProperty("assignee").GetString().ShouldBe("ada");

        // The summary is prose. It is in the text half, where the delimiters are, and nowhere else.
        row.TryGetProperty("summary", out _).ShouldBeFalse();
        structure.GetRawText().ShouldNotContain("Login fails");
    }

    [Fact]
    public async Task A_write_carries_the_identifiers_it_created()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue").UsingPost())
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
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-12").UsingGet())
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

        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-99").UsingGet())
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

    [Fact]
    public async Task The_create_screen_carries_the_field_ids_a_create_must_send()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/createmeta").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    {
                      "projects": [
                        {
                          "key": "PROJ",
                          "issuetypes": [
                            {
                              "name": "Bug",
                              "fields": {
                                "summary": {
                                  "name": "Summary",
                                  "required": true,
                                  "schema": { "type": "string" }
                                },
                                "customfield_10010": {
                                  "name": "Severity",
                                  "required": true,
                                  "schema": { "type": "option" },
                                  "allowedValues": [
                                    { "id": "10001", "value": "Blocker" },
                                    { "id": "10002", "value": "Major" }
                                  ]
                                }
                              }
                            }
                          ]
                        }
                      ]
                    }
                    """));

        var structure = await CallAsync("jira_get_create_fields");

        structure.GetProperty("outcome").GetString().ShouldBe("ok");
        structure.GetProperty("projectKey").GetString().ShouldBe("PROJ");
        structure.GetProperty("issueTypeName").GetString().ShouldBe("Bug");

        var severity = structure.GetProperty("fields").EnumerateArray()
            .Single(field => field.GetProperty("id").GetString() is "customfield_10010");

        // The identifier is what a create must send, and the name is what makes it actionable.
        severity.GetProperty("name").GetString().ShouldBe("Severity");
        severity.GetProperty("required").GetBoolean().ShouldBeTrue();
        severity.GetProperty("type").GetString().ShouldBe("option");
        severity.GetProperty("hasAllowedValues").GetBoolean().ShouldBeTrue();

        severity.GetProperty("allowedValues").EnumerateArray()
            .Select(value => value.GetString())
            .ShouldBe(["Blocker", "Major"]);
    }

    [Fact]
    public async Task A_project_listing_carries_the_keys_and_a_project_carries_its_names()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/project").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    [
                      { "key": "PROJ", "name": "Platform", "id": "10100", "projectTypeKey": "software" }
                    ]
                    """));

        var listing = await CallAsync("jira_list_projects");

        listing.GetProperty("outcome").GetString().ShouldBe("ok");
        listing.GetProperty("cutByCap").GetBoolean().ShouldBeFalse();

        var row = listing.GetProperty("projects").EnumerateArray().ShouldHaveSingleItem();

        row.GetProperty("key").GetString().ShouldBe("PROJ");
        row.GetProperty("name").GetString().ShouldBe("Platform");

        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/project/PROJ").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    {
                      "key": "PROJ",
                      "name": "Platform",
                      "id": "10100",
                      "lead": { "name": "ada", "displayName": "Ada Lovelace" },
                      "description": "Prose, which the structured half does not carry."
                    }
                    """));

        // A project read is four calls: the project, its issue types' statuses, its components,
        // and its versions.
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/project/PROJ/components").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""[{ "id": "100", "name": "api" }]"""));

        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/project/PROJ/versions").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    [{ "id": "200", "name": "1.4.0", "released": true, "archived": false }]
                    """));

        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/project/PROJ/statuses").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    [{ "id": "1", "name": "Bug", "subtask": false, "statuses": [] }]
                    """));

        var project = await CallAsync("jira_get_project");

        project.GetProperty("key").GetString().ShouldBe("PROJ");
        project.GetProperty("versionNames").EnumerateArray().ShouldHaveSingleItem()
            .GetString().ShouldBe("1.4.0");
        project.GetProperty("componentNames").EnumerateArray().ShouldHaveSingleItem()
            .GetString().ShouldBe("api");
        project.GetProperty("issueTypeNames").EnumerateArray().ShouldHaveSingleItem()
            .GetString().ShouldBe("Bug");

        // The description is prose and the lead is a field nothing branches on.
        project.GetRawText().ShouldNotContain("Prose, which");
        project.TryGetProperty("lead", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_board_listing_and_a_sprint_listing_carry_their_rows_and_no_total()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/agile/1.0/board").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    {
                      "startAt": 0,
                      "maxResults": 25,
                      "isLast": true,
                      "values": [{ "id": 42, "name": "PROJ board", "type": "scrum" }]
                    }
                    """));

        var boards = await CallAsync("jira_list_boards");

        boards.GetProperty("outcome").GetString().ShouldBe("ok");

        var board = boards.GetProperty("boards").EnumerateArray().ShouldHaveSingleItem();

        board.GetProperty("id").GetInt32().ShouldBe(42);
        board.GetProperty("name").GetString().ShouldBe("PROJ board");

        // The software API never says how many rows exist, so no field claims to.
        boards.TryGetProperty("total", out _).ShouldBeFalse();
        boards.TryGetProperty("nextStartAt", out _).ShouldBeFalse();

        _seam.Jira.Given(Request.Create().WithPath("/rest/agile/1.0/board/1/sprint").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    {
                      "startAt": 0,
                      "maxResults": 25,
                      "isLast": false,
                      "values": [
                        {
                          "id": 118,
                          "name": "Sprint 14",
                          "state": "active",
                          "startDate": "2026-08-01T09:00:00.000+02:00"
                        }
                      ]
                    }
                    """));

        var sprints = await CallAsync("jira_list_sprints");

        var sprint = sprints.GetProperty("sprints").EnumerateArray().ShouldHaveSingleItem();

        sprint.GetProperty("state").GetString().ShouldBe("active");
        sprint.TryGetProperty("startDate", out _).ShouldBeFalse();

        // Not the last page, so there is somewhere to resume — past the page, not at its rows.
        sprints.GetProperty("nextStartAt").GetInt32().ShouldBe(25);
    }

    [Fact]
    public async Task A_user_search_and_the_account_carry_usernames_and_no_personal_data()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/user/search").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    [
                      {
                        "name": "ada",
                        "displayName": "Ada Lovelace",
                        "emailAddress": "ada@example.invalid",
                        "active": true
                      }
                    ]
                    """));

        var users = await CallAsync("jira_search_users");

        users.GetProperty("outcome").GetString().ShouldBe("ok");
        users.GetProperty("includeInactive").GetBoolean().ShouldBeFalse();

        var user = users.GetProperty("users").EnumerateArray().ShouldHaveSingleItem();

        user.GetProperty("username").GetString().ShouldBe("ada");
        user.GetProperty("active").GetBoolean().ShouldBeTrue();

        // Neither the display name nor the email leaves the delimited region.
        users.GetRawText().ShouldNotContain("Ada Lovelace");
        users.GetRawText().ShouldNotContain("example.invalid");

        // Jira's user search reports no total, so nothing here claims one.
        users.TryGetProperty("total", out _).ShouldBeFalse();

        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JiraAccount.Payload()));

        var account = await CallAsync("jira_whoami");

        account.GetProperty("username").GetString().ShouldBe("ada");
        account.GetProperty("active").GetBoolean().ShouldBeTrue();
        account.GetRawText().ShouldNotContain("Lovelace");
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
