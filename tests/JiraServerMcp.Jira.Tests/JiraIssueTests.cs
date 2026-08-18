using System.Net;
using JiraServerMcp.Jira.Errors;
using Microsoft.Extensions.DependencyInjection;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// The single-issue read against an HTTP double: what Jira is asked for once expansions are in
/// play, and how the sections it answers with are read back.
/// </summary>
public sealed class JiraIssueTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    /// <summary>
    /// An issue as Jira 8 answers it when every section was asked for in the one call: the
    /// projection and the three collection fields under <c>fields</c>, and the two expansions
    /// beside it at the top level.
    /// </summary>
    private const string IssuePayload = """
        {
          "key": "PROJ-12",
          "fields": {
            "summary": "Login fails with a 401",
            "status": { "name": "In Progress" },
            "issuetype": { "name": "Bug" },
            "comment": {
              "startAt": 0,
              "maxResults": 2,
              "total": 7,
              "comments": [
                {
                  "id": "10100",
                  "author": { "name": "ada", "displayName": "Ada Lovelace" },
                  "body": "Reproduced on staging.",
                  "created": "2026-08-01T09:15:00.000+0000"
                },
                {
                  "id": "10101",
                  "author": { "name": "jsmith", "displayName": "Jane Smith" },
                  "body": "Token expiry is off by one.",
                  "created": "2026-08-02T11:30:00.000+0000"
                }
              ]
            },
            "issuelinks": [
              {
                "id": "20100",
                "type": { "name": "Blocks", "inward": "is blocked by", "outward": "blocks" },
                "outwardIssue": {
                  "key": "PROJ-13",
                  "fields": { "summary": "Rotate the signing key" }
                }
              },
              {
                "id": "20101",
                "type": { "name": "Blocks", "inward": "is blocked by", "outward": "blocks" },
                "inwardIssue": {
                  "key": "PROJ-11",
                  "fields": { "summary": "Upgrade the auth library" }
                }
              }
            ],
            "worklog": {
              "startAt": 0,
              "maxResults": 1,
              "total": 4,
              "worklogs": [
                {
                  "author": { "name": "ada", "displayName": "Ada Lovelace" },
                  "timeSpent": "3h 30m",
                  "started": "2026-08-01T08:00:00.000+0000"
                }
              ]
            }
          },
          "transitions": [
            {
              "id": "21",
              "name": "Start Progress",
              "to": { "name": "In Progress" },
              "fields": {}
            },
            {
              "id": "31",
              "name": "Resolve Issue",
              "to": { "name": "Resolved" },
              "fields": {
                "resolution": { "name": "Resolution", "required": true },
                "assignee": { "name": "Assignee", "required": false }
              }
            }
          ],
          "changelog": {
            "startAt": 0,
            "maxResults": 2,
            "total": 9,
            "histories": [
              {
                "id": "30100",
                "author": { "name": "ada", "displayName": "Ada Lovelace" },
                "created": "2026-08-01T09:00:00.000+0000",
                "items": [
                  {
                    "field": "status",
                    "fromString": "Open",
                    "toString": "In Progress"
                  }
                ]
              },
              {
                "id": "30101",
                "author": { "name": "jsmith", "displayName": "Jane Smith" },
                "created": "2026-08-02T10:00:00.000+0000",
                "items": [
                  {
                    "field": "assignee",
                    "fromString": null,
                    "toString": "Ada Lovelace"
                  }
                ]
              }
            ]
          }
        }
        """;

    /// <summary>An issue with nothing but the projection, as a call with no expansions gets.</summary>
    private const string BareIssuePayload = """
        {
          "key": "PROJ-12",
          "fields": {
            "summary": "Login fails with a 401",
            "status": { "name": "In Progress" }
          }
        }
        """;

    private readonly WireMockServer _jira = WireMockServer.Start();
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _jira.Stop();
    }

    [Fact]
    public async Task An_issue_read_asks_for_the_key_and_the_projected_fields()
    {
        StubIssue(Json(BareIssuePayload));

        var issue = await GetIssueAsync("PROJ-12", ["summary", "status"]);

        issue.Key.ShouldBe("PROJ-12");
        issue.Fields["summary"].GetString().ShouldBe("Login fails with a 401");

        var request = SingleRequest();

        request.Method.ShouldBe("GET");
        request.Path.ShouldBe("/rest/api/2/issue/PROJ-12");
        request.Query.ShouldNotBeNull()["fields"].ShouldHaveSingleItem().ShouldBe("summary,status");
    }

    [Fact]
    public async Task A_read_with_no_expansions_asks_jira_to_expand_nothing()
    {
        StubIssue(Json(BareIssuePayload));

        await GetIssueAsync("PROJ-12", ["summary"]);

        SingleRequest().Query.ShouldNotBeNull().ContainsKey("expand").ShouldBeFalse();
    }

    [Fact]
    public async Task A_read_with_no_expansions_carries_no_sections()
    {
        StubIssue(Json(BareIssuePayload));

        var issue = await GetIssueAsync("PROJ-12", ["summary"]);

        issue.Transitions.ShouldBeEmpty();
        issue.Changelog.ShouldBeNull();
        issue.Comments.ShouldBeNull();
        issue.Links.ShouldBeEmpty();
        issue.Worklogs.ShouldBeNull();
    }

    [Fact]
    public async Task Every_section_is_asked_for_in_one_request()
    {
        StubIssue(Json(IssuePayload));

        await GetIssueAsync(
            "PROJ-12",
            ["summary", "comment", "issuelinks", "worklog"],
            ["transitions.fields", "changelog"]);

        _jira.LogEntries.Count.ShouldBe(1);

        var query = SingleRequest().Query.ShouldNotBeNull();

        query["fields"].ShouldHaveSingleItem().ShouldBe("summary,comment,issuelinks,worklog");
        query["expand"].ShouldHaveSingleItem().ShouldBe("transitions.fields,changelog");
    }

    [Fact]
    public async Task An_issue_key_that_would_not_survive_a_url_reaches_jira_whole()
    {
        StubIssue(Json(BareIssuePayload));

        // Not a key Jira would ever issue, but well within what a model will invent, and
        // unescaped it would take the query string with it.
        await GetIssueAsync("PROJ 12", ["summary"]);

        var request = SingleRequest();

        request.Path.ShouldBe("/rest/api/2/issue/PROJ 12");
        request.Query.ShouldNotBeNull()["fields"].ShouldHaveSingleItem().ShouldBe("summary");
    }

    [Fact]
    public async Task The_collection_fields_are_read_as_sections_rather_than_left_in_the_projection()
    {
        StubIssue(Json(IssuePayload));

        var issue = await GetIssueAsync("PROJ-12", ["summary", "comment", "issuelinks", "worklog"]);

        // Left in place they would render as three unreadable JSON blobs in the field list.
        issue.Fields.ContainsKey("comment").ShouldBeFalse();
        issue.Fields.ContainsKey("issuelinks").ShouldBeFalse();
        issue.Fields.ContainsKey("worklog").ShouldBeFalse();
        issue.Fields.ContainsKey("summary").ShouldBeTrue();
    }

    [Fact]
    public async Task Comments_carry_their_author_timestamp_and_body_with_jiras_own_total()
    {
        StubIssue(Json(IssuePayload));

        var comments = (await GetIssueAsync("PROJ-12", ["comment"])).Comments.ShouldNotBeNull();

        comments.Total.ShouldBe(7);
        comments.Comments.Count.ShouldBe(2);

        var first = comments.Comments[0];

        first.Author.ShouldBe("Ada Lovelace");
        first.Created.ShouldBe("2026-08-01T09:15:00.000+0000");
        first.Body.ShouldBe("Reproduced on staging.");
    }

    [Fact]
    public async Task Transitions_carry_their_name_and_the_fields_their_screen_demands()
    {
        StubIssue(Json(IssuePayload));

        var transitions = (await GetIssueAsync("PROJ-12", ["summary"], ["transitions.fields"]))
            .Transitions;

        transitions.Count.ShouldBe(2);

        transitions[0].Name.ShouldBe("Start Progress");
        transitions[0].ToStatus.ShouldBe("In Progress");
        transitions[0].Fields.ShouldBeEmpty();

        var resolve = transitions[1];

        resolve.Name.ShouldBe("Resolve Issue");
        resolve.ToStatus.ShouldBe("Resolved");

        var resolution = resolve.Fields.Single(field => field.Id is "resolution");

        resolution.Name.ShouldBe("Resolution");
        resolution.Required.ShouldBeTrue();

        resolve.Fields.Single(field => field.Id is "assignee").Required.ShouldBeFalse();
    }

    [Fact]
    public async Task Changelog_groups_carry_their_author_timestamp_and_each_field_that_moved()
    {
        StubIssue(Json(IssuePayload));

        var changelog = (await GetIssueAsync("PROJ-12", ["summary"], ["changelog"]))
            .Changelog.ShouldNotBeNull();

        changelog.Total.ShouldBe(9);

        var first = changelog.Histories[0];

        first.Author.ShouldBe("Ada Lovelace");
        first.Created.ShouldBe("2026-08-01T09:00:00.000+0000");

        var item = first.Items.ShouldHaveSingleItem();

        item.Field.ShouldBe("status");
        item.From.ShouldBe("Open");
        item.To.ShouldBe("In Progress");
    }

    [Fact]
    public async Task Links_carry_the_direction_jira_worded_and_the_issue_on_the_other_end()
    {
        StubIssue(Json(IssuePayload));

        var links = (await GetIssueAsync("PROJ-12", ["issuelinks"])).Links;

        links.Count.ShouldBe(2);

        // Jira words the direction differently at each end, and only that wording says which end
        // this issue is on.
        links[0].Relation.ShouldBe("blocks");
        links[0].Key.ShouldBe("PROJ-13");
        links[0].Summary.ShouldBe("Rotate the signing key");

        links[1].Relation.ShouldBe("is blocked by");
        links[1].Key.ShouldBe("PROJ-11");
        links[1].Summary.ShouldBe("Upgrade the auth library");
    }

    [Fact]
    public async Task Worklogs_carry_their_author_duration_and_start_time_with_jiras_own_total()
    {
        StubIssue(Json(IssuePayload));

        var worklogs = (await GetIssueAsync("PROJ-12", ["worklog"])).Worklogs.ShouldNotBeNull();

        worklogs.Total.ShouldBe(4);

        var entry = worklogs.Worklogs.ShouldHaveSingleItem();

        entry.Author.ShouldBe("Ada Lovelace");
        entry.TimeSpent.ShouldBe("3h 30m");
        entry.Started.ShouldBe("2026-08-01T08:00:00.000+0000");
    }

    [Fact]
    public async Task An_issue_jira_will_not_show_fails_naming_the_issue_endpoint()
    {
        StubIssue(Response.Create().WithStatusCode(404)
            .WithHeader("Content-Type", "application/json")
            .WithBody("""
                {"errorMessages":["Issue does not exist or you do not have permission to see it."],"errors":{}}
                """));

        var exception = await Should.ThrowAsync<JiraApiException>(
            () => GetIssueAsync("PROJ-12", ["summary"]));

        exception.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // JiraToolError keys the two-meanings message off this endpoint.
        exception.Endpoint.ShouldContain("/rest/api/2/issue/");
    }

    private Task<Models.JiraIssueDetail> GetIssueAsync(
        string key,
        IReadOnlyList<string> fields,
        IReadOnlyList<string>? expand = null,
        bool remoteLinks = false) =>
        CreateClient().GetIssueAsync(
            key,
            fields,
            expand ?? [],
            remoteLinks,
            TestContext.Current.CancellationToken);

    private static IResponseBuilder Json(string body) =>
        Response.Create().WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(body);

    private IRequestMessage SingleRequest() =>
        _jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull().RequestMessage.ShouldNotBeNull();

    private void StubIssue(IResponseBuilder response) =>
        _jira.Given(Request.Create().WithPath(new WireMock.Matchers.WildcardMatcher("/rest/api/2/issue/*")).UsingGet())
            .RespondWith(response);

    private JiraClient CreateClient()
    {
        var services = new ServiceCollection();

        services.AddJiraClient();
        services.Configure<JiraClientOptions>(options =>
        {
            options.BaseUrl = new Uri(_jira.Url!, UriKind.Absolute);
            options.PersonalAccessToken = Token;
        });

        var provider = services.BuildServiceProvider();

        _providers.Add(provider);

        return provider.GetRequiredService<JiraClient>();
    }
}
