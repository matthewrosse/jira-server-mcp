using System.Collections.Concurrent;
using System.Diagnostics;
using JiraServerMcp.Jira.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// A log is the one place a bearer token most easily escapes to, so an authenticated round trip
/// is captured whole — every level, every category — and searched for it.
/// </summary>
public sealed class JiraLoggingTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    private readonly WireMockServer _jira = WireMockServer.Start();
    private readonly CapturedLog _log = new();
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
    public async Task A_full_log_of_an_authenticated_round_trip_holds_no_part_of_the_token()
    {
        StubMyself(200);

        await Send();

        _log.Lines.ShouldNotBeEmpty();

        foreach (var line in _log.Lines)
        {
            line.ShouldNotContain(Token);
            line.ShouldNotContain("Bearer");
            line.ShouldNotContain("Authorization");
        }
    }

    [Fact]
    public async Task A_round_trip_is_logged_with_its_method_endpoint_status_and_elapsed_time()
    {
        StubMyself(200);

        await Send();

        var line = _log.Lines.ShouldHaveSingleItem();

        line.ShouldContain("GET");
        line.ShouldContain("/rest/api/2/myself");
        line.ShouldContain("200");
        line.ShouldContain("ms");
    }

    [Fact]
    public async Task A_failure_is_logged_with_the_status_jira_returned()
    {
        StubMyself(403);

        await Send();

        _log.Lines.ShouldHaveSingleItem().ShouldContain("403");
    }

    [Fact]
    public async Task The_body_jira_sent_is_never_logged()
    {
        _jira
            .Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"displayName":"a name only this response knows"}"""));

        await Send();

        _log.Lines.ShouldHaveSingleItem().ShouldNotContain("a name only this response knows");
    }

    [Fact]
    public async Task A_call_that_runs_out_of_time_is_logged_too()
    {
        // The one failure with nothing to show for it: no status, no exception from Jira. Without
        // a line here, a hung Jira leaves the log silent about the request that never came back.
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_log));
        using var handler = new JiraRequestLoggingHandler(
            loggerFactory.CreateLogger<JiraRequestLoggingHandler>())
        {
            InnerHandler = new NeverAnswers(),
        };

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://jira.example.com", UriKind.Absolute),
            Timeout = TimeSpan.FromMilliseconds(200),
        };

        await Should.ThrowAsync<TaskCanceledException>(
            () => client.GetAsync("rest/api/2/myself", TestContext.Current.CancellationToken));

        var line = _log.Lines.ShouldHaveSingleItem();

        line.ShouldContain("GET");
        line.ShouldContain("/rest/api/2/myself");
        line.ShouldContain("ms");
    }

    private void StubMyself(int status) =>
        _jira
            .Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status).WithBody("{}"));

    private async Task Send()
    {
        using var response = await CreateClient()
            .GetAsync("rest/api/2/myself", TestContext.Current.CancellationToken);
    }

    private HttpClient CreateClient()
    {
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            // Everything the pipeline is willing to say, including what the HTTP client factory
            // would log at trace level.
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(_log);
        });

        services.AddJiraClient();
        services.Configure<JiraClientOptions>(options =>
        {
            options.BaseUrl = new Uri(_jira.Url!, UriKind.Absolute);
            options.PersonalAccessToken = Token;
        });

        var provider = services.BuildServiceProvider();

        _providers.Add(provider);

        return provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(JiraClient));
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

    private sealed class CapturedLog : ILoggerProvider, ILogger
    {
        public ConcurrentBag<string> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Lines.Add($"{formatter(state, exception)} {exception}");

        public void Dispose()
        {
        }
    }
}
