using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// Linking across the protocol seam (ADR-0008): which key Jira received on which end of a link,
/// what an agent that guessed a relation phrase is told, and what the link panel's two kinds of
/// link look like when they are read back. Each case pins a decision from ADR-0010 rather than a
/// code path.
/// </summary>
public sealed class LinksProtocolTests : IAsyncLifetime
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
    /// Jira's own three, plus a custom type whose outward wording repeats one of theirs — which is
    /// what a Jira with local link types actually looks like.
    /// </summary>
    private const string LinkTypesPayload = """
        {
          "issueLinkTypes": [
            { "id": "10000", "name": "Blocks", "inward": "is blocked by", "outward": "blocks" },
            { "id": "10001", "name": "Relates", "inward": "relates to", "outward": "relates to" },
            {
              "id": "10002",
              "name": "Duplicate",
              "inward": "is duplicated by",
              "outward": "duplicates"
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
    public async Task A_matched_phrase_links_in_the_direction_the_phrase_reads()
    {
        StubLinkTypes();
        StubLink(201);

        var text = await CallAsync(
            await ClientAsync("links:write"),
            "jira_link_issues",
            new Dictionary<string, object?>
            {
                ["from"] = "PROJ-1",
                ["to"] = "PROJ-2",
                ["relation"] = "  IS BLOCKED BY  ",
            });

        text.ShouldContain("PROJ-1");
        text.ShouldContain("PROJ-2");

        // "PROJ-1 is blocked by PROJ-2" is the same link as "PROJ-2 blocks PROJ-1", and Jira takes
        // only the second form: the phrase, not the argument order, decides which key goes where.
        var body = Body(Requests().Single(request => request.Method is "POST"));

        body.GetProperty("type").GetProperty("name").GetString().ShouldBe("Blocks");
        body.GetProperty("outwardIssue").GetProperty("key").GetString().ShouldBe("PROJ-2");
        body.GetProperty("inwardIssue").GetProperty("key").GetString().ShouldBe("PROJ-1");
    }

    [Fact]
    public async Task A_symmetric_phrase_sends_from_as_the_outward_end()
    {
        StubLinkTypes();
        StubLink(201);

        await CallAsync(
            await ClientAsync("links:write"),
            "jira_link_issues",
            new Dictionary<string, object?>
            {
                ["from"] = "PROJ-1",
                ["to"] = "PROJ-2",
                ["relation"] = "relates to",
            });

        // Relates words both its ends the same way, so there is no direction to get right and no
        // ambiguity to report: one type matched, and 'from' goes out.
        var body = Body(Requests().Single(request => request.Method is "POST"));

        body.GetProperty("type").GetProperty("name").GetString().ShouldBe("Relates");
        body.GetProperty("outwardIssue").GetProperty("key").GetString().ShouldBe("PROJ-1");
        body.GetProperty("inwardIssue").GetProperty("key").GetString().ShouldBe("PROJ-2");
    }

    [Fact]
    public async Task One_phrase_on_two_types_links_nothing_and_names_both()
    {
        // A Jira with a locally added type can publish one wording twice, and the two put
        // different relations on the panel.
        StubLinkTypes(200, """
            {
              "issueLinkTypes": [
                { "id": "10000", "name": "Blocks", "inward": "is blocked by", "outward": "blocks" },
                {
                  "id": "10900",
                  "name": "Release Blocks",
                  "inward": "is release blocked by",
                  "outward": "blocks"
                }
              ]
            }
            """);

        StubLink(201);

        var text = await FailedCallAsync(
            await ClientAsync("links:write"),
            "jira_link_issues",
            new Dictionary<string, object?>
            {
                ["from"] = "PROJ-1",
                ["to"] = "PROJ-2",
                ["relation"] = "blocks",
            });

        text.ShouldContain("Blocks");
        text.ShouldContain("Release Blocks");

        Requests().Count(request => request.Method is "POST").ShouldBe(0);
    }

    [Fact]
    public async Task A_phrase_this_jira_does_not_publish_comes_back_with_the_ones_it_does()
    {
        StubLinkTypes();

        var text = await FailedCallAsync(
            await ClientAsync("links:write"),
            "jira_link_issues",
            new Dictionary<string, object?>
            {
                ["from"] = "PROJ-1",
                ["to"] = "PROJ-2",
                ["relation"] = "depends on",
            });

        // The single most useful thing to tell an agent that guessed: both wordings of every type.
        text.ShouldContain("depends on");
        text.ShouldContain("blocks");
        text.ShouldContain("is blocked by");
        text.ShouldContain("is duplicated by");

        Requests().Count(request => request.Method is "POST").ShouldBe(0);
    }

    [Fact]
    public async Task A_404_names_both_keys_and_says_nothing_was_linked()
    {
        StubLinkTypes();
        StubLink(404, """{ "errorMessages": ["Issue Does Not Exist"], "errors": {} }""");

        var text = await FailedCallAsync(
            await ClientAsync("links:write"),
            "jira_link_issues",
            new Dictionary<string, object?>
            {
                ["from"] = "PROJ-1",
                ["to"] = "PROJ-404",
                ["relation"] = "blocks",
            });

        // Jira answers a key that does not exist and a key this account cannot see the same way,
        // and does not reliably say which of the two it meant.
        text.ShouldContain("PROJ-1");
        text.ShouldContain("PROJ-404");
        text.ShouldContain("Nothing was linked");
    }

    [Fact]
    public async Task A_comment_rides_along_with_the_link()
    {
        StubLinkTypes();
        StubLink(201);

        await CallAsync(
            await ClientAsync("links:write"),
            "jira_link_issues",
            new Dictionary<string, object?>
            {
                ["from"] = "PROJ-1",
                ["to"] = "PROJ-2",
                ["relation"] = "blocks",
                ["comment"] = "The migration has to land first.",
            });

        // One request, not a link followed by a comment that may fail on its own.
        Body(Requests().Single(request => request.Method is "POST"))
            .GetProperty("comment").GetProperty("body").GetString()
            .ShouldBe("The migration has to land first.");
    }

    [Fact]
    public async Task A_first_remote_link_is_reported_as_attached()
    {
        StubRemoteLinkWrite(201);

        var text = await CallAsync(
            await ClientAsync("links:write"),
            "jira_add_remote_link",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["url"] = "https://github.com/acme/web/pull/128",
                ["title"] = "PR #128: retry on 429",
                ["relationship"] = "pull request",
            });

        text.ShouldContain("https://github.com/acme/web/pull/128");
        text.ShouldNotContain("already attached");

        var body = Body(Requests().ShouldHaveSingleItem());

        // The URL is the identity, bare: a namespaced identifier would attach a second copy of a
        // pull request another integration already attached.
        body.GetProperty("globalId").GetString().ShouldBe("https://github.com/acme/web/pull/128");
        body.GetProperty("object").GetProperty("url").GetString()
            .ShouldBe("https://github.com/acme/web/pull/128");
        body.GetProperty("object").GetProperty("title").GetString().ShouldBe("PR #128: retry on 429");
        body.GetProperty("relationship").GetString().ShouldBe("pull request");
    }

    [Fact]
    public async Task The_same_url_attached_twice_is_reported_as_one_link_updated()
    {
        StubRemoteLinkWrite(200);

        var text = await CallAsync(
            await ClientAsync("links:write"),
            "jira_add_remote_link",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["url"] = "https://github.com/acme/web/pull/128",
                ["title"] = "PR #128: retry on 429, now merged",
            });

        // An agent told the link was already there learns that its earlier call landed, which is
        // the whole value of keying the link by its URL.
        text.ShouldContain("already attached");
        text.ShouldContain("one link, not two");
    }

    [Fact]
    public async Task Remote_links_render_beside_the_issue_links_and_a_refusal_costs_only_them()
    {
        StubIssue();

        _jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-12/remotelink").UsingGet())
            .RespondWith(Json(200, """
                [
                  {
                    "id": 10100,
                    "globalId": "https://github.com/acme/web/pull/128",
                    "relationship": "pull request",
                    "object": {
                      "url": "https://github.com/acme/web/pull/128",
                      "title": "PR #128: retry on 429"
                    }
                  }
                ]
                """));

        var readable = await CallAsync(
            await ClientAsync(),
            "jira_get_issues",
            new Dictionary<string, object?>
            {
                ["keys"] = new[] { "PROJ-12" },
                ["include"] = new[] { "links" },
            });

        readable.ShouldContain("is blocked by PROJ-13");
        readable.ShouldContain("pull request");
        readable.ShouldContain("https://github.com/acme/web/pull/128");

        _jira.Reset();
        StubIssue();

        _jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-12/remotelink").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(403));

        var refused = await CallAsync(
            await ClientAsync(),
            "jira_get_issues",
            new Dictionary<string, object?>
            {
                ["keys"] = new[] { "PROJ-12" },
                ["include"] = new[] { "links" },
            });

        // An opt-in sub-section this account may not read costs that section and nothing else.
        refused.ShouldContain("Login fails with a 401");
        refused.ShouldContain("is blocked by PROJ-13");
        refused.ShouldContain("may not read them");
    }

    [Fact]
    public async Task Neither_link_tool_exists_without_the_links_write_grant()
    {
        var tools = await (await ClientAsync("issues:write")).ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        tools.Select(tool => tool.Name).ShouldNotContain("jira_link_issues");
        tools.Select(tool => tool.Name).ShouldNotContain("jira_add_remote_link");
    }

    private void StubIssue() =>
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-12").UsingGet())
            .RespondWith(Json(200, """
                {
                  "key": "PROJ-12",
                  "fields": {
                    "summary": "Login fails with a 401",
                    "issuelinks": [
                      {
                        "type": {
                          "name": "Blocks",
                          "inward": "is blocked by",
                          "outward": "blocks"
                        },
                        "inwardIssue": {
                          "key": "PROJ-13",
                          "fields": { "summary": "Token refresh drops the session" }
                        }
                      }
                    ]
                  }
                }
                """));

    private void StubLinkTypes() => StubLinkTypes(200, LinkTypesPayload);

    private void StubLinkTypes(int status, string payload) =>
        _jira.Given(Request.Create().WithPath("/rest/api/2/issueLinkType").UsingGet())
            .RespondWith(Json(status, payload));

    private void StubLink(int status, string payload = "") =>
        _jira.Given(Request.Create().WithPath("/rest/api/2/issueLink").UsingPost())
            .RespondWith(Json(status, payload));

    private void StubRemoteLinkWrite(int status) =>
        _jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42/remotelink").UsingPost())
            .RespondWith(Json(status, """{ "id": 10100, "self": "http://jira/rest/api/2/issue/PROJ-42/remotelink/10100" }"""));

    private static JsonElement Body(IRequestMessage request) =>
        JsonDocument.Parse(request.Body ?? string.Empty).RootElement;

    private static IResponseBuilder Json(int status, string body) =>
        Response.Create().WithStatusCode(status)
            .WithHeader("Content-Type", "application/json")
            .WithBody(body);

    private IReadOnlyList<IRequestMessage> Requests() =>
    [
        .. _jira.LogEntries.Select(entry => entry.RequestMessage).OfType<IRequestMessage>(),
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
