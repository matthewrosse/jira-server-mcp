using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// Linking across the protocol seam (ADR-0008): which key Jira received on which end of a link,
/// what an agent that guessed a relation phrase is told, and what the link panel's two kinds of
/// link look like when they are read back. Each case pins a decision from ADR-0010 rather than a
/// code path.
/// </summary>
public sealed class LinksProtocolTests : IAsyncLifetime
{
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

    /// <summary>One pull request, attached twice: the arguments are the same both times.</summary>
    private static readonly Dictionary<string, object?> _remoteLinkArguments = new()
    {
        ["key"] = "PROJ-42",
        ["url"] = "https://github.com/acme/web/pull/128",
        ["title"] = "PR #128: retry on 429",
        ["relationship"] = "pull request",
    };

    private ProtocolSeam _seam = null!;

    public async ValueTask InitializeAsync() => _seam = await ProtocolSeam.StartAsync();

    public async ValueTask DisposeAsync() => await _seam.DisposeAsync();

    [Fact]
    public async Task A_matched_phrase_links_in_the_direction_the_phrase_reads()
    {
        StubLinkTypes();
        StubLink(201);

        var text = await CallAsync(
            await _seam.ConnectAsync("links:write"),
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
            await _seam.ConnectAsync("links:write"),
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
            await _seam.ConnectAsync("links:write"),
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
            await _seam.ConnectAsync("links:write"),
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
    public async Task A_blank_relation_is_refused_before_anything_is_sent()
    {
        StubLinkTypes();
        StubLink(201);

        var text = await FailedCallAsync(
            await _seam.ConnectAsync("links:write"),
            "jira_link_issues",
            new Dictionary<string, object?>
            {
                ["from"] = "PROJ-1",
                ["to"] = "PROJ-2",
                ["relation"] = "   ",
            });

        // A blank phrase names no direction, and Jira publishes types whose payload may omit one
        // wording — matching on that would link two issues under a type nobody asked for.
        text.ShouldContain("PROJ-1");
        Requests().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_404_names_both_keys_and_says_nothing_was_linked()
    {
        StubLinkTypes();
        StubLink(404, """{ "errorMessages": ["Issue Does Not Exist"], "errors": {} }""");

        var text = await FailedCallAsync(
            await _seam.ConnectAsync("links:write"),
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
            await _seam.ConnectAsync("links:write"),
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
            await _seam.ConnectAsync("links:write"),
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
            await _seam.ConnectAsync("links:write"),
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

        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-12/remotelink").UsingGet())
            .RespondWith(JiraResponse.Json(200, """
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
            await _seam.ConnectAsync(),
            "jira_get_issues",
            new Dictionary<string, object?>
            {
                ["keys"] = new[] { "PROJ-12" },
                ["include"] = new[] { "links" },
            });

        readable.ShouldContain("is blocked by PROJ-13");
        readable.ShouldContain("pull request");
        readable.ShouldContain("https://github.com/acme/web/pull/128");

        _seam.Jira.Reset();
        StubIssue();

        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-12/remotelink").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(403));

        var refused = await CallAsync(
            await _seam.ConnectAsync(),
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
    public async Task An_instance_with_linking_switched_off_still_reads_the_issue()
    {
        StubIssue();

        // Jira answers this endpoint with a 404 both where the issue is invisible and where issue
        // linking is disabled instance-wide — and the issue itself just read fine.
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-12/remotelink").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        var text = await CallAsync(
            await _seam.ConnectAsync(),
            "jira_get_issues",
            new Dictionary<string, object?>
            {
                ["keys"] = new[] { "PROJ-12" },
                ["include"] = new[] { "links" },
            });

        text.ShouldContain("Login fails with a 401");
        text.ShouldContain("is blocked by PROJ-13");
        text.ShouldContain("may not read them");
    }

    [Fact]
    public async Task A_link_carries_both_ends_the_phrase_and_the_type_jira_stored_it_under()
    {
        StubLinkTypes();
        StubLink(201);

        var structure = await StructureAsync(
            await _seam.ConnectAsync("links:write"),
            "jira_link_issues",
            new Dictionary<string, object?>
            {
                ["from"] = "PROJ-1",
                ["to"] = "PROJ-2",
                ["relation"] = "  IS BLOCKED BY  ",
            });

        // Exact equality rather than spot-checks, because rule 1 of ADR-0009 promises a contract
        // and only the whole document catches a field that quietly changed its name or its place.
        // The keys are the caller's, unswapped: the phrase decided the direction (ADR-0010), and
        // reporting Jira's own slots would hand back a sentence nobody wrote. The phrase is
        // trimmed but not recased — it is what a repeat call would send — and the type name beside
        // it is what Jira stored, which is a different string.
        structure.GetRawText().ShouldBe(
            """
            {"outcome":"ok","from":"PROJ-1","to":"PROJ-2","relation":"IS BLOCKED BY","typeName":"Blocks"}
            """);
    }

    [Fact]
    public async Task A_phrase_this_jira_does_not_publish_is_a_plain_refusal()
    {
        StubLinkTypes();

        var structure = await StructureAsync(
            await _seam.ConnectAsync("links:write"),
            "jira_link_issues",
            new Dictionary<string, object?>
            {
                ["from"] = "PROJ-1",
                ["to"] = "PROJ-2",
                ["relation"] = "depends on",
            },
            failed: true);

        // No branchable field beyond the outcome, and deliberately: jira_transition_issue answers
        // an unmatched or ambiguous transition name exactly this way, and the phrases this Jira
        // does publish are Jira-authored text, which belongs in the delimited region of the prose
        // and nowhere else.
        structure.GetRawText().ShouldBe("""{"outcome":"refused"}""");
    }

    [Fact]
    public async Task A_link_jira_refused_carries_the_status_it_answered_with()
    {
        StubLinkTypes();
        StubLink(403, """{ "errorMessages": ["You do not have permission"], "errors": {} }""");

        var structure = await StructureAsync(
            await _seam.ConnectAsync("links:write"),
            "jira_link_issues",
            new Dictionary<string, object?>
            {
                ["from"] = "PROJ-1",
                ["to"] = "PROJ-2",
                ["relation"] = "blocks",
            },
            failed: true);

        structure.GetRawText().ShouldBe("""{"outcome":"jira_api","statusCode":403}""");
    }

    [Fact]
    public async Task A_first_attach_and_a_repeat_attach_differ_in_the_structured_half()
    {
        StubRemoteLinkWrite(201);

        var first = await StructureAsync(
            await _seam.ConnectAsync("links:write"),
            "jira_add_remote_link",
            _remoteLinkArguments);

        first.GetRawText().ShouldBe(
            """
            {"outcome":"ok","key":"PROJ-42","url":"https://github.com/acme/web/pull/128","created":true}
            """);

        _seam.Jira.Reset();
        StubRemoteLinkWrite(200);

        var repeat = await StructureAsync(
            await _seam.ConnectAsync("links:write"),
            "jira_add_remote_link",
            _remoteLinkArguments);

        // The two answers differ in a field rather than only in a sentence, which is the loop
        // ADR-0009 exists to close: the caller reads created and never parses the prose to find
        // out whether an earlier call of its own already landed.
        repeat.GetRawText().ShouldBe(
            """
            {"outcome":"ok","key":"PROJ-42","url":"https://github.com/acme/web/pull/128","created":false}
            """);

        // The title is what a human typed into the link panel, so it stays in the prose half.
        repeat.GetRawText().ShouldNotContain("retry on 429");
    }

    [Fact]
    public async Task An_attach_jira_refused_carries_the_status_and_no_created_field()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42/remotelink").UsingPost())
            .RespondWith(JiraResponse.Json(403, """{ "errorMessages": ["You do not have permission"], "errors": {} }"""));

        var structure = await StructureAsync(
            await _seam.ConnectAsync("links:write"),
            "jira_add_remote_link",
            _remoteLinkArguments,
            failed: true);

        // A failure carries the outcome and nothing else — created would be a claim about a write
        // that was never made.
        structure.GetRawText().ShouldBe("""{"outcome":"jira_api","statusCode":403}""");
    }

    [Fact]
    public async Task Neither_link_tool_exists_without_the_links_write_grant()
    {
        var tools = await (await _seam.ConnectAsync("issues:write")).ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        tools.Select(tool => tool.Name).ShouldNotContain("jira_link_issues");
        tools.Select(tool => tool.Name).ShouldNotContain("jira_add_remote_link");
    }

    private void StubIssue() =>
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-12").UsingGet())
            .RespondWith(JiraResponse.Json(200, """
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
                        "outwardIssue": {
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
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issueLinkType").UsingGet())
            .RespondWith(JiraResponse.Json(status, payload));

    private void StubLink(int status, string payload = "") =>
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issueLink").UsingPost())
            .RespondWith(JiraResponse.Json(status, payload));

    private void StubRemoteLinkWrite(int status) =>
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42/remotelink").UsingPost())
            .RespondWith(JiraResponse.Json(status, """{ "id": 10100, "self": "http://jira/rest/api/2/issue/PROJ-42/remotelink/10100" }"""));

    private static JsonElement Body(IRequestMessage request) =>
        JsonDocument.Parse(request.Body ?? string.Empty).RootElement;

    private IReadOnlyList<IRequestMessage> Requests() =>
    [
        .. _seam.Jira.LogEntries.Select(entry => entry.RequestMessage).OfType<IRequestMessage>(),
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
    /// The structured half of a call, whether or not the call reported an error. Rule 3 of
    /// ADR-0009 puts it on every result, so a result carrying only prose fails here.
    /// </summary>
    private async Task<JsonElement> StructureAsync(
        McpClient client,
        string tool,
        IReadOnlyDictionary<string, object?> arguments,
        bool failed = false)
    {
        var result = await client.CallToolAsync(
            tool,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken);

        if (failed)
        {
            result.IsError.ShouldBe(true);
        }
        else
        {
            result.IsError.ShouldNotBe(true);
        }

        return result.StructuredContent.ShouldNotBeNull(
            $"{tool} answered with prose and no structured content.");
    }
}
