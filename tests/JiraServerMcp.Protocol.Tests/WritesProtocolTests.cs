using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// The write surface across the protocol seam: which tools a client is shown under which grant,
/// what the faked Jira received, and what an agent is told when Jira refuses.
/// </summary>
public sealed class WritesProtocolTests : IAsyncLifetime
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

    private const string CreatedPayload = """
        { "id": "10500", "key": "PROJ-42" }
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

        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Json(200, MyselfPayload));

        var loggedIn = await HostProcess.RunAsync(
            ["auth", "login", Profile],
            TestContext.Current.CancellationToken,
            _home.Environment,
            standardInput: Token + "\n");

        loggedIn.ExitCode.ShouldBe(0);

        _jira.Reset();
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
    public async Task Without_a_grant_no_write_tool_is_in_the_list_at_all()
    {
        var tools = await ToolsAsync(await ClientAsync());

        // Not registered rather than refused at call time: a tool an agent cannot see is one it
        // never discovers, attempts, and burns context learning it may not have.
        tools.ShouldNotContain("jira_create_issue");
        tools.ShouldNotContain("jira_update_issue");

        tools.ShouldContain("jira_search");
        tools.ShouldContain("jira_get_issue");
    }

    [Fact]
    public async Task The_issues_grant_registers_the_two_issue_writes_and_nothing_further()
    {
        var tools = await ToolsAsync(await ClientAsync("issues:write"));

        tools.ShouldContain("jira_create_issue");
        tools.ShouldContain("jira_update_issue");

        // Commenting and logging work are their own grants, and their own issue.
        tools.ShouldNotContain("jira_add_comment");
        tools.ShouldNotContain("jira_add_worklog");
    }

    [Fact]
    public async Task Another_grant_does_not_bring_the_issue_writes_with_it()
    {
        var tools = await ToolsAsync(await ClientAsync("comments:write,worklogs:write"));

        tools.ShouldNotContain("jira_create_issue");
        tools.ShouldNotContain("jira_update_issue");
    }

    [Fact]
    public async Task Every_tool_says_honestly_whether_it_reads_and_whether_it_destroys()
    {
        var client = await ClientAsync("issues:write");

        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        foreach (var tool in tools)
        {
            var annotations = tool.ProtocolTool.Annotations.ShouldNotBeNull();

            annotations.ReadOnlyHint.ShouldNotBeNull($"{tool.Name} does not say whether it reads");
            annotations.DestructiveHint.ShouldNotBeNull(
                $"{tool.Name} does not say whether it destroys");
        }

        Annotations(tools, "jira_get_issue").ReadOnlyHint.ShouldBe(true);

        // A create adds an issue and overwrites nothing; an update writes over values that were
        // already there, and a client offering a confirmation prompt should say so.
        Annotations(tools, "jira_create_issue").ReadOnlyHint.ShouldBe(false);
        Annotations(tools, "jira_create_issue").DestructiveHint.ShouldBe(false);
        Annotations(tools, "jira_update_issue").ReadOnlyHint.ShouldBe(false);
        Annotations(tools, "jira_update_issue").DestructiveHint.ShouldBe(true);
    }

    [Fact]
    public async Task Creating_an_issue_returns_its_key_and_nothing_bulky()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue").UsingPost())
            .RespondWith(Json(201, CreatedPayload));

        var text = await CallAsync(
            await ClientAsync("issues:write"),
            "jira_create_issue",
            new Dictionary<string, object?>
            {
                ["projectKey"] = "PROJ",
                ["issueType"] = "Bug",
                ["summary"] = "The login page returns 500",
                ["fields"] = new Dictionary<string, object?>
                {
                    ["description"] = "It has done it twice today.",
                    ["customfield_10010"] = new Dictionary<string, object?> { ["id"] = "10300" },
                },
            });

        text.ShouldContain("PROJ-42");
        text.Length.ShouldBeLessThanOrEqualTo(300);

        var fields = Body(SingleRequest()).GetProperty("fields");

        fields.GetProperty("project").GetProperty("key").GetString().ShouldBe("PROJ");
        fields.GetProperty("issuetype").GetProperty("name").GetString().ShouldBe("Bug");
        fields.GetProperty("summary").GetString().ShouldBe("The login page returns 500");
        fields.GetProperty("description").GetString().ShouldBe("It has done it twice today.");
        fields.GetProperty("customfield_10010").GetProperty("id").GetString().ShouldBe("10300");
    }

    [Fact]
    public async Task A_rejected_create_hands_back_jiras_own_message_for_each_field()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue").UsingPost())
            .RespondWith(Json(400, """
                {
                  "errorMessages": [],
                  "errors": {
                    "customfield_10010": "Team is required.",
                    "duedate": "Date value 'tomorrow' is invalid."
                  }
                }
                """));

        var text = await FailedCallAsync(
            await ClientAsync("issues:write"),
            "jira_create_issue",
            new Dictionary<string, object?>
            {
                ["projectKey"] = "PROJ",
                ["issueType"] = "Bug",
                ["summary"] = "The login page returns 500",
            });

        // Intact, per field: that is what lets an agent correct the create rather than guess.
        text.ShouldContain("customfield_10010");
        text.ShouldContain("Team is required.");
        text.ShouldContain("duedate");
        text.ShouldContain("Date value 'tomorrow' is invalid.");

        // And the one call that says what the project actually requires.
        text.ShouldContain("jira_get_create_fields");
    }

    [Fact]
    public async Task Updating_changes_fields_and_the_assignee_in_a_single_call()
    {
        StubUpdate(204);

        await CallAsync(
            await ClientAsync("issues:write"),
            "jira_update_issue",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["assignee"] = "jbloggs",
                ["fields"] = new Dictionary<string, object?>
                {
                    ["summary"] = "A better summary",
                },
            });

        var request = SingleRequest();

        request.Method.ShouldBe("PUT");
        request.Path.ShouldBe("/rest/api/2/issue/PROJ-42");

        // One request, because reassignment should not cost two operations.
        var fields = Body(request).GetProperty("fields");

        fields.GetProperty("summary").GetString().ShouldBe("A better summary");
        fields.GetProperty("assignee").GetProperty("name").GetString().ShouldBe("jbloggs");
    }

    [Fact]
    public async Task A_field_can_be_cleared_and_that_is_not_the_same_as_leaving_it_alone()
    {
        StubUpdate(204);

        await CallAsync(
            await ClientAsync("issues:write"),
            "jira_update_issue",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["fields"] = new Dictionary<string, object?>
                {
                    ["duedate"] = null,
                },
            });

        var fields = Body(SingleRequest()).GetProperty("fields");

        fields.GetProperty("duedate").ValueKind.ShouldBe(JsonValueKind.Null);
        fields.TryGetProperty("summary", out _).ShouldBeFalse();
        fields.TryGetProperty("assignee", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task An_issue_can_be_unassigned()
    {
        StubUpdate(204);

        await CallAsync(
            await ClientAsync("issues:write"),
            "jira_update_issue",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["assignee"] = string.Empty,
            });

        Body(SingleRequest()).GetProperty("fields").GetProperty("assignee")
            .ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_create_jira_could_not_answer_is_asked_exactly_once()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503));

        await FailedCallAsync(
            await ClientAsync("issues:write"),
            "jira_create_issue",
            new Dictionary<string, object?>
            {
                ["projectKey"] = "PROJ",
                ["issueType"] = "Bug",
                ["summary"] = "The login page returns 500",
            });

        _jira.LogEntries.Count().ShouldBe(1);
    }

    [Fact]
    public async Task An_update_jira_could_not_answer_is_asked_exactly_once()
    {
        StubUpdate(503);

        await FailedCallAsync(
            await ClientAsync("issues:write"),
            "jira_update_issue",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["fields"] = new Dictionary<string, object?> { ["summary"] = "A better summary" },
            });

        _jira.LogEntries.Count().ShouldBe(1);
    }

    [Fact]
    public async Task An_update_naming_nothing_to_change_is_refused_without_asking_jira()
    {
        await FailedCallAsync(
            await ClientAsync("issues:write"),
            "jira_update_issue",
            new Dictionary<string, object?> { ["key"] = "PROJ-42" });

        _jira.LogEntries.Count().ShouldBe(0);
    }

    private static ToolAnnotations Annotations(IList<McpClientTool> tools, string name) =>
        tools.Single(tool => tool.Name == name).ProtocolTool.Annotations.ShouldNotBeNull();

    private static JsonElement Body(IRequestMessage request) =>
        JsonDocument.Parse(request.Body ?? string.Empty).RootElement;

    private static IResponseBuilder Json(int status, string body) =>
        Response.Create().WithStatusCode(status)
            .WithHeader("Content-Type", "application/json")
            .WithBody(body);

    private void StubUpdate(int status) =>
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(status));

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

    private async Task<string> FailedCallAsync(
        McpClient client,
        string tool,
        IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(
            tool,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        return result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;
    }

    /// <summary>
    /// A server launched with the grants named here, exactly as an operator's MCP configuration
    /// would (ADR-0005).
    /// </summary>
    private async Task<McpClient> ClientAsync(params string[] grants)
    {
        string[] allow = [.. grants.SelectMany(grant => (string[])["--allow", grant])];

        var client = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "jira-server-mcp",
                Command = HostProcess.Command,
                Arguments = HostProcess.ArgumentsFor(["serve", "--profile", Profile, .. allow]),
                EnvironmentVariables = _home.Environment.ToDictionary(
                    entry => entry.Key,
                    entry => (string?)entry.Value),
            }),
            cancellationToken: TestContext.Current.CancellationToken);

        _clients.Add(client);

        return client;
    }
}
