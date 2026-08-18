using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// Field aliases across the protocol seam (ADR-0008): a write naming an alias reaches Jira as the
/// identifier, a read shows both names, and a name nothing recognises fails with the aliases this
/// profile declares.
/// </summary>
public sealed class FieldAliasProtocolTests : IAsyncLifetime
{
    private const string Token = "s3cr3t-personal-access-token";

    private const string Profile = "work";

    private const string MyselfPayload = """
        {
          "key": "JIRAUSER10100",
          "name": "ada",
          "displayName": "Ada Lovelace",
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

        var aliased = await HostProcess.RunAsync(
            ["profile", "alias", "set", Profile, "story_points", "customfield_10010"],
            TestContext.Current.CancellationToken,
            _home.Environment);

        aliased.ExitCode.ShouldBe(0);

        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Json(200, MyselfPayload));

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
                Arguments = HostProcess.ArgumentsFor(
                    "serve", "--profile", Profile, "--allow", "issues:write"),
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
    public async Task A_create_naming_an_alias_reaches_jira_as_the_identifier()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue").UsingPost())
            .RespondWith(Json(201, """{ "id": "10500", "key": "PROJ-42" }"""));

        var result = await _client.CallToolAsync(
            "jira_create_issue",
            new Dictionary<string, object?>
            {
                ["projectKey"] = "PROJ",
                ["issueType"] = "Bug",
                ["summary"] = "It fell over",
                ["fields"] = new Dictionary<string, object?> { ["story_points"] = 5 },
            },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true);

        var sent = SingleRequest("/rest/api/2/issue").Body.ShouldNotBeNull();

        sent.ShouldContain("customfield_10010");
        sent.ShouldNotContain("story_points");
    }

    [Fact]
    public async Task An_update_naming_the_identifier_still_works_because_an_alias_is_not_a_rename()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(204));

        var result = await _client.CallToolAsync(
            "jira_update_issue",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["fields"] = new Dictionary<string, object?> { ["customfield_10010"] = 8 },
            },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true);
        SingleRequest("/rest/api/2/issue/PROJ-42").Body.ShouldNotBeNull()
            .ShouldContain("customfield_10010");
    }

    [Fact]
    public async Task A_read_asks_jira_for_the_identifier_and_shows_the_agent_both_names()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42").UsingGet())
            .RespondWith(Json(200, """
                {
                  "key": "PROJ-42",
                  "fields": { "summary": "It fell over", "customfield_10010": 5 }
                }
                """));

        var result = await _client.CallToolAsync(
            "jira_get_issues",
            new Dictionary<string, object?>
            {
                ["keys"] = new[] { "PROJ-42" },
                ["fields"] = new[] { "story_points" },
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var query = SingleRequest("/rest/api/2/issue/PROJ-42").Query.ShouldNotBeNull();

        query["fields"].ShouldHaveSingleItem().ShouldContain("customfield_10010");

        // Both names, because the identifier is still what every write that is not aliased needs.
        TextOf(result).ShouldContain("story_points (customfield_10010)");
    }

    [Fact]
    public async Task A_field_name_nothing_recognises_fails_with_the_aliases_this_profile_declares()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue").UsingPost())
            .RespondWith(Json(400, """
                {
                  "errorMessages": [],
                  "errors": { "storypoints": "Field 'storypoints' cannot be set." }
                }
                """));

        var result = await _client.CallToolAsync(
            "jira_create_issue",
            new Dictionary<string, object?>
            {
                ["projectKey"] = "PROJ",
                ["issueType"] = "Bug",
                ["summary"] = "It fell over",
                ["fields"] = new Dictionary<string, object?> { ["storypoints"] = 5 },
            },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        var text = TextOf(result);

        // The field catalogue lives in Jira, so the moment an unknown name fails loudly is the
        // moment Jira refuses it — and that is where the names it could have used belong.
        text.ShouldContain("storypoints");
        text.ShouldContain("story_points -> customfield_10010");
    }

    private static string TextOf(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;

    private IRequestMessage SingleRequest(string path) =>
        _jira.LogEntries
            .Select(entry => entry.RequestMessage)
            .OfType<IRequestMessage>()
            .Single(request => request.Path == path);

    private static IResponseBuilder Json(int status, string payload) =>
        Response.Create().WithStatusCode(status)
            .WithHeader("Content-Type", "application/json")
            .WithBody(payload);
}
