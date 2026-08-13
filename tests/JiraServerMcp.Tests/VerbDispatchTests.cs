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
}
