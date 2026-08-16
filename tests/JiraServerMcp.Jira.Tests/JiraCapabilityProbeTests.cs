using System.Net;
using JiraServerMcp.Jira.Capabilities;
using JiraServerMcp.Jira.Errors;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// The capability probe against an HTTP double: what it asks Jira, and what it makes of the
/// answers. The software API's absence is normal, and it is a status code — Phase 0 found the body
/// of that 404 to be HTML, so nothing here may depend on reading it.
/// </summary>
public sealed class JiraCapabilityProbeTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    private const string ServerInfoPayload = """
        {
          "baseUrl": "http://localhost:8080",
          "version": "8.20.7",
          "versionNumbers": [ 8, 20, 7 ],
          "deploymentType": "Server",
          "buildNumber": 820007
        }
        """;

    private const string BoardPayload = """
        {
          "maxResults": 1,
          "startAt": 0,
          "isLast": false,
          "values": [ { "id": 1, "name": "PZ board", "type": "scrum" } ]
        }
        """;

    /// <summary>
    /// What Jira Core answers at the software API: a 404 whose body is HTML, not JSON.
    /// </summary>
    private const string CoreNotFoundPayload =
        "<html><head><title>Oops</title></head><body>Not found</body></html>";

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
    public async Task The_probe_records_the_version_the_deployment_type_and_a_licensed_jira_software()
    {
        StubServerInfo();
        StubBoard(HttpStatusCode.OK, BoardPayload, "application/json");

        var capabilities = await ProbeAsync();

        capabilities.Version.ShouldBe("8.20.7");
        capabilities.DeploymentType.ShouldBe("Server");
        capabilities.SoftwareLicensed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_jira_core_instance_answers_the_software_api_with_a_404_and_is_recorded_unlicensed()
    {
        StubServerInfo();
        StubBoard(HttpStatusCode.NotFound, CoreNotFoundPayload, "text/html");

        var capabilities = await ProbeAsync();

        capabilities.Version.ShouldBe("8.20.7");
        capabilities.SoftwareLicensed.ShouldBeFalse();
    }

    [Fact]
    public async Task The_probe_asks_server_info_and_the_smallest_possible_board_page()
    {
        StubServerInfo();
        StubBoard(HttpStatusCode.OK, BoardPayload, "application/json");

        await ProbeAsync();

        Paths().ShouldBe(["/rest/api/2/serverInfo", "/rest/agile/1.0/board"], ignoreOrder: true);

        var board = _jira.LogEntries
            .Select(entry => entry.RequestMessage)
            .Single(request => request?.Path is "/rest/agile/1.0/board")
            .ShouldNotBeNull();

        board.Query.ShouldNotBeNull()["maxResults"].ShouldHaveSingleItem().ShouldBe("1");
    }

    [Fact]
    public async Task The_probe_records_when_it_was_taken()
    {
        StubServerInfo();
        StubBoard(HttpStatusCode.OK, BoardPayload, "application/json");

        var before = DateTimeOffset.UtcNow;

        var capabilities = await ProbeAsync();

        capabilities.ProbedAt.ShouldBeInRange(before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void A_probe_older_than_seven_days_is_stale()
    {
        var probedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var capabilities = new JiraCapabilities("8.20.7", "Server", true, probedAt);

        capabilities.IsStale(probedAt.AddDays(7).AddSeconds(-1)).ShouldBeFalse();
        capabilities.IsStale(probedAt.AddDays(7).AddSeconds(1)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_revoked_token_fails_the_probe_rather_than_being_read_as_jira_core()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/serverInfo").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(ServerInfoPayload));

        StubBoard(HttpStatusCode.Unauthorized, """{ "errorMessages": ["denied"], "errors": {} }""", "application/json");

        var failure = await Should.ThrowAsync<JiraApiException>(ProbeAsync);

        failure.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private Task<JiraCapabilities> ProbeAsync() =>
        CreateClient().ProbeCapabilitiesAsync(TestContext.Current.CancellationToken);

    private void StubServerInfo() =>
        _jira.Given(Request.Create().WithPath("/rest/api/2/serverInfo").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(ServerInfoPayload));

    private void StubBoard(HttpStatusCode status, string payload, string contentType) =>
        _jira.Given(Request.Create().WithPath("/rest/agile/1.0/board").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(status)
                .WithHeader("Content-Type", contentType)
                .WithBody(payload));

    private string[] Paths() =>
    [
        .. _jira.LogEntries.Select(entry => entry.RequestMessage?.Path).OfType<string>(),
    ];

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
