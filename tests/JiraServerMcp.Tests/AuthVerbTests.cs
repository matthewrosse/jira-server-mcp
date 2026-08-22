using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace JiraServerMcp.Tests;

/// <summary>
/// The auth verbs as an operator experiences them: exit codes, what the terminal shows, and what
/// is left in the store afterwards.
/// </summary>
public sealed class AuthVerbTests : IDisposable
{
    private readonly VerbSeam _seam = new();

    public void Dispose() => _seam.Dispose();

    [Fact]
    public async Task Login_stores_a_piped_token_and_names_the_user_it_resolved()
    {
        GivenTheTokenIsAccepted();
        await _seam.AddProfileAsync();

        var result = await _seam.RunAsync(
            ["auth", "login", "work"], standardInput: VerbSeam.Token + "\n");

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("Ada Lovelace");
        result.StandardOutput.ShouldContain("ada");
        result.StandardOutput.ShouldNotContain(VerbSeam.Token);
        result.StandardError.ShouldNotContain(VerbSeam.Token);

        File.Exists(_seam.Home.CredentialsFile).ShouldBeTrue();
    }

    [Fact]
    public async Task Login_refuses_a_token_Jira_rejects_and_stores_nothing()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(401)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"errorMessages":["You do not have permission to log in."],"errors":{}}"""));

        await _seam.AddProfileAsync();

        var result = await _seam.RunAsync(
            ["auth", "login", "work"], standardInput: VerbSeam.Token + "\n");

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("401");
        result.StandardError.ShouldNotContain("Unhandled exception");

        // The failure lands at login, so nothing is left for an agent to trip over later.
        File.Exists(_seam.Home.CredentialsFile).ShouldBeFalse();
    }

    [Fact]
    public async Task A_token_given_as_an_argument_is_refused_with_the_reason()
    {
        await _seam.AddProfileAsync();

        var result = await _seam.RunAsync(["auth", "login", "work", "--token", VerbSeam.Token]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("--token");
        result.StandardError.ShouldContain("JIRA_SERVER_MCP__WORK__TOKEN");
        File.Exists(_seam.Home.CredentialsFile).ShouldBeFalse();
    }

    [Fact]
    public async Task Status_prints_the_resolved_jira_user()
    {
        await _seam.AddProfileAsync();
        await _seam.LoginAsync();

        var result = await _seam.RunAsync(["auth", "status", "work"]);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("Ada Lovelace");
        result.StandardOutput.ShouldContain("ada");
        result.StandardOutput.ShouldNotContain(VerbSeam.Token);
    }

    [Fact]
    public async Task Status_says_what_to_run_when_nothing_is_stored()
    {
        await _seam.AddProfileAsync();

        var result = await _seam.RunAsync(["auth", "status", "work"]);

        result.ExitCode.ShouldBe(1);
        result.StandardError.ShouldContain("auth login work");
    }

    [Fact]
    public async Task Status_reports_a_revoked_token_as_something_to_do_rather_than_a_status_code()
    {
        await _seam.AddProfileAsync();
        await _seam.LoginAsync();

        _seam.Jira.Reset();
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(401));

        var result = await _seam.RunAsync(["auth", "status", "work"]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("invalid or revoked");
        result.StandardError.ShouldContain("auth login work");
    }

    [Fact]
    public async Task Status_reads_the_environment_variable_ahead_of_the_store()
    {
        GivenTheTokenIsAccepted();
        await _seam.AddProfileAsync();

        var result = await _seam.RunAsync(
            ["auth", "status", "work"],
            environment: new Dictionary<string, string>
            {
                ["JIRA_SERVER_MCP__WORK__TOKEN"] = VerbSeam.Token,
            });

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("ada");
        result.StandardOutput.ShouldContain("JIRA_SERVER_MCP__WORK__TOKEN");

        _seam.Jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull()
            .RequestMessage.ShouldNotBeNull()
            .Headers.ShouldNotBeNull()["Authorization"].ShouldHaveSingleItem()
            .ShouldBe("Bearer " + VerbSeam.Token);
    }

    [Fact]
    public async Task Logout_removes_the_credential_and_leaves_the_profile()
    {
        await _seam.AddProfileAsync();
        await _seam.LoginAsync();

        var result = await _seam.RunAsync(["auth", "logout", "work"]);

        result.ExitCode.ShouldBe(0);

        var status = await _seam.RunAsync(["auth", "status", "work"]);

        status.ExitCode.ShouldNotBe(0);
        status.StandardError.ShouldContain("auth login work");

        var profiles = await _seam.RunAsync(["profile", "list"]);

        profiles.StandardOutput.ShouldContain("work");
    }

    [Fact]
    public async Task Logout_with_nothing_stored_says_so_and_is_not_a_failure()
    {
        await _seam.AddProfileAsync();

        var result = await _seam.RunAsync(["auth", "logout", "work"]);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("work");
    }

    [Fact]
    public async Task Logout_of_a_profile_that_is_not_there_is_refused()
    {
        var result = await _seam.RunAsync(["auth", "logout", "absent"]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("absent");
    }

    /// <summary>
    /// Matched on the bearer token, for the tests where which token reached Jira is the point.
    /// The seam's own login stub answers whatever it is shown, which is what the tests that only
    /// need a stored credential want.
    /// </summary>
    private void GivenTheTokenIsAccepted() =>
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet()
                .WithHeader("Authorization", "Bearer " + VerbSeam.Token))
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JiraAccount.Payload()));
}
