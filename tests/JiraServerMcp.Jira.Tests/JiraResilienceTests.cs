using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// Retry behaviour asserted from the faked Jira's received-request log rather than from any
/// counter inside the pipeline: what matters is how many times Jira was actually asked.
/// </summary>
public sealed class JiraResilienceTests : IDisposable
{
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

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(429)]
    [InlineData(408)]
    public async Task A_read_is_retried_three_times(int status)
    {
        Stub(Request.Create().WithPath("/read").UsingGet(), Response.Create().WithStatusCode(status));

        using var response = await Send(HttpMethod.Get, "read");

        ReceivedRequests().Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_head_request_is_retried_like_a_read()
    {
        Stub(Request.Create().WithPath("/read").UsingHead(), Response.Create().WithStatusCode(503));

        using var response = await Send(HttpMethod.Head, "read");

        ReceivedRequests().Count.ShouldBe(3);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task A_write_is_never_retried(string method)
    {
        Stub(
            Request.Create().WithPath("/write").UsingMethod(method),
            Response.Create().WithStatusCode(503));

        using var response = await Send(new HttpMethod(method), "write");

        // A retried POST would silently create the same issue twice.
        ReceivedRequests().Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task A_status_jira_will_not_recover_from_is_not_retried(int status)
    {
        Stub(Request.Create().WithPath("/read").UsingGet(), Response.Create().WithStatusCode(status));

        using var response = await Send(HttpMethod.Get, "read");

        ReceivedRequests().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Retrying_stops_as_soon_as_jira_answers()
    {
        _jira
            .Given(Request.Create().WithPath("/read").UsingGet())
            .InScenario("recovers")
            .WillSetStateTo("recovered")
            .RespondWith(Response.Create().WithStatusCode(503));

        _jira
            .Given(Request.Create().WithPath("/read").UsingGet())
            .InScenario("recovers")
            .WhenStateIs("recovered")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        using var response = await Send(HttpMethod.Get, "read");

        response.IsSuccessStatusCode.ShouldBeTrue();
        ReceivedRequests().Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_transport_failure_on_a_read_is_retried()
    {
        // Nothing is listening, so every attempt fails before Jira could log it. The elapsed time
        // is the evidence: a single attempt would fail immediately, with nothing to wait for.
        var client = CreateClient(new Uri($"http://127.0.0.1:{ClosedPort()}", UriKind.Absolute));
        var stopwatch = Stopwatch.StartNew();

        await Should.ThrowAsync<HttpRequestException>(
            () => client.GetAsync("read", TestContext.Current.CancellationToken));

        stopwatch.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(600));
    }

    [Fact]
    public async Task Attempts_are_spaced_by_a_growing_backoff()
    {
        Stub(Request.Create().WithPath("/read").UsingGet(), Response.Create().WithStatusCode(503));

        var stopwatch = Stopwatch.StartNew();

        using var response = await Send(HttpMethod.Get, "read");

        // Two waits between three attempts, the second longer than the first.
        stopwatch.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(600));
    }

    [Fact]
    public async Task Retry_after_in_seconds_is_honoured()
    {
        StubRetryAfter("1");

        var stopwatch = Stopwatch.StartNew();

        using var response = await Send(HttpMethod.Get, "read");

        response.IsSuccessStatusCode.ShouldBeTrue();
        stopwatch.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public async Task Retry_after_as_an_http_date_is_honoured()
    {
        StubRetryAfter(DateTimeOffset.UtcNow.AddSeconds(2)
            .ToString("r", CultureInfo.InvariantCulture));

        var stopwatch = Stopwatch.StartNew();

        using var response = await Send(HttpMethod.Get, "read");

        response.IsSuccessStatusCode.ShouldBeTrue();
        stopwatch.Elapsed.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public void The_whole_call_including_retries_is_bounded_by_thirty_seconds()
    {
        CreateClient().Timeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// A 429 answered with a <c>Retry-After</c>, then a success, so the delay the header asks for
    /// is the only thing standing between the two received requests.
    /// </summary>
    private void StubRetryAfter(string retryAfter)
    {
        _jira
            .Given(Request.Create().WithPath("/read").UsingGet())
            .InScenario("throttled")
            .WillSetStateTo("released")
            .RespondWith(Response.Create()
                .WithStatusCode(429)
                .WithHeader("Retry-After", retryAfter));

        _jira
            .Given(Request.Create().WithPath("/read").UsingGet())
            .InScenario("throttled")
            .WhenStateIs("released")
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));
    }

    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        listener.Stop();

        return port;
    }

    private void Stub(IRequestBuilder request, IResponseBuilder response) =>
        _jira.Given(request).RespondWith(response);

    private IReadOnlyList<WireMock.Logging.ILogEntry> ReceivedRequests() => [.. _jira.LogEntries];

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path);

        return await CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private HttpClient CreateClient(Uri? baseUrl = null)
    {
        var services = new ServiceCollection();

        services.AddJiraClient();
        services.Configure<JiraClientOptions>(options =>
        {
            options.BaseUrl = baseUrl ?? new Uri(_jira.Url!, UriKind.Absolute);
            options.PersonalAccessToken = "s3cr3t-personal-access-token";
        });

        var provider = services.BuildServiceProvider();

        _providers.Add(provider);

        return provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(JiraClient));
    }
}
