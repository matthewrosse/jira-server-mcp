using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// A real MCP client, over stdio, against a real host process, with WireMock.Net standing in for
/// Jira. Both directions of the seam are asserted: what the client receives, and what Jira was
/// asked.
/// </summary>
public sealed class WhoamiProtocolTests : IAsyncLifetime
{
    private const string Token = "s3cr3t-personal-access-token";

    private const string Profile = "work";

    private const string MyselfPayload = """
        {
          "self": "http://localhost/rest/api/2/user?username=mrosse",
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
        // The server is configured the way a user configures it: a registered profile and a
        // stored credential, with no environment variable in sight.
        await RegisterProfileAsync();

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

    private async Task RegisterProfileAsync()
    {
        var added = await HostProcess.RunAsync(
            ["profile", "add", Profile, "--url", _jira.Url!],
            TestContext.Current.CancellationToken,
            _home.Environment);

        added.ExitCode.ShouldBe(0);

        // `auth login` validates the token before storing it, so Jira has to answer for the
        // login itself. The stub and the request it logged are then cleared, leaving each test
        // the empty slate it asserts against.
        StubMyself(Response.Create().WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(MyselfPayload));

        var loggedIn = await HostProcess.RunAsync(
            ["auth", "login", Profile],
            TestContext.Current.CancellationToken,
            _home.Environment,
            standardInput: Token + "\n");

        loggedIn.ExitCode.ShouldBe(0);

        _jira.Reset();
    }

    [Fact]
    public async Task The_client_sees_jira_whoami_in_the_tool_list()
    {
        var tools = await _client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var whoami = tools.Single(tool => tool.Name is "jira_whoami");

        whoami.ProtocolTool.Annotations.ShouldNotBeNull().ReadOnlyHint.ShouldBe(true);
        whoami.JsonSchema.GetProperty("properties").EnumerateObject().ShouldBeEmpty();
    }

    [Fact]
    public async Task Calling_it_returns_the_identity_the_token_belongs_to()
    {
        StubMyself(Response.Create().WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(MyselfPayload));

        var result = await CallWhoamiAsync(TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true);

        var text = result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;

        text.ShouldContain("Mateusz Różański");
        text.ShouldContain("mrosse");
        text.ShouldContain("mrosse@example.com");
        text.ShouldContain("active");
        text.ShouldContain("<jira-data");
    }

    [Fact]
    public async Task The_outgoing_request_carries_the_bearer_token_to_the_myself_endpoint()
    {
        StubMyself(Response.Create().WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(MyselfPayload));

        await CallWhoamiAsync(TestContext.Current.CancellationToken);

        var request = _jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull()
            .RequestMessage.ShouldNotBeNull();

        request.Method.ShouldBe("GET");
        request.Path.ShouldBe("/rest/api/2/myself");
        request.Headers.ShouldNotBeNull()["Authorization"]
            .ShouldHaveSingleItem()
            .ShouldBe("Bearer " + Token);
    }

    [Fact]
    public async Task Cancelling_the_tool_call_does_not_wait_for_jira()
    {
        StubMyself(Response.Create().WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(MyselfPayload)
            .WithDelay(TimeSpan.FromSeconds(10)));

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Should.ThrowAsync<OperationCanceledException>(
            () => CallWhoamiAsync(cancellation.Token).AsTask());
    }

    [Fact]
    public async Task A_rejected_token_is_reported_as_a_rejected_token()
    {
        StubMyself(Response.Create().WithStatusCode(401)
            .WithHeader("Content-Type", "application/json")
            .WithBody("""{"errorMessages":["You do not have permission."],"errors":{}}"""));

        var result = await CallWhoamiAsync(TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        var text = result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;

        text.ShouldContain("401");
        text.ShouldContain("personal access token");
    }

    [Fact]
    public async Task A_wrong_base_url_is_reported_differently_from_a_rejected_token()
    {
        // No stub, so WireMock answers 404 the way a Jira behind a context path would.
        var result = await CallWhoamiAsync(TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        var text = result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;

        text.ShouldContain("404");
        text.ShouldContain("base URL");
        text.ShouldNotContain("personal access token");
    }

    [Fact]
    public async Task An_unreachable_jira_is_reported_as_unreachable()
    {
        _jira.Stop();

        var result = await CallWhoamiAsync(TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem()
            .Text.ShouldContain("Could not reach Jira");
    }

    private ValueTask<CallToolResult> CallWhoamiAsync(CancellationToken cancellationToken) =>
        _client.CallToolAsync("jira_whoami", cancellationToken: cancellationToken);

    private void StubMyself(IResponseBuilder response) =>
        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet()).RespondWith(response);
}
