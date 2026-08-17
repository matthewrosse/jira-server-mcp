using System.Net;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;
using Microsoft.Extensions.DependencyInjection;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Types;
using WireMock.Util;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// The bulk issue read's fan-out against an HTTP double: the concurrency cap, one key's failure
/// leaving the rest untouched, and a profile-level auth failure surfacing as itself rather than as
/// a per-key line.
/// </summary>
public sealed class JiraBulkIssueTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

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
    public async Task No_more_than_five_requests_are_in_flight_at_once()
    {
        var inFlight = 0;
        var maxObserved = 0;
        var maxLock = new object();

        _jira.Given(Request.Create().WithPath("/rest/api/2/issue/*").UsingGet())
            .RespondWith(Response.Create().WithCallback(request =>
            {
                var current = Interlocked.Increment(ref inFlight);

                lock (maxLock)
                {
                    maxObserved = Math.Max(maxObserved, current);
                }

                Thread.Sleep(60);

                Interlocked.Decrement(ref inFlight);

                var key = request.Path.Split('/').Last();

                return new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData
                    {
                        BodyAsJson = new { key, fields = new { summary = "x" } },
                        DetectedBodyType = BodyType.Json,
                    },
                };
            }));

        var keys = Enumerable.Range(1, 20).Select(number => $"PROJ-{number}").ToArray();

        var results = await GetIssuesAsync(keys);

        results.Count.ShouldBe(20);
        results.ShouldAllBe(result => result.Succeeded);
        maxObserved.ShouldBeLessThanOrEqualTo(5);
        maxObserved.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task One_keys_404_does_not_sink_the_others()
    {
        StubIssue("PROJ-1", Json("PROJ-1"));
        StubIssue("PROJ-2", Json("PROJ-2"));
        StubIssue(
            "PROJ-404",
            Response.Create().WithStatusCode(404)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"errorMessages":["Issue Does Not Exist"],"errors":{}}"""));

        var results = await GetIssuesAsync(["PROJ-1", "PROJ-404", "PROJ-2"]);

        results.Single(result => result.Key is "PROJ-1").Succeeded.ShouldBeTrue();
        results.Single(result => result.Key is "PROJ-2").Succeeded.ShouldBeTrue();

        var failed = results.Single(result => result.Key is "PROJ-404");

        failed.Succeeded.ShouldBeFalse();
        failed.Failure.ShouldBeOfType<JiraApiException>().StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_401_surfaces_as_itself_rather_than_as_a_per_key_line()
    {
        StubIssue("PROJ-1", Json("PROJ-1"));
        StubIssue(
            "PROJ-2",
            Response.Create().WithStatusCode(401)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"errorMessages":["Client must be authenticated"],"errors":{}}"""));

        var exception = await Should.ThrowAsync<JiraApiException>(
            () => GetIssuesAsync(["PROJ-1", "PROJ-2"]));

        exception.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private Task<IReadOnlyList<BulkIssueResult>> GetIssuesAsync(IReadOnlyList<string> keys) =>
        CreateClient().GetIssuesAsync(keys, ["summary"], [], TestContext.Current.CancellationToken);

    private static IResponseBuilder Json(string key) =>
        Response.Create().WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody($$"""{ "key": "{{key}}", "fields": { "summary": "x" } }""");

    private void StubIssue(string key, IResponseBuilder response) =>
        _jira.Given(Request.Create().WithPath($"/rest/api/2/issue/{key}").UsingGet())
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
