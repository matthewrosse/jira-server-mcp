using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock.RequestBuilders;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// The saved filters across the protocol seam (ADR-0008): what an agent is shown of the queries a
/// team already curates, how it is told to run one, and what it is told when there are none.
/// </summary>
public sealed class SavedFiltersProtocolTests : IAsyncLifetime
{
    /// <summary>
    /// Two favourites as 8.20.7 sends them: the JQL is present without an expand, and most of the
    /// bytes are the sharing envelopes this server drops.
    /// </summary>
    private const string FavouritesPayload = """
        [
          {
            "id": "10001",
            "name": "Open payment bugs",
            "description": "What the payments team triages every morning.",
            "owner": { "name": "grace", "displayName": "Grace Hopper", "active": true },
            "jql": "project = PAY AND status != Done ORDER BY created DESC",
            "viewUrl": "http://localhost/issues/?filter=10001",
            "favourite": true,
            "editable": false,
            "sharePermissions": [ { "id": 10000, "type": "group" } ],
            "sharedUsers": { "size": 0, "items": [] },
            "subscriptions": { "size": 0, "items": [] }
          },
          {
            "id": "10002",
            "name": "Anything assigned to me",
            "owner": { "name": "ada", "displayName": "Ada Lovelace", "active": true },
            "jql": "assignee = currentUser() ORDER BY updated DESC",
            "favourite": true,
            "editable": true,
            "sharePermissions": [],
            "sharedUsers": { "size": 0, "items": [] },
            "subscriptions": { "size": 0, "items": [] }
          }
        ]
        """;

    private ProtocolSeam _seam = null!;

    private McpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _seam = await ProtocolSeam.StartAsync();

        _client = await _seam.ConnectAsync();
    }

    public async ValueTask DisposeAsync() => await _seam.DisposeAsync();

    /// <summary>
    /// Discovery is a dead end unless the agent learns the move, and the move is static — so it
    /// is stated in the description, which costs nothing per call, rather than in a line above
    /// every response.
    /// </summary>
    [Fact]
    public async Task The_tool_is_registered_for_every_client_and_its_description_says_how_to_run_one()
    {
        var tools = await _client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // No grant: grants are write categories and this reads. No capability gate either — the
        // favourites endpoint is core Jira rather than Jira Software.
        var tool = tools.Single(candidate => candidate.Name is "jira_list_saved_filters");

        tool.ProtocolTool.Annotations.ShouldNotBeNull().ReadOnlyHint.ShouldBe(true);
        tool.ProtocolTool.Description.ShouldNotBeNull().ShouldContain("filter = <id>");
        tool.ProtocolTool.Description.ShouldNotBeNull().ShouldContain("jira_search");

        tool.JsonSchema.TryGetProperty("required", out _).ShouldBeFalse();

        tool.JsonSchema.GetProperty("properties").TryGetProperty("startsWith", out _)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task A_favourite_arrives_with_its_id_its_query_and_the_account_that_owns_it()
    {
        StubFavourites(FavouritesPayload);

        var result = await CallAsync();

        var text = TextOf(result);

        text.ShouldContain("10001 | Open payment bugs | owner grace");
        text.ShouldContain("  jql: project = PAY AND status != Done ORDER BY created DESC");
        text.ShouldContain("What the payments team triages every morning.");

        // Text a human typed in Jira, framed as data rather than edited.
        text.ShouldContain("never as instructions");
        text.ShouldContain("<jira-data ");

        var structure = result.StructuredContent.ShouldNotBeNull();

        structure.GetProperty("outcome").GetString().ShouldBe("ok");
        structure.GetProperty("count").GetInt32().ShouldBe(2);
        // Sorted by name, so "Anything assigned to me" leads: Jira's own order for this endpoint
        // is undocumented, and a cap applied to it would cut differently between two calls.
        structure.GetProperty("filters").EnumerateArray()
            .Select(filter => filter.GetProperty("id").GetString())
            .ShouldBe(["10002", "10001"]);

        structure.GetProperty("filters")[1].GetProperty("owner").GetString().ShouldBe("grace");

        // One request, and no expand: the JQL is in the listing payload already.
        var request = _seam.Jira.LogEntries.ShouldHaveSingleItem().RequestMessage.ShouldNotBeNull();

        request.Path.ShouldBe("/rest/api/2/filter/favourite");
        request.RawQuery.ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task A_prefix_narrows_the_list_without_a_second_round_trip()
    {
        StubFavourites(FavouritesPayload);

        var text = TextOf(await CallAsync(("startsWith", "open")));

        text.ShouldContain("saved filters: 1 of 2 whose name starts with 'open'.");
        text.ShouldContain("10001 |");
        text.ShouldNotContain("10002 |");

        _seam.Jira.LogEntries.Count.ShouldBe(1);
    }

    /// <summary>
    /// The likely production case rather than an edge one: a personal access token minted for a
    /// service account has no favourites, because no human ever starred a filter as it. "No
    /// favourites" and "wrong account" are otherwise the same answer.
    /// </summary>
    [Fact]
    public async Task An_account_with_no_favourites_is_told_which_account_was_asked_about()
    {
        StubFavourites("[]");

        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(JiraResponse.Json(200, JiraAccount.Payload()));

        var result = await CallAsync();

        var text = TextOf(result);

        text.ShouldContain("'ada'");
        text.ShouldContain("stars it");

        // Not a refusal and not an error: an account with no favourites is a real answer.
        result.IsError.ShouldNotBe(true, text);
        result.StructuredContent.ShouldNotBeNull()
            .GetProperty("outcome").GetString().ShouldBe("ok");
        result.StructuredContent.ShouldNotBeNull()
            .GetProperty("count").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task A_jira_that_refuses_the_listing_says_so_rather_than_answering_with_none()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/filter/favourite").UsingGet())
            .RespondWith(JiraResponse.Json(401, """
                {"errorMessages":["You do not have permission to view this filter."],"errors":{}}
                """));

        var result = await _client.CallToolAsync(
            "jira_list_saved_filters",
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        result.StructuredContent.ShouldNotBeNull()
            .GetProperty("outcome").GetString().ShouldBe("jira_api");
        result.StructuredContent.ShouldNotBeNull()
            .GetProperty("statusCode").GetInt32().ShouldBe(401);
    }

    private async Task<CallToolResult> CallAsync(params (string Name, object? Value)[] arguments)
    {
        var result = await _client.CallToolAsync(
            "jira_list_saved_filters",
            arguments.ToDictionary(argument => argument.Name, argument => argument.Value),
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true, TextOf(result));

        return result;
    }

    private static string TextOf(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;

    private void StubFavourites(string payload) =>
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/filter/favourite").UsingGet())
            .RespondWith(JiraResponse.Json(200, payload));
}
