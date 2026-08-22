using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// <c>jira_my_open_issues</c> across the protocol seam: what a real MCP client receives, and what
/// the faked Jira was asked for.
/// </summary>
public sealed class MyOpenIssuesProtocolTests : IAsyncLifetime
{
    private const string BaseJql = "assignee = currentUser() AND resolution = Unresolved ORDER BY updated DESC";

    private ProtocolSeam _seam = null!;

    private McpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _seam = await ProtocolSeam.StartAsync();

        _client = await _seam.ConnectAsync();
    }

    public async ValueTask DisposeAsync() => await _seam.DisposeAsync();

    [Fact]
    public async Task The_client_sees_the_tool_as_read_only_with_no_required_parameters()
    {
        var tools = await _client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var tool = tools.Single(entry => entry.Name is "jira_my_open_issues");

        tool.ProtocolTool.Annotations.ShouldNotBeNull().ReadOnlyHint.ShouldBe(true);

        var properties = tool.JsonSchema.GetProperty("properties");

        properties.TryGetProperty("project", out _).ShouldBeTrue();
        properties.TryGetProperty("startAt", out _).ShouldBeTrue();
        properties.TryGetProperty("maxResults", out _).ShouldBeTrue();
        properties.TryGetProperty("fields", out _).ShouldBeTrue();

        if (tool.JsonSchema.TryGetProperty("required", out var required))
        {
            required.EnumerateArray().ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task With_no_project_the_query_is_the_bare_canned_jql()
    {
        StubSearch(JiraResponse.Json(200, SearchPayload(total: 1, ("PROJ-12", "Login fails with a 401"))));

        var text = await MyOpenIssuesAsync(new Dictionary<string, object?>());

        text.ShouldContain($"jql: {BaseJql}");
        text.ShouldContain("PROJ-12");

        SingleRequest().Query.ShouldNotBeNull()["jql"].ShouldHaveSingleItem().ShouldBe(BaseJql);
    }

    [Fact]
    public async Task The_canned_jql_is_a_line_above_the_page_rather_than_a_wrapper_around_it()
    {
        StubSearch(JiraResponse.Json(200, SearchPayload(total: 1, ("PROJ-12", "Login fails with a 401"))));

        var result = await _client.CallToolAsync(
            "jira_my_open_issues",
            new Dictionary<string, object?>(),
            cancellationToken: TestContext.Current.CancellationToken);

        var text = result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;

        // The renderer answers with a text-and-structure pair (ADR-0009, rule 4). Putting the
        // whole pair where the text belongs prints the record's own shape into the prose and
        // leaves the structured half behind.
        text.ShouldNotContain("Rendered {");
        text.ShouldNotContain("Structure =");

        var structure = result.StructuredContent.ShouldNotBeNull();

        structure.GetProperty("total").GetInt32().ShouldBe(1);
        structure.GetProperty("issues").EnumerateArray()
            .Select(issue => issue.GetProperty("key").GetString())
            .ShouldBe(["PROJ-12"]);
    }

    [Fact]
    public async Task A_project_is_prefixed_onto_the_canned_jql()
    {
        StubSearch(JiraResponse.Json(200, SearchPayload(total: 1, ("PROJ-12", "Login fails with a 401"))));

        var expected = $"project = PROJ AND {BaseJql}";

        var text = await MyOpenIssuesAsync(new Dictionary<string, object?> { ["project"] = "PROJ" });

        text.ShouldContain($"jql: {expected}");

        SingleRequest().Query.ShouldNotBeNull()["jql"].ShouldHaveSingleItem().ShouldBe(expected);
    }

    [Fact]
    public async Task A_project_key_that_fails_the_grammar_is_rejected_before_any_jira_call()
    {
        // Called directly rather than through MyOpenIssuesAsync: that helper asserts the call
        // succeeded, which is right for every other case in this file and wrong for this one.
        var result = await _client.CallToolAsync(
            "jira_my_open_issues",
            new Dictionary<string, object?> { ["project"] = "proj-1; DROP" },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem()
            .Text.ShouldContain("not a valid Jira project key");

        result.StructuredContent.ShouldNotBeNull()
            .GetProperty("outcome").GetString().ShouldBe("refused");

        _seam.Jira.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_default_page_and_projection_match_search()
    {
        StubSearch(JiraResponse.Json(200, SearchPayload(total: 1, ("PROJ-12", "Login fails with a 401"))));

        await MyOpenIssuesAsync(new Dictionary<string, object?>());

        var query = SingleRequest().Query.ShouldNotBeNull();

        query["startAt"].ShouldHaveSingleItem().ShouldBe("0");
        query["maxResults"].ShouldHaveSingleItem().ShouldBe("25");
        query["fields"].ShouldHaveSingleItem().ShouldBe(
            "summary,status,issuetype,priority,assignee,reporter,created,updated,parent,labels");
    }

    [Fact]
    public async Task A_request_for_more_than_a_hundred_results_is_clamped_rather_than_rejected()
    {
        StubSearch(JiraResponse.Json(200, SearchPayload(total: 4_000, ("PROJ-12", "Login fails with a 401"))));

        await MyOpenIssuesAsync(new Dictionary<string, object?> { ["maxResults"] = 500 });

        SingleRequest().Query.ShouldNotBeNull()["maxResults"].ShouldHaveSingleItem().ShouldBe("100");
    }

    private async Task<string> MyOpenIssuesAsync(IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await _client.CallToolAsync(
            "jira_my_open_issues",
            arguments,
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true);

        return result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;
    }

    private static string SearchPayload(int total, params (string Key, string Summary)[] issues)
    {
        var rendered = issues.Select(issue => $$"""
            {
              "key": "{{issue.Key}}",
              "fields": {
                "summary": "{{issue.Summary}}",
                "status": { "name": "In Progress" },
                "issuetype": { "name": "Bug" },
                "assignee": { "name": "ada", "displayName": "Ada Lovelace" },
                "labels": ["api", "backend"]
              }
            }
            """);

        return JsonSerializer.Serialize(new
        {
            startAt = 0,
            maxResults = 100,
            total,
        }).TrimEnd('}')
           + ",\"issues\":[" + string.Join(",", rendered) + "]}";
    }

    private IRequestMessage SingleRequest() =>
        _seam.Jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull().RequestMessage.ShouldNotBeNull();

    private void StubSearch(IResponseBuilder response) =>
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
            .RespondWith(response);
}
