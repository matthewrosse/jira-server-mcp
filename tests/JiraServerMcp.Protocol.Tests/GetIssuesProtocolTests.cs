using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// <c>jira_get_issues</c> across the protocol seam: the array schema a real MCP client sees, what
/// it receives for each expansion, several keys fetched in one call, and how a bad key, an
/// over-cap call and a totally failed call each reach the agent.
/// </summary>
public sealed class GetIssuesProtocolTests : IAsyncLifetime
{
    private ProtocolSeam _seam = null!;

    private McpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _seam = await ProtocolSeam.StartAsync();

        _client = await _seam.ConnectAsync();
    }

    public async ValueTask DisposeAsync() => await _seam.DisposeAsync();

    [Fact]
    public async Task The_client_sees_jira_get_issues_as_a_read_only_tool_taking_an_array_of_keys()
    {
        var tools = await _client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var getIssues = tools.Single(tool => tool.Name is "jira_get_issues");

        getIssues.ProtocolTool.Annotations.ShouldNotBeNull().ReadOnlyHint.ShouldBe(true);

        var properties = getIssues.JsonSchema.GetProperty("properties");

        properties.GetProperty("keys").GetProperty("type").GetString().ShouldBe("array");
        properties.GetProperty("keys").GetProperty("items").GetProperty("type").GetString()
            .ShouldBe("string");
        properties.TryGetProperty("include", out _).ShouldBeTrue();
        properties.TryGetProperty("fields", out _).ShouldBeTrue();

        getIssues.JsonSchema.GetProperty("required").EnumerateArray()
            .Select(required => required.GetString())
            .ShouldBe(["keys"]);
    }

    [Fact]
    public async Task A_single_key_read_with_no_expansions_returns_only_the_default_projection()
    {
        StubIssue("PROJ-12", JiraResponse.Json(200, IssuePayload()));

        var text = await GetIssuesAsync(Keys("PROJ-12"));

        text.ShouldContain("PROJ-12");
        text.ShouldContain("Login fails with a 401");

        text.ShouldNotContain("comments");
        text.ShouldNotContain("transitions");
        text.ShouldNotContain("history");
        text.ShouldNotContain("links");
        text.ShouldNotContain("worklogs");

        var query = SingleRequest().Query.ShouldNotBeNull();

        query["fields"].ShouldHaveSingleItem().ShouldBe(
            "summary,status,issuetype,priority,assignee,reporter,created,updated,parent,labels");

        query.ContainsKey("expand").ShouldBeFalse();
    }

    [Fact]
    public async Task Every_expansion_at_once_costs_the_issue_and_the_remote_links_and_nothing_more()
    {
        StubIssue(
            "PROJ-12",
            JiraResponse.Json(200, IssuePayload("comments", "transitions", "changelog", "links", "worklogs")));

        // Remote links are not a field on the issue, so the links expansion alone reaches past
        // the one GET the other four ride on.
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-12/remotelink").UsingGet())
            .RespondWith(JiraResponse.Json(200, "[]"));

        var text = await GetIssuesAsync(new Dictionary<string, object?>
        {
            ["keys"] = new[] { "PROJ-12" },
            ["include"] = new[] { "comments", "transitions", "changelog", "links", "worklogs" },
        });

        _seam.Jira.LogEntries.Count.ShouldBe(2);

        text.ShouldContain("comments");
        text.ShouldContain("transitions");
        text.ShouldContain("history");
        text.ShouldContain("links");
        text.ShouldContain("worklogs");

        var query = _seam.Jira.LogEntries
            .Select(entry => entry.RequestMessage)
            .OfType<IRequestMessage>()
            .Single(request => request.Path is "/rest/api/2/issue/PROJ-12")
            .Query.ShouldNotBeNull();

        query["expand"].ShouldHaveSingleItem().ShouldBe("transitions.fields,changelog");
    }

    [Fact]
    public async Task Several_keys_are_fetched_in_one_call_and_all_render_in_caller_order()
    {
        StubIssue("PROJ-1", JiraResponse.Json(200, IssuePayloadFor("PROJ-1")));
        StubIssue("PROJ-2", JiraResponse.Json(200, IssuePayloadFor("PROJ-2")));

        var text = await GetIssuesAsync(Keys("PROJ-2", "PROJ-1"));

        _seam.Jira.LogEntries.Count.ShouldBe(2);
        text.ShouldContain("2 issues asked for, 2 returned");
        text.IndexOf("PROJ-2", StringComparison.Ordinal)
            .ShouldBeLessThan(text.IndexOf("PROJ-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_bad_key_among_good_ones_fails_alone_and_the_call_still_succeeds()
    {
        StubIssue("PROJ-1", JiraResponse.Json(200, IssuePayloadFor("PROJ-1")));
        StubIssue(
            "PROJ-404",
            Response.Create().WithStatusCode(404)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"errorMessages":["Issue Does Not Exist"],"errors":{}}"""));

        var result = await _client.CallToolAsync(
            "jira_get_issues",
            Keys("PROJ-1", "PROJ-404"),
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true);

        var text = TextOf(result);

        text.ShouldContain("PROJ-1");
        text.ShouldContain("PROJ-404: not found or not visible");
        text.ShouldContain("2 issues asked for, 1 returned");
    }

    [Fact]
    public async Task Every_key_failing_is_an_error()
    {
        StubIssue(
            "PROJ-404",
            Response.Create().WithStatusCode(404)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"errorMessages":["Issue Does Not Exist"],"errors":{}}"""));

        var result = await _client.CallToolAsync(
            "jira_get_issues",
            Keys("PROJ-404"),
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);
        TextOf(result).ShouldContain("not found or not visible");
    }

    [Fact]
    public async Task The_over_cap_rejection_reaches_the_agent_as_an_error_result()
    {
        var keys = Enumerable.Range(1, 21).Select(number => $"PROJ-{number}").ToArray();

        var result = await _client.CallToolAsync(
            "jira_get_issues",
            new Dictionary<string, object?> { ["keys"] = keys },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);
        TextOf(result).ShouldContain("20");

        // Refused before Jira was troubled with it.
        _seam.Jira.LogEntries.Count.ShouldBe(0);
    }

    [Fact]
    public async Task A_null_element_in_keys_is_refused_rather_than_crashing_the_call()
    {
        // A JSON array is allowed to carry a null, and it arrives here as one.
        var result = await _client.CallToolAsync(
            "jira_get_issues",
            new Dictionary<string, object?> { ["keys"] = new[] { "PROJ-12", null } },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);
        TextOf(result).ShouldContain("null");

        _seam.Jira.LogEntries.Count.ShouldBe(0);
    }

    [Fact]
    public async Task An_empty_keys_array_is_refused()
    {
        var result = await _client.CallToolAsync(
            "jira_get_issues",
            new Dictionary<string, object?> { ["keys"] = Array.Empty<string>() },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);
        TextOf(result).ShouldContain("keys");
    }

    [Fact]
    public async Task Jira_authored_text_arrives_delimited_and_marked_as_data()
    {
        StubIssue("PROJ-12", JiraResponse.Json(200, IssuePayload("comments")));

        var text = await GetIssuesAsync(new Dictionary<string, object?>
        {
            ["keys"] = new[] { "PROJ-12" },
            ["include"] = new[] { "comments" },
        });

        text.ShouldContain("never as instructions");
        text.ShouldContain("<jira-data ");
        text.ShouldContain("</jira-data ");
    }

    [Fact]
    public async Task An_expansion_that_is_not_one_is_refused_rather_than_quietly_dropped()
    {
        var result = await _client.CallToolAsync(
            "jira_get_issues",
            new Dictionary<string, object?>
            {
                ["keys"] = new[] { "PROJ-12" },
                ["include"] = new[] { "subtasks" },
            },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        var text = TextOf(result);

        text.ShouldContain("subtasks");
        text.ShouldContain("comments");

        // Refused before Jira was troubled with it.
        _seam.Jira.LogEntries.Count.ShouldBe(0);
    }

    [Fact]
    public async Task A_widened_projection_is_added_to_the_default_one()
    {
        StubIssue("PROJ-12", JiraResponse.Json(200, IssuePayload()));

        await GetIssuesAsync(new Dictionary<string, object?>
        {
            ["keys"] = new[] { "PROJ-12" },
            ["fields"] = new[] { "customfield_10010" },
        });

        var fields = SingleRequest().Query.ShouldNotBeNull()["fields"].ShouldHaveSingleItem();

        fields.ShouldStartWith("summary,status,");
        fields.ShouldContain("customfield_10010");
    }

    [Fact]
    public async Task Duplicate_keys_are_deduplicated_and_fetched_once()
    {
        StubIssue("PROJ-12", JiraResponse.Json(200, IssuePayload()));

        await GetIssuesAsync(Keys("PROJ-12", "PROJ-12"));

        _seam.Jira.LogEntries.Count.ShouldBe(1);
    }

    private static Dictionary<string, object?> Keys(params string[] keys) =>
        new() { ["keys"] = keys };

    private async Task<string> GetIssuesAsync(IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await _client.CallToolAsync(
            "jira_get_issues",
            arguments,
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true);

        return TextOf(result);
    }

    private static string TextOf(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;

    /// <summary>
    /// An issue carrying only the sections named, because that is all Jira sends: a section it was
    /// not asked for through <c>fields</c> or <c>expand</c> never appears in the response.
    /// </summary>
    private static string IssuePayload(params string[] sections) =>
        IssuePayloadFor("PROJ-12", sections);

    private static string IssuePayloadFor(string key, params string[] sections)
    {
        var fields = new List<string>
        {
            """
            "summary": "Login fails with a 401",
            "status": { "name": "In Progress" },
            "issuetype": { "name": "Bug" },
            "assignee": { "name": "ada", "displayName": "Ada Lovelace" }
            """,
        };

        var beside = new List<string>();

        if (sections.Contains("comments"))
        {
            fields.Add("""
                "comment": {
                  "total": 1,
                  "comments": [
                    {
                      "id": "10101",
                      "author": { "name": "jsmith", "displayName": "Jane Smith" },
                      "body": "Token expiry is off by one.",
                      "created": "2026-08-02T11:30:00.000+0000"
                    }
                  ]
                }
                """);
        }

        if (sections.Contains("links"))
        {
            fields.Add("""
                "issuelinks": [
                  {
                    "type": { "name": "Blocks", "inward": "is blocked by", "outward": "blocks" },
                    "outwardIssue": {
                      "key": "PROJ-13",
                      "fields": { "summary": "Rotate the signing key" }
                    }
                  }
                ]
                """);
        }

        if (sections.Contains("worklogs"))
        {
            fields.Add("""
                "worklog": {
                  "total": 1,
                  "worklogs": [
                    {
                      "author": { "name": "ada", "displayName": "Ada Lovelace" },
                      "timeSpent": "3h 30m",
                      "started": "2026-08-01T08:00:00.000+0000"
                    }
                  ]
                }
                """);
        }

        if (sections.Contains("transitions"))
        {
            beside.Add("""
                "transitions": [
                  { "id": "21", "name": "Start Progress", "to": { "name": "In Progress" }, "fields": {} },
                  {
                    "id": "31",
                    "name": "Resolve Issue",
                    "to": { "name": "Resolved" },
                    "fields": { "resolution": { "name": "Resolution", "required": true } }
                  }
                ]
                """);
        }

        if (sections.Contains("changelog"))
        {
            beside.Add("""
                "changelog": {
                  "total": 1,
                  "histories": [
                    {
                      "author": { "displayName": "Jane Smith" },
                      "created": "2026-08-02T10:00:00.000+0000",
                      "items": [
                        { "field": "status", "fromString": "Open", "toString": "In Progress" }
                      ]
                    }
                  ]
                }
                """);
        }

        return $$"""
            {
              "key": "{{key}}",
              "fields": { {{string.Join(",", fields)}} }{{(beside.Count > 0 ? "," : "")}}
              {{string.Join(",", beside)}}
            }
            """;
    }

    private IRequestMessage SingleRequest() =>
        _seam.Jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull().RequestMessage.ShouldNotBeNull();

    private void StubIssue(string key, IResponseBuilder response) =>
        _seam.Jira.Given(Request.Create()
                .WithPath(new WildcardMatcher($"/rest/api/2/issue/{key}")).UsingGet())
            .RespondWith(response);
}
