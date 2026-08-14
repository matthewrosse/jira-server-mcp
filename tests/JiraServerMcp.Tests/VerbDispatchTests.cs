namespace JiraServerMcp.Tests;

/// <summary>
/// ADR-0002: standard output belongs to the protocol, so even a usage message goes to standard
/// error.
/// </summary>
public sealed class VerbDispatchTests : IDisposable
{
    private readonly ConfigurationHome _home = new();

    public void Dispose() => _home.Dispose();

    [Fact]
    public async Task An_unknown_verb_fails_and_explains_itself_on_standard_error()
    {
        var result = await RunAsync(["frobnicate"]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("frobnicate");
        result.StandardError.ShouldContain("Usage:");
        result.StandardOutput.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_verb_fails_and_explains_itself_on_standard_error()
    {
        var result = await RunAsync([]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("Usage:");
        result.StandardOutput.ShouldBeEmpty();
    }

    [Fact]
    public async Task Serving_without_a_profile_says_which_option_is_missing()
    {
        var result = await RunAsync(["serve"]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("--profile");
        result.StandardOutput.ShouldBeEmpty();
    }

    [Fact]
    public async Task Serving_an_unknown_profile_fails_at_startup_and_names_it()
    {
        var result = await RunAsync(["serve", "--profile", "absent"]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("absent");
        result.StandardError.ShouldContain("profile add absent");
        result.StandardError.ShouldNotContain("Unhandled exception");
        result.StandardOutput.ShouldBeEmpty();
    }

    [Fact]
    public async Task Serving_a_profile_with_no_credential_names_the_command_that_fixes_it()
    {
        await RunAsync(["profile", "add", "work", "--url", "https://jira.example.com"]);

        var result = await RunAsync(["serve", "--profile", "work"]);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("work");
        result.StandardError.ShouldContain("auth login work");
        result.StandardError.ShouldNotContain("Unhandled exception");
        result.StandardOutput.ShouldBeEmpty();
    }

    [Fact]
    public async Task Logging_in_to_an_unknown_profile_is_refused()
    {
        var result = await HostProcess.RunAsync(
            ["auth", "login", "absent"],
            TestContext.Current.CancellationToken,
            _home.Environment,
            standardInput: "s3cr3t-personal-access-token\n");

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("absent");
        File.Exists(_home.CredentialsFile).ShouldBeFalse();
    }

    private Task<HostProcessResult> RunAsync(string[] verb) =>
        HostProcess.RunAsync(verb, TestContext.Current.CancellationToken, _home.Environment);
}
