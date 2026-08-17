using System.Text.RegularExpressions;

namespace JiraServerMcp.Tests;

/// <summary>
/// ADR-0004: two artefacts, produced by one workflow, whose versions have to agree. The workflow
/// itself can only be proven by a real tag, so what is asserted here is that the release cannot
/// quietly lose a runtime identifier, a checksum, the attestation, or the smoke check that keeps
/// an unstartable package off the feed.
/// </summary>
public class ReleaseWorkflowTests
{
    private static readonly string _workflow = File.ReadAllText(
        Path.Combine(RepositoryRoot.Find().FullName, ".github", "workflows", "release.yml"));

    [Fact]
    public void The_release_is_triggered_by_a_tag()
    {
        _workflow.ShouldMatch(@"on:\s*\n\s*push:\s*\n\s*tags:");
    }

    [Theory]
    [InlineData("win-x64")]
    [InlineData("win-arm64")]
    [InlineData("osx-arm64")]
    [InlineData("osx-x64")]
    [InlineData("linux-x64")]
    [InlineData("linux-arm64")]
    public void Every_runtime_identifier_is_built(string runtimeIdentifier)
    {
        _workflow.ShouldContain(runtimeIdentifier);
    }

    [Fact]
    public void The_version_is_derived_from_the_tag_rather_than_hand_edited()
    {
        _workflow.ShouldContain("GITHUB_REF_NAME");

        // Both artefacts take the same computed version, which is what makes them agree.
        Regex.Matches(_workflow, @"-p:Version=\$").Count.ShouldBeGreaterThanOrEqualTo(2);
        _workflow.ShouldNotMatch(@"-p:Version=\d");
    }

    [Fact]
    public void A_tag_that_is_not_a_version_stops_the_release()
    {
        // 'v1.0' would name the binaries 1.0 while NuGet normalises the package to 1.0.0, and
        // the release would carry artefacts disagreeing about their own version.
        _workflow.ShouldContain(@"^[0-9]+\.[0-9]+\.[0-9]+");
    }

    [Fact]
    public void Every_artefact_is_published_with_a_checksum()
    {
        _workflow.ShouldContain("sha256sum");
        _workflow.ShouldContain(".sha256");
    }

    [Fact]
    public void Build_provenance_is_attested_for_the_release_artefacts()
    {
        _workflow.ShouldContain("actions/attest-build-provenance");
        _workflow.ShouldContain("id-token: write");
        _workflow.ShouldContain("attestations: write");
    }

    [Fact]
    public void The_tool_package_and_a_binary_are_started_before_anything_is_published()
    {
        // A release that ships a package which cannot start is worse than no release.
        _workflow.ShouldContain("dotnet tool install");
        _workflow.ShouldContain("profile list");

        // '--source' replaces the configured feeds; '--add-source' appends to them, which would
        // let the smoke check install and run a package of the same name from somewhere else.
        _workflow.ShouldContain("--source ./package");
        WithoutComments.ShouldNotContain("--add-source");

        var publish = Job("publish");
        publish.ShouldContain("package");
        publish.ShouldContain("binaries");
    }

    [Fact]
    public void The_tool_package_goes_to_github_packages_and_not_to_the_public_gallery()
    {
        _workflow.ShouldContain("nuget.pkg.github.com");
        _workflow.ShouldContain("packages: write");
        _workflow.ShouldNotContain("api.nuget.org");
        _workflow.ShouldNotContain("nuget.org/v3");
    }

    /// <summary>
    /// The workflow with its comments taken out, for assertions about what it runs rather than
    /// about what it says.
    /// </summary>
    private static string WithoutComments =>
        string.Concat(_workflow.Split('\n')
            .Where(line => !line.TrimStart().StartsWith('#'))
            .Select(line => line + "\n"));

    /// <summary>
    /// The <c>needs:</c> line of a job, which is where the ordering between building, smoke
    /// testing and publishing actually lives.
    /// </summary>
    private static string Job(string name) =>
        Regex.Match(_workflow, $@"^  {name}:\n(?<body>(?:.*\n)*?)(?=^  \S|\z)", RegexOptions.Multiline)
            .Groups["body"].Value is { Length: > 0 } body
            ? Regex.Match(body, @"needs:.*").Value
            : throw new InvalidOperationException($"The workflow has no '{name}' job.");
}
