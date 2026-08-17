using System.Xml.Linq;

namespace JiraServerMcp.Tests;

/// <summary>
/// ADR-0004: the primary artefact is a .NET tool, so the host project's packaging metadata is
/// part of the product rather than a build detail.
/// </summary>
public class PackagingTests
{
    private static readonly XDocument _host =
        XDocument.Load(RepositoryRoot.SourceProject("JiraServerMcp").FullName);

    private static readonly XDocument _client =
        XDocument.Load(RepositoryRoot.SourceProject("JiraServerMcp.Jira").FullName);

    [Fact]
    public void The_host_packs_as_a_tool_invoked_by_name()
    {
        Property(_host, "PackAsTool").ShouldBe("true");
        Property(_host, "ToolCommandName").ShouldBe("jira-server-mcp");
    }

    [Fact]
    public void The_package_is_identified_by_the_repository_name()
    {
        Property(_host, "PackageId").ShouldBe("jira-server-mcp");
    }

    [Theory]
    [InlineData("Description")]
    [InlineData("Authors")]
    [InlineData("PackageLicenseExpression")]
    [InlineData("PackageProjectUrl")]
    [InlineData("PackageTags")]
    [InlineData("PackageReadmeFile")]
    public void Package_metadata_is_complete_enough_to_read_on_a_feed(string property)
    {
        Property(_host, property).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_readme_named_by_the_metadata_is_packed()
    {
        var readme = Property(_host, "PackageReadmeFile");

        var packed = _host.Descendants("None")
            .Where(item => item.Attribute("Pack")?.Value == "true")
            .Select(item => item.Attribute("Include")?.Value ?? string.Empty);

        packed.ShouldContain(include => include.EndsWith(readme!, StringComparison.Ordinal));
        File.Exists(Path.Combine(RepositoryRoot.Find().FullName, readme!)).ShouldBeTrue();
    }

    [Fact]
    public void The_version_comes_from_the_tag_rather_than_from_a_file()
    {
        // A version written here is a version someone forgets to bump. The release workflow
        // passes one derived from the tag to both the pack and the publish, which is the only
        // way the tool package and the binaries can be relied on to agree.
        Property(_host, "Version").ShouldBeNull();
        Property(_host, "VersionPrefix").ShouldBeNull();
    }

    [Fact]
    public void The_binaries_are_published_untrimmed()
    {
        // The MCP SDK discovers tools by reflection and System.Text.Json binds by reflection.
        // Trimming either is a runtime failure on one platform in exchange for disk space.
        Property(_host, "PublishTrimmed").ShouldBe("false");
    }

    [Fact]
    public void A_tag_produces_one_package_and_not_two()
    {
        // The client is publishable on its own merits one day (ADR-0003), but shipping it
        // unannounced alongside the tool is not that decision.
        Property(_client, "IsPackable").ShouldBe("false");
    }

    private static string? Property(XDocument project, string name) =>
        project.Descendants("PropertyGroup").Elements(name).SingleOrDefault()?.Value;
}
