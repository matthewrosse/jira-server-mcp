using System.Text.Json;
using JiraServerMcp.Jira.Errors;
using Microsoft.Extensions.DependencyInjection;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// Commenting and logging work against an HTTP double: the body Jira is sent, the small answer that
/// comes back, and that neither is ever sent twice.
/// </summary>
public sealed class JiraCommentWorklogTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    private const string CommentPayload = """
        {
          "id": "10101",
          "created": "2026-08-16T10:00:00.000+0000",
          "body": "It has done it twice today."
        }
        """;

    private const string WorklogPayload = """
        {
          "id": "10200",
          "timeSpent": "3h 30m",
          "started": "2026-08-16T09:00:00.000+0000"
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
    public async Task A_comment_is_posted_as_a_body_and_comes_back_as_an_identifier_and_a_timestamp()
    {
        StubComment(201, CommentPayload);

        var added = await CreateClient().AddCommentAsync(
            "PROJ-42",
            "It has done it twice today.",
            TestContext.Current.CancellationToken);

        added.Id.ShouldBe("10101");
        added.Created.ShouldBe("2026-08-16T10:00:00.000+0000");

        var request = SingleRequest();

        request.Method.ShouldBe("POST");
        request.Path.ShouldBe("/rest/api/2/issue/PROJ-42/comment");

        Body(request).GetProperty("body").GetString().ShouldBe("It has done it twice today.");
    }

    [Fact]
    public async Task A_comment_jira_could_not_answer_is_never_sent_twice()
    {
        StubComment(503, "");

        await Should.ThrowAsync<JiraApiException>(
            () => CreateClient().AddCommentAsync(
                "PROJ-42",
                "It has done it twice today.",
                TestContext.Current.CancellationToken));

        _jira.LogEntries.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Logged_work_carries_jiras_own_duration_and_comes_back_with_what_jira_recorded()
    {
        StubWorklog(201, WorklogPayload);

        var logged = await CreateClient().AddWorklogAsync(
            "PROJ-42",
            "3h 30m",
            started: null,
            comment: null,
            TestContext.Current.CancellationToken);

        logged.Id.ShouldBe("10200");
        logged.TimeSpent.ShouldBe("3h 30m");

        var request = SingleRequest();

        request.Method.ShouldBe("POST");
        request.Path.ShouldBe("/rest/api/2/issue/PROJ-42/worklog");

        var body = Body(request);

        body.GetProperty("timeSpent").GetString().ShouldBe("3h 30m");
        body.TryGetProperty("started", out _).ShouldBeFalse();
        body.TryGetProperty("comment", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_start_time_and_a_comment_are_sent_where_they_were_given()
    {
        StubWorklog(201, WorklogPayload);

        await CreateClient().AddWorklogAsync(
            "PROJ-42",
            "3h 30m",
            "2026-08-16T09:00:00.000+0000",
            "Tracked down the 500s.",
            TestContext.Current.CancellationToken);

        var body = Body(SingleRequest());

        body.GetProperty("started").GetString().ShouldBe("2026-08-16T09:00:00.000+0000");
        body.GetProperty("comment").GetString().ShouldBe("Tracked down the 500s.");
    }

    [Fact]
    public async Task A_worklog_jira_could_not_answer_is_never_sent_twice()
    {
        StubWorklog(503, "");

        await Should.ThrowAsync<JiraApiException>(
            () => CreateClient().AddWorklogAsync(
                "PROJ-42",
                "3h 30m",
                started: null,
                comment: null,
                TestContext.Current.CancellationToken));

        _jira.LogEntries.Count().ShouldBe(1);
    }

    private static JsonElement Body(IRequestMessage request) =>
        JsonDocument.Parse(request.Body ?? string.Empty).RootElement;

    private void StubComment(int status, string payload) =>
        Stub("/rest/api/2/issue/PROJ-42/comment", status, payload);

    private void StubWorklog(int status, string payload) =>
        Stub("/rest/api/2/issue/PROJ-42/worklog", status, payload);

    private void Stub(string path, int status, string payload) =>
        _jira.Given(Request.Create().WithPath(path).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(status)
                .WithHeader("Content-Type", "application/json")
                .WithBody(payload));

    private IRequestMessage SingleRequest() =>
        _jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull().RequestMessage.ShouldNotBeNull();

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
