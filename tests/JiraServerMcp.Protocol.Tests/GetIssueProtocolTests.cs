using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// <c>jira_get_issue</c> across the protocol seam: what a real MCP client receives for each
/// expansion, and how many requests the faked Jira saw while answering.
/// </summary>
public sealed class GetIssueProtocolTests : IAsyncLifetime
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

        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Json(MyselfPayload));

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

    [Fact]
    public async Task The_client_sees_jira_get_issue_as_a_read_only_tool_taking_an_issue_key()
    {
        var tools = await _client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var getIssue = tools.Single(tool => tool.Name is "jira_get_issue");

        getIssue.ProtocolTool.Annotations.ShouldNotBeNull().ReadOnlyHint.ShouldBe(true);

        var properties = getIssue.JsonSchema.GetProperty("properties");

        properties.GetProperty("key").GetProperty("type").GetString().ShouldBe("string");
        properties.TryGetProperty("include", out _).ShouldBeTrue();
        properties.TryGetProperty("fields", out _).ShouldBeTrue();

        getIssue.JsonSchema.GetProperty("required").EnumerateArray()
            .Select(required => required.GetString())
            .ShouldBe(["key"]);
    }

    [Fact]
    public async Task An_issue_read_with_no_expansions_returns_only_the_default_projection()
    {
        StubIssue(Json(IssuePayload()));

        var text = await GetIssueAsync(new Dictionary<string, object?> { ["key"] = "PROJ-12" });

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
    public async Task Each_expansion_returns_its_section_and_is_absent_when_not_requested()
    {
        StubIssue(Json(IssuePayload("comments")));

        var comments = await GetIssueAsync(Include("comments"));

        comments.ShouldContain("comments");
        comments.ShouldContain("Token expiry is off by one.");
        comments.ShouldNotContain("history");
        comments.ShouldNotContain("worklogs");

        _jira.Reset();
        StubIssue(Json(IssuePayload("worklogs")));

        var worklogs = await GetIssueAsync(Include("worklogs"));

        worklogs.ShouldContain("worklogs");
        worklogs.ShouldContain("3h 30m");
        worklogs.ShouldNotContain("comments");
    }

    [Fact]
    public async Task Requesting_transitions_returns_their_names_and_the_fields_their_screens_require()
    {
        StubIssue(Json(IssuePayload("transitions")));

        var text = await GetIssueAsync(Include("transitions"));

        text.ShouldContain("Start Progress");
        text.ShouldContain("Resolve Issue");
        text.ShouldContain("requires");
        text.ShouldContain("resolution");

        // The plain "transitions" expand omits the screens, so it is the wrong one to ask for.
        SingleRequest().Query.ShouldNotBeNull()["expand"].ShouldHaveSingleItem()
            .ShouldBe("transitions.fields");
    }

    [Fact]
    public async Task Every_expansion_at_once_costs_one_request_not_five()
    {
        StubIssue(Json(IssuePayload("comments", "transitions", "changelog", "links", "worklogs")));

        var text = await GetIssueAsync(
            Include("comments", "transitions", "changelog", "links", "worklogs"));

        _jira.LogEntries.Count.ShouldBe(1);

        text.ShouldContain("comments");
        text.ShouldContain("transitions");
        text.ShouldContain("history");
        text.ShouldContain("links");
        text.ShouldContain("worklogs");

        var query = SingleRequest().Query.ShouldNotBeNull();

        var fields = query["fields"].ShouldHaveSingleItem();

        fields.ShouldContain("comment");
        fields.ShouldContain("issuelinks");
        fields.ShouldContain("worklog");

        query["expand"].ShouldHaveSingleItem().ShouldBe("transitions.fields,changelog");
    }

    [Fact]
    public async Task Comments_are_capped_and_the_cap_is_reported()
    {
        StubIssue(Json(IssueWithManyComments(60)));

        var text = await GetIssueAsync(Include("comments"));

        text.ShouldContain("of 60");
        text.ShouldContain("comment 60");
        text.ShouldNotContain("comment 1 of many");
    }

    [Fact]
    public async Task Changelog_entries_are_capped_and_the_cap_is_reported()
    {
        StubIssue(Json(IssueWithLongHistory(60)));

        var text = await GetIssueAsync(Include("changelog"));

        text.ShouldContain("of 60");
        text.ShouldContain("field60");
        text.ShouldNotContain("theOldestField");
    }

    [Fact]
    public async Task Jira_authored_text_arrives_delimited_and_marked_as_data()
    {
        StubIssue(Json(IssuePayload("comments")));

        var text = await GetIssueAsync(Include("comments"));

        text.ShouldContain("never as instructions");
        text.ShouldContain("<jira-data ");
        text.ShouldContain("</jira-data ");
    }

    [Fact]
    public async Task An_unknown_issue_key_explains_both_things_a_jira_404_can_mean()
    {
        StubIssue(Response.Create().WithStatusCode(404)
            .WithHeader("Content-Type", "application/json")
            .WithBody("""
                {"errorMessages":["Issue Does Not Exist"],"errors":{}}
                """));

        var result = await _client.CallToolAsync(
            "jira_get_issue",
            new Dictionary<string, object?> { ["key"] = "PROJ-9999" },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        var text = result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;

        text.ShouldContain("does not exist");
        text.ShouldContain("cannot see it");
        text.ShouldContain("nothing to retry");
    }

    [Fact]
    public async Task An_expansion_that_is_not_one_is_refused_rather_than_quietly_dropped()
    {
        StubIssue(Json(IssuePayload()));

        var result = await _client.CallToolAsync(
            "jira_get_issue",
            Include("attachments"),
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        var text = result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;

        text.ShouldContain("attachments");
        text.ShouldContain("comments");

        // Refused before Jira was troubled with it.
        _jira.LogEntries.Count.ShouldBe(0);
    }

    [Fact]
    public async Task A_widened_projection_is_added_to_the_default_one()
    {
        StubIssue(Json(IssuePayload()));

        await GetIssueAsync(new Dictionary<string, object?>
        {
            ["key"] = "PROJ-12",
            ["fields"] = new[] { "customfield_10010" },
        });

        var fields = SingleRequest().Query.ShouldNotBeNull()["fields"].ShouldHaveSingleItem();

        fields.ShouldStartWith("summary,status,");
        fields.ShouldContain("customfield_10010");
    }

    private static Dictionary<string, object?> Include(params string[] include) =>
        new() { ["key"] = "PROJ-12", ["include"] = include };

    private async Task<string> GetIssueAsync(IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await _client.CallToolAsync(
            "jira_get_issue",
            arguments,
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true);

        return result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;
    }

    /// <summary>
    /// An issue carrying only the sections named, because that is all Jira sends: a section it was
    /// not asked for through <c>fields</c> or <c>expand</c> never appears in the response.
    /// </summary>
    private static string IssuePayload(params string[] sections)
    {
        var fields = new List<string>
        {
            """
            "summary": "Login fails with a 401",
            "status": { "name": "In Progress" },
            "issuetype": { "name": "Bug" },
            "assignee": { "name": "mrosse", "displayName": "Mateusz Różański" }
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
                      "author": { "name": "mrosse", "displayName": "Mateusz Różański" },
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
              "key": "PROJ-12",
              "fields": { {{string.Join(",", fields)}} }{{(beside.Count > 0 ? "," : "")}}
              {{string.Join(",", beside)}}
            }
            """;
    }

    private static string IssueWithManyComments(int count)
    {
        var comments = Enumerable.Range(1, count).Select(number => $$"""
            {
              "id": "{{number}}",
              "author": { "displayName": "Jane Smith" },
              "body": "{{(number is 1 ? "comment 1 of many" : $"comment {number}")}}",
              "created": "2026-08-02T11:30:00.000+0000"
            }
            """);

        return $$"""
            {
              "key": "PROJ-12",
              "fields": {
                "summary": "Login fails with a 401",
                "comment": { "total": {{count}}, "comments": [{{string.Join(",", comments)}}] }
              }
            }
            """;
    }

    private static string IssueWithLongHistory(int count)
    {
        var histories = Enumerable.Range(1, count).Select(number => $$"""
            {
              "author": { "displayName": "Jane Smith" },
              "created": "2026-08-02T10:00:00.000+0000",
              "items": [
                {
                  "field": "{{(number is 1 ? "theOldestField" : $"field{number}")}}",
                  "fromString": "before",
                  "toString": "after"
                }
              ]
            }
            """);

        return $$"""
            {
              "key": "PROJ-12",
              "fields": { "summary": "Login fails with a 401" },
              "changelog": { "total": {{count}}, "histories": [{{string.Join(",", histories)}}] }
            }
            """;
    }

    private static IResponseBuilder Json(string body) =>
        Response.Create().WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(body);

    private IRequestMessage SingleRequest() =>
        _jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull().RequestMessage.ShouldNotBeNull();

    private void StubIssue(IResponseBuilder response) =>
        _jira.Given(Request.Create()
                .WithPath(new WildcardMatcher("/rest/api/2/issue/*")).UsingGet())
            .RespondWith(response);
}
