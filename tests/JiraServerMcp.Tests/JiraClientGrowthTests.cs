namespace JiraServerMcp.Tests;

/// <summary>
/// ADR-0006: JiraClient has no internal seam, so every new tool adds a method to the same file.
/// This pins the trigger for revisiting that rather than leaving it as prose nobody re-reads.
/// </summary>
public class JiraClientGrowthTests
{
    [Fact]
    public void JiraClient_files_stay_under_the_adr_0006_line_budget()
    {
        var jiraProject = new DirectoryInfo(
            Path.Combine(RepositoryRoot.Find().FullName, "src", "JiraServerMcp.Jira"));

        var totalLines = jiraProject.GetFiles("JiraClient*.cs")
            .Sum(file => File.ReadAllLines(file.FullName).Length);

        totalLines.ShouldBeLessThan(800,
            "JiraClient*.cs has grown past the ADR-0006 budget. Split JiraClient into " +
            "partial class files along the resource axis (issues, projects, users, agile, " +
            "writes, core), or amend ADR-0006 to move the threshold.");
    }
}
