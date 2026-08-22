using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// Field aliases across the protocol seam (ADR-0008): a write naming an alias reaches Jira as the
/// identifier, a read shows both names, and a name nothing recognises fails with the aliases this
/// profile declares.
/// </summary>
public sealed class FieldAliasProtocolTests : IAsyncLifetime
{
    private ProtocolSeam _seam = null!;

    private McpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _seam = await ProtocolSeam.StartAsync();

        await _seam.RunAsync(
            ["profile", "alias", "set", ProtocolSeam.Profile, "story_points", "customfield_10010"]);

        _client = await _seam.ConnectAsync("issues:write");
    }

    public async ValueTask DisposeAsync() => await _seam.DisposeAsync();

    [Fact]
    public async Task A_create_naming_an_alias_reaches_jira_as_the_identifier()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue").UsingPost())
            .RespondWith(JiraResponse.Json(201, """{ "id": "10500", "key": "PROJ-42" }"""));

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
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42").UsingPut())
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
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42").UsingGet())
            .RespondWith(JiraResponse.Json(200, """
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
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue").UsingPost())
            .RespondWith(JiraResponse.Json(400, """
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

    [Fact]
    public async Task A_search_row_labels_an_aliased_field_as_an_issue_read_does()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
            .RespondWith(JiraResponse.Json(200, """
                {
                  "startAt": 0,
                  "maxResults": 25,
                  "total": 1,
                  "issues": [
                    { "key": "PROJ-42", "fields": { "customfield_10010": 5 } }
                  ]
                }
                """));

        var result = await _client.CallToolAsync(
            "jira_search",
            new Dictionary<string, object?>
            {
                ["jql"] = "project = PROJ",
                ["fields"] = new[] { "story_points" },
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // A caller that asked for story_points must be able to find story_points in the answer.
        TextOf(result).ShouldContain("story_points (customfield_10010)");
    }

    [Fact]
    public async Task One_field_named_by_both_of_its_names_is_refused_before_anything_is_sent()
    {
        var result = await _client.CallToolAsync(
            "jira_update_issue",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["fields"] = new Dictionary<string, object?>
                {
                    ["story_points"] = 5,
                    ["customfield_10010"] = 8,
                },
            },
            cancellationToken: TestContext.Current.CancellationToken);

        TextOf(result).ShouldContain("name the same field");
        _seam.Jira.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_confirmation_names_the_field_the_way_the_caller_did()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(204));

        var result = await _client.CallToolAsync(
            "jira_update_issue",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["fields"] = new Dictionary<string, object?> { ["story_points"] = 5 },
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // The prose carries both names so the agent can match its own request; the structured half
        // carries the identifier alone, which is what a follow-up call must send.
        TextOf(result).ShouldContain("story_points (customfield_10010)");

        result.StructuredContent.ShouldNotBeNull()
            .GetProperty("changed").EnumerateArray()
            .Select(field => field.GetString())
            .ShouldBe(["customfield_10010"]);
    }

    [Fact]
    public async Task An_alias_named_after_an_expansions_own_field_does_not_hijack_the_expansion()
    {
        await _seam.RunAsync(
            ["profile", "alias", "set", ProtocolSeam.Profile, "comment", "customfield_10050"]);

        // Aliases are read at startup, so a test that declares one after the fact needs a
        // server of its own.
        var client = await _seam.ConnectAsync("issues:write");

        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42").UsingGet())
            .RespondWith(JiraResponse.Json(200, """
                {
                  "key": "PROJ-42",
                  "fields": {
                    "summary": "It fell over",
                    "comment": { "total": 1, "comments": [ { "body": "Looked at it." } ] }
                  }
                }
                """));

        var result = await client.CallToolAsync(
            "jira_get_issues",
            new Dictionary<string, object?>
            {
                ["keys"] = new[] { "PROJ-42" },
                ["include"] = new[] { "comments" },
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // Asking Jira for customfield_10050 here would return an issue with no comments on it —
        // a wrong answer that reads exactly like a right one.
        SingleRequest("/rest/api/2/issue/PROJ-42").Query.ShouldNotBeNull()["fields"]
            .ShouldHaveSingleItem().ShouldContain("comment");

        TextOf(result).ShouldContain("Looked at it.");
    }

    private static string TextOf(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;

    private IRequestMessage SingleRequest(string path) =>
        _seam.Jira.LogEntries
            .Select(entry => entry.RequestMessage)
            .OfType<IRequestMessage>()
            .Single(request => request.Path == path);
}
