using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Tests;

/// <summary>
/// The auth verbs as an operator experiences them: exit codes, what the terminal shows, and what
/// is left in the store afterwards.
/// </summary>
public sealed class AuthVerbTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    private readonly WireMockServer _jira = WireMockServer.Start();
    private readonly ConfigurationHome _home = new();

    public void Dispose()
    {
        _jira.Stop();
        _home.Dispose();
    }

    [Fact]
    public async Task Login_stores_a_piped_token_and_names_the_user_it_resolved()
    {
        GivenTheTokenIsAccepted();
        await AddProfileAsync();

        var result = await RunAsync(["auth", "login", "work"], standardInput: Token + "\n");

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("Mateusz Różański");
        result.StandardOutput.ShouldContain("mrosse");
        result.StandardOutput.ShouldNotContain(Token);
        result.StandardError.ShouldNotContain(Token);

        File.Exists(_home.CredentialsFile).ShouldBeTrue();
    }

    [Fact]
    public async Task Login_refuses_a_token_Jira_rejects_and_stores_nothing()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(401)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"errorMessages":["You do not have permission to log in."],"errors":{}}"""));

        await AddProfileAsync();

        var result = await RunAsync(["auth", "login", "work"], standardInput: Token + "\n");

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("401");
        result.StandardError.ShouldNotContain("Unhandled exception");

        // The failure lands at login, so nothing is left for an agent to trip over later.
        File.Exists(_home.CredentialsFile).ShouldBeFalse();
    }

    [Fact]
    public async Task A_token_given_as_an_argument_is_refused_with_the_reason()
    {
        await AddProfileAsync();

        var result = await RunAsync(["auth", "login", "work", "--token", Token]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("--token");
        result.StandardError.ShouldContain("JIRA_SERVER_MCP__WORK__TOKEN");
        File.Exists(_home.CredentialsFile).ShouldBeFalse();
    }

    [Fact]
    public async Task Status_prints_the_resolved_jira_user()
    {
        GivenTheTokenIsAccepted();
        await AddProfileAsync();
        await LoginAsync();

        var result = await RunAsync(["auth", "status", "work"]);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("Mateusz Różański");
        result.StandardOutput.ShouldContain("mrosse");
        result.StandardOutput.ShouldNotContain(Token);
    }

    [Fact]
    public async Task Status_says_what_to_run_when_nothing_is_stored()
    {
        await AddProfileAsync();

        var result = await RunAsync(["auth", "status", "work"]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("auth login work");
    }

    [Fact]
    public async Task Status_reports_a_revoked_token_as_something_to_do_rather_than_a_status_code()
    {
        GivenTheTokenIsAccepted();
        await AddProfileAsync();
        await LoginAsync();

        _jira.Reset();
        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(401));

        var result = await RunAsync(["auth", "status", "work"]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("invalid or revoked");
        result.StandardError.ShouldContain("auth login work");
    }

    [Fact]
    public async Task Status_reads_the_environment_variable_ahead_of_the_store()
    {
        GivenTheTokenIsAccepted();
        await AddProfileAsync();

        var result = await RunAsync(
            ["auth", "status", "work"],
            environment: new Dictionary<string, string>(_home.Environment)
            {
                ["JIRA_SERVER_MCP__WORK__TOKEN"] = Token,
            });

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("mrosse");
        result.StandardOutput.ShouldContain("JIRA_SERVER_MCP__WORK__TOKEN");

        _jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull()
            .RequestMessage.ShouldNotBeNull()
            .Headers.ShouldNotBeNull()["Authorization"].ShouldHaveSingleItem()
            .ShouldBe("Bearer " + Token);
    }

    [Fact]
    public async Task Logout_removes_the_credential_and_leaves_the_profile()
    {
        GivenTheTokenIsAccepted();
        await AddProfileAsync();
        await LoginAsync();

        var result = await RunAsync(["auth", "logout", "work"]);

        result.ExitCode.ShouldBe(0);

        var status = await RunAsync(["auth", "status", "work"]);

        status.ExitCode.ShouldNotBe(0);
        status.StandardError.ShouldContain("auth login work");

        var profiles = await RunAsync(["profile", "list"]);

        profiles.StandardOutput.ShouldContain("work");
    }

    [Fact]
    public async Task Logout_with_nothing_stored_says_so_and_is_not_a_failure()
    {
        await AddProfileAsync();

        var result = await RunAsync(["auth", "logout", "work"]);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("work");
    }

    [Fact]
    public async Task Logout_of_a_profile_that_is_not_there_is_refused()
    {
        var result = await RunAsync(["auth", "logout", "absent"]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("absent");
    }

    private void GivenTheTokenIsAccepted() =>
        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet()
                .WithHeader("Authorization", "Bearer " + Token))
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"name":"mrosse","displayName":"Mateusz Różański","active":true}"""));

    private Task<HostProcessResult> AddProfileAsync() =>
        RunAsync(["profile", "add", "work", "--url", _jira.Url!]);

    private Task<HostProcessResult> LoginAsync() =>
        RunAsync(["auth", "login", "work"], standardInput: Token + "\n");

    private Task<HostProcessResult> RunAsync(
        string[] verb,
        string? standardInput = null,
        IReadOnlyDictionary<string, string>? environment = null) =>
        HostProcess.RunAsync(
            verb,
            TestContext.Current.CancellationToken,
            environment ?? _home.Environment,
            standardInput);
}
