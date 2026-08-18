using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// The workflow prompt across the protocol seam (ADR-0008): whether a real client sees
/// <c>implement_issue</c> at all under a given grant set, and what <c>prompts/get</c> hands back.
/// The message is asserted by structure and by reference — the tools it names, and how the two
/// argument branches differ — never by snapshot, which would only be a second copy of the prompt.
/// </summary>
public sealed class ImplementIssuePromptProtocolTests : IAsyncLifetime
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

    private readonly List<McpClient> _clients = [];

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
    public async Task A_client_granted_every_tool_the_procedure_calls_sees_the_prompt()
    {
        var client = await ClientAsync("issues:write", "comments:write");

        client.ServerCapabilities.Prompts.ShouldNotBeNull();

        var prompts = await client.ListPromptsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var prompt = prompts.ShouldHaveSingleItem();

        prompt.Name.ShouldBe("implement_issue");

        // One optional argument. A caller that names no issue still gets a usable procedure.
        var argument = prompt.ProtocolPrompt.Arguments.ShouldNotBeNull().ShouldHaveSingleItem();

        argument.Name.ShouldBe("key");
        argument.Required.ShouldNotBe(true);
    }

    [Theory]
    [InlineData]
    [InlineData("issues:write")]
    [InlineData("comments:write")]
    [InlineData("comments:write", "worklogs:write")]
    public async Task A_client_short_of_those_tools_sees_no_prompt_at_all(params string[] grants)
    {
        // A procedure telling an agent to transition an issue, handed to a client with no
        // transition tool, would read as an instruction to do something impossible. With nothing
        // registered the server advertises no prompts capability at all, so such a client does not
        // see an empty list — it sees no prompt surface, and prompts/list is not even available.
        var client = await ClientAsync(grants);

        client.ServerCapabilities.Prompts.ShouldBeNull();

        await Should.ThrowAsync<McpProtocolException>(
            () => client.ListPromptsAsync(
                cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task The_prompt_is_one_user_message_naming_the_tools_it_depends_on()
    {
        var text = await GetAsync(null);

        // MCP prompt messages carry only user and assistant roles, and a synthetic assistant turn
        // would put words in the model's mouth that it never said.
        text.ShouldContain("jira_get_issues");
        text.ShouldContain("jira_transition_issue");
        text.ShouldContain("jira_add_comment");

        // Never a status: the vocabulary is per-team, and this server does not know it.
        text.ShouldNotContain("In Progress");
        text.ShouldContain("transitions");
    }

    [Fact]
    public async Task Naming_an_issue_starts_there_and_naming_none_starts_from_the_callers_own()
    {
        var named = await GetAsync("PROJ-42");
        var unnamed = await GetAsync(null);

        named.ShouldContain("PROJ-42");
        named.ShouldNotContain("jira_my_open_issues");

        // Without a key the procedure has to say where to find one.
        unnamed.ShouldContain("jira_my_open_issues");
        unnamed.ShouldNotContain("PROJ-42");
    }

    [Fact]
    public async Task Fetching_the_prompt_asks_jira_for_nothing()
    {
        await GetAsync("PROJ-42");

        // Static text: no fetch to go stale, no failure at prompt-fetch time, and not a character
        // of Jira-authored content in the message.
        _jira.LogEntries.ShouldBeEmpty();
    }

    /// <summary>
    /// The prompt's single message, as a client receives it.
    /// </summary>
    private async Task<string> GetAsync(string? key)
    {
        var client = await ClientAsync("issues:write", "comments:write");

        var result = await client.GetPromptAsync(
            "implement_issue",
            key is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?> { ["key"] = key },
            cancellationToken: TestContext.Current.CancellationToken);

        var message = result.Messages.ShouldHaveSingleItem();

        message.Role.ShouldBe(Role.User);

        return message.Content.ShouldBeOfType<TextContentBlock>().Text;
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
