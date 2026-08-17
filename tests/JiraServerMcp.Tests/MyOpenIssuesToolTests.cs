using System.Diagnostics;
using System.Net;
using System.Text.Json;
using JiraServerMcp.Jira;
using JiraServerMcp.Profiles;
using JiraServerMcp.Tools;
using ModelContextProtocol.Protocol;

namespace JiraServerMcp.Tests;

/// <summary>
/// The JQL <c>jira_my_open_issues</c> builds, and the project-key grammar guarding the
/// interpolation seam — both provable without a real Jira behind them.
/// </summary>
public sealed class MyOpenIssuesToolTests
{
    private const string BaseJql = "assignee = currentUser() AND resolution = Unresolved ORDER BY updated DESC";

    [Fact]
    public async Task A_project_key_that_fails_the_grammar_is_rejected_before_any_request_is_made()
    {
        var handler = new RecordingHandler();
        var tool = Tool(handler);

        var result = await tool.MyOpenIssuesAsync(
            project: "PROJ; DROP TABLE",
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true);
        Text(result).ShouldContain("not a valid Jira project key");
        Text(result).ShouldContain("jira_search");
        handler.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1PROJ")]
    [InlineData("PROJ KEY")]
    [InlineData("PROJ-1")]
    public async Task Project_keys_outside_the_grammar_are_rejected(string project)
    {
        var handler = new RecordingHandler();
        var tool = Tool(handler);

        var result = await tool.MyOpenIssuesAsync(
            project: project,
            cancellationToken: TestContext.Current.CancellationToken);

        Text(result).ShouldContain("not a valid Jira project key");
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_no_project_the_jql_is_the_bare_canned_query()
    {
        var handler = new RecordingHandler();
        var tool = Tool(handler);

        var result = await tool.MyOpenIssuesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Text(result).ShouldContain($"jql: {BaseJql}");
        JqlFrom(handler.Requests.ShouldHaveSingleItem()).ShouldBe(BaseJql);
    }

    [Fact]
    public async Task A_valid_project_key_is_prefixed_onto_the_canned_query()
    {
        var handler = new RecordingHandler();
        var tool = Tool(handler);

        var result = await tool.MyOpenIssuesAsync(
            project: "PROJ",
            cancellationToken: TestContext.Current.CancellationToken);

        var expected = $"project = PROJ AND {BaseJql}";

        Text(result).ShouldContain($"jql: {expected}");
        JqlFrom(handler.Requests.ShouldHaveSingleItem()).ShouldBe(expected);
    }

    [Fact]
    public async Task A_jira_that_never_answers_names_jira_search_as_a_narrower_fallback()
    {
        var tool = Tool(new NeverAnswers(), TimeSpan.FromMilliseconds(200));

        var result = await tool.MyOpenIssuesAsync(cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);
        Text(result).ShouldContain("did not answer");
        Text(result).ShouldContain("jira_search");
    }

    private static MyOpenIssuesTool Tool(HttpMessageHandler handler, TimeSpan? timeout = null) =>
        new(
            new JiraClient(new HttpClient(handler)
            {
                BaseAddress = new Uri("https://jira.example.com", UriKind.Absolute),
                Timeout = timeout ?? TimeSpan.FromSeconds(30),
            }),
            new ServedProfile("work"));

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;

    private static string JqlFrom(Uri request) =>
        Uri.UnescapeDataString(
            request.Query.TrimStart('?').Split('&')
                .Select(pair => pair.Split('=', 2))
                .Single(pair => pair[0] == "jql")[1]);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);

            var payload = JsonSerializer.Serialize(new
            {
                startAt = 0,
                maxResults = 25,
                total = 0,
                issues = Array.Empty<object>(),
            });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class NeverAnswers : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);

            throw new UnreachableException();
        }
    }
}
