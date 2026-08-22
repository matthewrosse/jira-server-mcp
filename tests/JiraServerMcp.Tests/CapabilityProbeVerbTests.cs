using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Tests;

/// <summary>
/// Where the capability probe is taken and where it is kept: at `auth login`, again at
/// `profile refresh`, and on the profile in between.
/// </summary>
public sealed class CapabilityProbeVerbTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    private readonly ConfigurationHome _home = new();
    private readonly WireMockServer _jira = WireMockServer.Start();

    public CapabilityProbeVerbTests()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Json("""{"name":"ada","displayName":"Ada Lovelace","active":true}"""));

        _jira.Given(Request.Create().WithPath("/rest/api/2/serverInfo").UsingGet())
            .RespondWith(Json("""{"version":"8.20.7","deploymentType":"Server"}"""));
    }

    public void Dispose()
    {
        _jira.Stop();
        _home.Dispose();
    }

    [Fact]
    public async Task Logging_in_records_the_probe_on_the_profile()
    {
        StubSoftware(licensed: true);

        await AddAsync("work");

        var result = await LoginAsync("work");

        result.ExitCode.ShouldBe(0);

        var capabilities = CapabilitiesOf("work");

        capabilities.GetProperty("version").GetString().ShouldBe("8.20.7");
        capabilities.GetProperty("deploymentType").GetString().ShouldBe("Server");
        capabilities.GetProperty("softwareLicensed").GetBoolean().ShouldBeTrue();
        capabilities.GetProperty("probedAt").GetDateTimeOffset()
            .ShouldBeInRange(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_jira_core_instance_is_recorded_as_having_no_jira_software()
    {
        StubSoftware(licensed: false);

        await AddAsync("work");
        await LoginAsync("work");

        CapabilitiesOf("work").GetProperty("softwareLicensed").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_probe_that_fails_does_not_lose_the_token_that_was_just_validated()
    {
        _jira.Given(Request.Create().WithPath("/rest/agile/1.0/board").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        await AddAsync("work");

        var result = await LoginAsync("work");

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("Ada Lovelace");
        result.StandardError.ShouldContain("profile refresh work");

        ProfileIn("work").TryGetProperty("capabilities", out var capabilities);
        (capabilities.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined).ShouldBeTrue();
    }

    [Fact]
    public async Task Logging_in_again_with_a_probe_that_fails_leaves_the_recorded_one_alone()
    {
        StubSoftware(licensed: true);

        await AddAsync("work");
        await LoginAsync("work");

        _jira.Reset();
        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Json("""{"name":"ada","displayName":"Ada Lovelace","active":true}"""));
        _jira.Given(Request.Create().WithPath("/rest/api/2/serverInfo").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(503));

        // Rotating a token onto a profile that has been probed before.
        var result = await LoginAsync("work");

        result.ExitCode.ShouldBe(0);

        // The probe is still there, and so are the tools it registers, so saying there is none
        // would contradict what the next `serve` does.
        result.StandardError.ShouldNotContain("has no capability probe");
        result.StandardError.ShouldContain("unchanged");
        result.StandardError.ShouldContain("profile refresh work");

        CapabilitiesOf("work").GetProperty("softwareLicensed").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Refresh_takes_the_probe_again_and_reports_what_it_found()
    {
        StubSoftware(licensed: true);

        await AddAsync("work");
        await LoginAsync("work");

        var result = await RunAsync(["profile", "refresh", "work"]);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("8.20.7");
        result.StandardOutput.ShouldContain("Jira Software");
    }

    [Fact]
    public async Task Refresh_replaces_a_probe_that_no_longer_describes_the_instance()
    {
        StubSoftware(licensed: true);

        await AddAsync("work");
        await LoginAsync("work");

        CapabilitiesOf("work").GetProperty("softwareLicensed").GetBoolean().ShouldBeTrue();

        // The licence lapsed, and the software API stopped answering.
        _jira.Reset();
        _jira.Given(Request.Create().WithPath("/rest/api/2/serverInfo").UsingGet())
            .RespondWith(Json("""{"version":"8.20.7","deploymentType":"Server"}"""));
        StubSoftware(licensed: false);

        var probedAt = CapabilitiesOf("work").GetProperty("probedAt").GetDateTimeOffset();

        (await RunAsync(["profile", "refresh", "work"])).ExitCode.ShouldBe(0);

        var refreshed = CapabilitiesOf("work");

        refreshed.GetProperty("softwareLicensed").GetBoolean().ShouldBeFalse();
        refreshed.GetProperty("probedAt").GetDateTimeOffset().ShouldBeGreaterThan(probedAt);
    }

    [Fact]
    public async Task Refreshing_a_profile_that_was_never_registered_says_so()
    {
        var result = await RunAsync(["profile", "refresh", "absent"]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("profile add absent");
    }

    [Fact]
    public async Task Refreshing_without_a_stored_token_names_the_command_that_stores_one()
    {
        await AddAsync("work");

        var result = await RunAsync(["profile", "refresh", "work"]);

        result.ExitCode.ShouldBe(1);
        result.StandardError.ShouldContain("the capability probe is taken as the Jira user");
        result.StandardError.ShouldContain("auth login work");
    }

    [Fact]
    public async Task A_refresh_the_instance_refuses_leaves_the_recorded_probe_alone()
    {
        StubSoftware(licensed: true);

        await AddAsync("work");
        await LoginAsync("work");

        _jira.Reset();
        _jira.Given(Request.Create().WithPath("/rest/api/2/serverInfo").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(401)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"errorMessages":["denied"],"errors":{}}"""));

        var result = await RunAsync(["profile", "refresh", "work"]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldNotContain("Unhandled exception");

        CapabilitiesOf("work").GetProperty("softwareLicensed").GetBoolean().ShouldBeTrue();
    }

    private void StubSoftware(bool licensed) =>
        _jira.Given(Request.Create().WithPath("/rest/agile/1.0/board").UsingGet())
            .RespondWith(licensed
                ? Json("""{"startAt":0,"maxResults":1,"isLast":false,"values":[{"id":1,"name":"b"}]}""")
                : Response.Create().WithStatusCode(404)
                    .WithHeader("Content-Type", "text/html")
                    .WithBody("<html><body>Not found</body></html>"));

    private static IResponseBuilder Json(string payload) =>
        Response.Create().WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(payload);

    private Task<HostProcessResult> AddAsync(string name) =>
        RunAsync(["profile", "add", name, "--url", _jira.Url!]);

    private Task<HostProcessResult> LoginAsync(string name) =>
        RunAsync(["auth", "login", name], standardInput: Token + "\n");

    private Task<HostProcessResult> RunAsync(string[] verb, string? standardInput = null) =>
        HostProcess.RunAsync(
            verb,
            TestContext.Current.CancellationToken,
            _home.Environment,
            standardInput);

    private JsonElement CapabilitiesOf(string name) => ProfileIn(name).GetProperty("capabilities");

    private JsonElement ProfileIn(string name) =>
        JsonDocument.Parse(_home.ReadProfiles()).RootElement
            .GetProperty("profiles").GetProperty(name);
}
