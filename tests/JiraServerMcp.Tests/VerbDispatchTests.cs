namespace JiraServerMcp.Tests;

/// <summary>
/// ADR-0002: standard output belongs to the protocol, so even a usage message goes to standard
/// error.
/// </summary>
public sealed class VerbDispatchTests
{
    [Fact]
    public async Task An_unknown_verb_fails_and_explains_itself_on_standard_error()
    {
        var result = await HostProcess.RunAsync(["frobnicate"], TestContext.Current.CancellationToken);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("frobnicate");
        result.StandardError.ShouldContain("Usage:");
        result.StandardOutput.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_verb_fails_and_explains_itself_on_standard_error()
    {
        var result = await HostProcess.RunAsync([], TestContext.Current.CancellationToken);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("Usage:");
        result.StandardOutput.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_argument_after_serve_is_reported_as_the_argument_not_the_verb()
    {
        var result = await HostProcess.RunAsync(["serve", "--verbose"], TestContext.Current.CancellationToken);

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("takes no arguments");
        result.StandardError.ShouldNotContain("Unknown verb");
        result.StandardOutput.ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_configuration_is_reported_rather_than_thrown()
    {
        var result = await HostProcess.RunAsync(["serve"], TestContext.Current.CancellationToken);

        Console.WriteLine("DEBUG STDERR: " + result.StandardError);
        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("JIRA_SERVER_MCP_URL");
        result.StandardError.ShouldContain("JIRA_SERVER_MCP_TOKEN");
        result.StandardError.ShouldNotContain("Unhandled exception");
        result.StandardOutput.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_base_url_that_is_not_http_is_refused_at_startup()
    {
        var result = await HostProcess.RunAsync(
            ["serve"],
            TestContext.Current.CancellationToken,
            new Dictionary<string, string>
            {
                ["JIRA_SERVER_MCP_URL"] = "file:///etc/passwd",
                ["JIRA_SERVER_MCP_TOKEN"] = "unused-by-this-test",
            });

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldContain("must be an http or https URL");
        result.StandardOutput.ShouldBeEmpty();
    }
}
