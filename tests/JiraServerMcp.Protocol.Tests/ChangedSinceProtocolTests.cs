using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// <c>jira_changed_since</c> across the protocol seam (ADR-0008): the window a tick asks Jira for,
/// the watermark the next one gets back in both halves of the result, and what a caller sees when
/// the moment it passed carries no offset or when Jira refuses the call.
/// </summary>
public sealed class ChangedSinceProtocolTests : IAsyncLifetime
{
    /// <summary>
    /// The instance's own clock, one hour east of UTC — deliberately not the account's zone, so
    /// that a test asserting a window can only pass on one of the two.
    /// </summary>
    private const string ServerInfoPayload = """
        {
          "version": "8.20.7",
          "deploymentType": "Server",
          "serverTime": "2026-08-18T08:45:12.001+0100"
        }
        """;

    private ProtocolSeam _seam = null!;

    private McpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _seam = await ProtocolSeam.StartAsync();

        StubMyself();

        _client = await _seam.ConnectAsync();
    }

    public async ValueTask DisposeAsync() => await _seam.DisposeAsync();

    [Fact]
    public async Task The_client_sees_a_read_only_tool_whose_one_required_argument_is_the_moment()
    {
        var tools = await _client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var tool = tools.Single(entry => entry.Name is "jira_changed_since");

        tool.ProtocolTool.Annotations.ShouldNotBeNull().ReadOnlyHint.ShouldBe(true);

        var properties = tool.JsonSchema.GetProperty("properties");

        properties.TryGetProperty("project", out _).ShouldBeTrue();
        properties.TryGetProperty("startAt", out _).ShouldBeTrue();

        tool.JsonSchema.GetProperty("required").EnumerateArray()
            .Select(required => required.GetString())
            .ShouldBe(["since"]);
    }

    [Fact]
    public async Task The_window_is_asked_for_in_the_accounts_own_zone_oldest_change_first()
    {
        StubSearch(JiraResponse.Json(200, SearchPayload(("PROJ-12", "2026-08-18T09:31:47.412+0200"))));

        // 07:20 UTC is 09:20 where this Jira is, and Jira reads the literal in its own zone.
        var text = await ChangedSinceAsync(
            new Dictionary<string, object?> { ["since"] = "2026-08-18T07:20:00Z" });

        var expected = """updated >= "2026/08/18 09:20" ORDER BY updated ASC""";

        text.ShouldContain($"jql: {expected}");

        SearchRequest().Query.ShouldNotBeNull()["jql"].ShouldHaveSingleItem().ShouldBe(expected);
    }

    [Fact]
    public async Task The_watermark_reaches_the_caller_in_the_prose_and_in_the_structured_half()
    {
        StubSearch(JiraResponse.Json(200, SearchPayload(("PROJ-12", "2026-08-18T09:31:47.412+0200"))));

        var result = await CallAsync(
            new Dictionary<string, object?> { ["since"] = "2026-08-18T09:00:00+02:00" });

        result.IsError.ShouldNotBe(true);

        var structure = result.StructuredContent.ShouldNotBeNull();

        // The start of the last-seen minute, not the moment of the last change: the feed repeats
        // rather than skips.
        structure.GetProperty("nextSince").GetString().ShouldBe("2026-08-18T09:31:00+02:00");
        structure.GetProperty("outcome").GetString().ShouldBe("ok");
        structure.GetProperty("issues").EnumerateArray()
            .Select(issue => issue.GetProperty("key").GetString())
            .ShouldBe(["PROJ-12"]);

        TextOf(result).ShouldContain("nextSince: 2026-08-18T09:31:00+02:00");
    }

    [Fact]
    public async Task A_tick_on_which_nothing_changed_still_hands_back_a_watermark()
    {
        StubSearch(JiraResponse.Json(200, SearchPayload()));

        var result = await CallAsync(
            new Dictionary<string, object?> { ["since"] = "2026-08-18T09:14:32+02:00" });

        result.IsError.ShouldNotBe(true);

        // The quiet tick is the common one, and a loop that lost its watermark on it would have
        // to remember a timestamp itself — which is the work this tool exists to take off it.
        result.StructuredContent.ShouldNotBeNull()
            .GetProperty("nextSince").GetString().ShouldBe("2026-08-18T09:14:00+02:00");
    }

    [Fact]
    public async Task A_project_narrows_the_window_without_changing_what_it_means()
    {
        StubSearch(JiraResponse.Json(200, SearchPayload(("PROJ-12", "2026-08-18T09:31:00.000+0200"))));

        await ChangedSinceAsync(new Dictionary<string, object?>
        {
            ["since"] = "2026-08-18T09:00:00+02:00",
            ["project"] = "PROJ",
        });

        SearchRequest().Query.ShouldNotBeNull()["jql"].ShouldHaveSingleItem().ShouldBe(
            """project = PROJ AND updated >= "2026/08/18 09:00" ORDER BY updated ASC""");
    }

    [Fact]
    public async Task The_window_is_stated_in_the_accounts_zone_not_the_instances()
    {
        // Jira evaluates a bare date literal in the zone of the account running the query. This
        // account sits four hours west of UTC while the instance's own clock runs one hour east,
        // so a window built from the instance's offset would be five hours out — and a window
        // shifted forward skips changes with nothing in the response to show for it.
        StubMyself("America/New_York");
        StubServerInfo();
        StubSearch(JiraResponse.Json(200, SearchPayload(("PROJ-12", "2026-08-18T03:31:00.000-0400"))));

        await ChangedSinceAsync(
            new Dictionary<string, object?> { ["since"] = "2026-08-18T07:20:00Z" });

        SearchRequest().Query.ShouldNotBeNull()["jql"].ShouldHaveSingleItem().ShouldBe(
            """updated >= "2026/08/18 03:20" ORDER BY updated ASC""");
    }

    [Fact]
    public async Task A_zone_this_machine_cannot_resolve_falls_back_to_the_instances_own_clock()
    {
        StubMyself(timeZone: null);
        StubServerInfo();
        StubSearch(JiraResponse.Json(200, SearchPayload(("PROJ-12", "2026-08-18T08:31:00.000+0100"))));

        await ChangedSinceAsync(
            new Dictionary<string, object?> { ["since"] = "2026-08-18T07:20:00Z" });

        // The instance's clock runs one hour east, which is the closest thing to the account's
        // zone that Jira will state.
        SearchRequest().Query.ShouldNotBeNull()["jql"].ShouldHaveSingleItem().ShouldBe(
            """updated >= "2026/08/18 08:20" ORDER BY updated ASC""");
    }

    [Fact]
    public async Task A_window_holding_more_than_one_page_does_not_move_the_watermark_past_it()
    {
        // Every row in the same minute as the window's start — a bulk edit or an import — with
        // more behind it. Advancing the watermark here would strand every row the page did not
        // carry, so it stays put and the caller is told to page.
        StubSearch(JiraResponse.Json(200, SearchPayload(
            total: 400,
            ("PROJ-1", "2026-08-18T09:14:10.000+0200"),
            ("PROJ-2", "2026-08-18T09:14:55.000+0200"))));

        var result = await CallAsync(
            new Dictionary<string, object?> { ["since"] = "2026-08-18T09:14:00+02:00" });

        var structure = result.StructuredContent.ShouldNotBeNull();

        structure.GetProperty("nextSince").GetString().ShouldBe("2026-08-18T09:14:00+02:00");
        structure.GetProperty("nextStartAt").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task A_moment_with_no_offset_is_refused_before_anything_reaches_jira()
    {
        var result = await CallAsync(
            new Dictionary<string, object?> { ["since"] = "2026-08-18T09:00:00" });

        TextOf(result).ShouldContain("is not a timestamp with an offset");
        result.StructuredContent.ShouldNotBeNull()
            .GetProperty("outcome").GetString().ShouldBe("refused");

        _seam.Jira.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_jira_that_refuses_the_search_says_so_and_carries_no_watermark()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(403)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"errorMessages":["You do not have permission"],"errors":{}}"""));

        var result = await CallAsync(
            new Dictionary<string, object?> { ["since"] = "2026-08-18T09:00:00+02:00" });

        result.IsError.ShouldBe(true);

        var structure = result.StructuredContent.ShouldNotBeNull();

        structure.GetProperty("outcome").GetString().ShouldBe("jira_api");
        structure.GetProperty("statusCode").GetInt32().ShouldBe(403);

        // Nothing to resume from: a watermark on a call that read nothing would move the window
        // past a window that was never read.
        structure.TryGetProperty("nextSince", out _).ShouldBeFalse();
    }

    private async Task<CallToolResult> CallAsync(IReadOnlyDictionary<string, object?> arguments) =>
        await _client.CallToolAsync(
            "jira_changed_since",
            arguments,
            cancellationToken: TestContext.Current.CancellationToken);

    private async Task<string> ChangedSinceAsync(IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await CallAsync(arguments);

        result.IsError.ShouldNotBe(true);

        return TextOf(result);
    }

    private static string TextOf(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;

    private static string SearchPayload(params (string Key, string Updated)[] issues) =>
        SearchPayload(issues.Length, issues);

    private static string SearchPayload(int total, params (string Key, string Updated)[] issues)
    {
        var rendered = issues.Select(issue => $$"""
            {
              "key": "{{issue.Key}}",
              "fields": {
                "summary": "Login fails with a 401",
                "status": { "id": "3", "name": "In Progress" },
                "issuetype": { "name": "Bug" },
                "updated": "{{issue.Updated}}"
              }
            }
            """);

        return JsonSerializer.Serialize(new
        {
            startAt = 0,
            maxResults = 25,
            total,
        }).TrimEnd('}')
           + ",\"issues\":[" + string.Join(",", rendered) + "]}";
    }

    private IRequestMessage SearchRequest() =>
        _seam.Jira.LogEntries
            .Select(entry => entry.RequestMessage)
            .OfType<IRequestMessage>()
            .Single(request => request.Path is "/rest/api/2/search");

    private void StubServerInfo() =>
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/serverInfo").UsingGet())
            .RespondWith(JiraResponse.Json(200, ServerInfoPayload));

    /// <summary>
    /// The account this server is authenticated as. Its default zone is two hours east of UTC —
    /// the zone Jira reads its JQL date literals in, and deliberately not the instance's own.
    /// </summary>
    private void StubMyself(string? timeZone = "Europe/Warsaw") =>
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(JiraResponse.Json(200, JiraAccount.Payload(timeZone)));

    private void StubSearch(IResponseBuilder response) =>
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
            .RespondWith(response);
}
