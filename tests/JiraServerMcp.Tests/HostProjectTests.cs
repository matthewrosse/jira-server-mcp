using System.Xml.Linq;

namespace JiraServerMcp.Tests;

/// <summary>
/// ADR-0003: two source projects with one dependency edge, host to client.
/// </summary>
public class HostProjectTests
{
    [Fact]
    public void Host_project_references_the_client_project()
    {
        var project = XDocument.Load(RepositoryRoot.SourceProject("JiraServerMcp").FullName);

        var references = project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty);

        references.ShouldContain(
            reference => reference.EndsWith("JiraServerMcp.Jira.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void Source_tree_holds_two_projects_only()
    {
        var projects = RepositoryRoot.Find()
            .GetDirectories("src")
            .Single()
            .GetFiles("*.csproj", SearchOption.AllDirectories)
            .Select(file => Path.GetFileNameWithoutExtension(file.Name));

        projects.ShouldBe(["JiraServerMcp", "JiraServerMcp.Jira"], ignoreOrder: true);
    }
}
