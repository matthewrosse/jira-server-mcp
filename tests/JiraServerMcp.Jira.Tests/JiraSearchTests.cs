using System.Net;
using System.Text.Json;
using JiraServerMcp.Jira.Errors;
using Microsoft.Extensions.DependencyInjection;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// The search half of the client against an HTTP double: what Jira is asked, and what comes back.
/// </summary>
public sealed class JiraSearchTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    private const string SearchPayload = """
        {
          "startAt": 0,
          "maxResults": 25,
          "total": 128,
          "issues": [
            {
              "key": "PROJ-12",
              "fields": {
                "summary": "Login fails with a 401",
                "status": { "name": "In Progress" },
                "issuetype": { "name": "Bug" },
                "labels": ["api", "backend"]
              }
            }
          ]
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
    public async Task A_search_reports_the_page_and_the_total_jira_gave()
    {
        StubSearch(Json(SearchPayload));

        var page = await SearchAsync("project = PROJ");

        page.StartAt.ShouldBe(0);
        page.MaxResults.ShouldBe(25);
        page.Total.ShouldBe(128);

        var issue = page.Issues.ShouldHaveSingleItem();

        issue.Key.ShouldBe("PROJ-12");
        issue.Fields["summary"].GetString().ShouldBe("Login fails with a 401");
        issue.Fields["status"].GetProperty("name").GetString().ShouldBe("In Progress");
        issue.Fields["labels"].EnumerateArray().Select(label => label.GetString())
            .ShouldBe(["api", "backend"]);
    }

    [Fact]
    public async Task The_request_names_the_projected_fields_and_the_page_explicitly()
    {
        StubSearch(Json(SearchPayload));

        await SearchAsync("project = PROJ", startAt: 50, maxResults: 25, ["summary", "status"]);

        var request = SingleRequest();

        request.Method.ShouldBe("GET");
        request.Path.ShouldBe("/rest/api/2/search");

        var query = request.Query.ShouldNotBeNull();

        query["jql"].ShouldHaveSingleItem().ShouldBe("project = PROJ");
        query["startAt"].ShouldHaveSingleItem().ShouldBe("50");
        query["maxResults"].ShouldHaveSingleItem().ShouldBe("25");
        query["fields"].ShouldHaveSingleItem().ShouldBe("summary,status");
    }

    [Fact]
    public async Task A_jql_too_long_for_a_url_falls_back_to_the_post_form()
    {
        StubSearchPost(Json(SearchPayload));

        var jql = "project = PROJ AND summary ~ \"" + new string('x', 2_000) + "\"";

        var page = await SearchAsync(jql);

        page.Total.ShouldBe(128);

        var request = SingleRequest();

        request.Method.ShouldBe("POST");
        request.Path.ShouldBe("/rest/api/2/search");

        var body = JsonDocument.Parse(request.Body.ShouldNotBeNull()).RootElement;

        body.GetProperty("jql").GetString().ShouldBe(jql);
        body.GetProperty("startAt").GetInt32().ShouldBe(0);
        body.GetProperty("maxResults").GetInt32().ShouldBe(25);
        body.GetProperty("fields").EnumerateArray().Select(field => field.GetString())
            .ShouldBe(["summary", "status"]);
    }

    [Fact]
    public async Task A_jql_that_fits_in_a_url_is_never_posted()
    {
        StubSearch(Json(SearchPayload));

        await SearchAsync("project = PROJ AND assignee = currentUser() ORDER BY updated DESC");

        SingleRequest().Method.ShouldBe("GET");
    }

    [Fact]
    public async Task A_busy_jira_is_asked_again_even_when_the_search_went_out_as_a_post()
    {
        // The single exception to "a POST is never retried": this one reads, and repeating it
        // creates nothing. The write endpoints keep the rule, which JiraResilienceTests asserts.
        _jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingPost())
            .InScenario("busy").WillSetStateTo("recovered")
            .RespondWith(Response.Create().WithStatusCode(503));

        _jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingPost())
            .InScenario("busy").WhenStateIs("recovered")
            .RespondWith(Json(SearchPayload));

        var page = await SearchAsync(
            "project = PROJ AND summary ~ \"" + new string('x', 2_000) + "\"");

        page.Total.ShouldBe(128);
        _jira.LogEntries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_rejected_jql_carries_jiras_own_message()
    {
        StubSearch(Response.Create().WithStatusCode(400)
            .WithHeader("Content-Type", "application/json")
            .WithBody("""
                {"errorMessages":["Field 'nosuchfield' does not exist."],"errors":{}}
                """));

        var exception = await Should.ThrowAsync<JiraApiException>(
            () => SearchAsync("nosuchfield = 1"));

        exception.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        exception.ErrorMessages.ShouldContain("Field 'nosuchfield' does not exist.");
    }

    private Task<JiraSearchPage> SearchAsync(
        string jql,
        int startAt = 0,
        int maxResults = 25,
        IReadOnlyList<string>? fields = null) =>
        CreateClient().SearchAsync(
            jql,
            startAt,
            maxResults,
            fields ?? ["summary", "status"],
            TestContext.Current.CancellationToken);

    private static IResponseBuilder Json(string body) =>
        Response.Create().WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(body);

    private IRequestMessage SingleRequest() =>
        _jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull().RequestMessage.ShouldNotBeNull();

    private void StubSearch(IResponseBuilder response) =>
        _jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
            .RespondWith(response);

    private void StubSearchPost(IResponseBuilder response) =>
        _jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingPost())
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
