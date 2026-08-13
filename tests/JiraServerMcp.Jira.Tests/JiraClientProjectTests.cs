using System.Xml.Linq;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// ADR-0003: the Jira client project must not reference any MCP package, so the client stays
/// usable and testable with no MCP concept present.
/// </summary>
public class JiraClientProjectTests
{
    [Fact]
    public void Client_project_references_no_mcp_package()
    {
        var project = XDocument.Load(RepositoryRoot.Find()
            .GetFiles("JiraServerMcp.Jira.csproj", SearchOption.AllDirectories)
            .Single(file => file.Directory!.Name == "JiraServerMcp.Jira")
            .FullName);

        var packages = project.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty);

        packages.ShouldNotContain(
            package => package.Contains("ModelContextProtocol", StringComparison.OrdinalIgnoreCase)
                || package.Contains("Mcp", StringComparison.OrdinalIgnoreCase));
    }
}

internal static class RepositoryRoot
{
    public static DirectoryInfo Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !directory.GetFiles("JiraServerMcp.slnx").Any())
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException(
            "Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
