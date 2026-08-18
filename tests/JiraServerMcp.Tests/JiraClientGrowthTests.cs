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

        // ADR-0006's 2026-08-18 amendment moved this from 800 and spent its one deferral: the
        // next time it goes red, the split is the answer, not a third number.
        totalLines.ShouldBeLessThan(1_000,
            "JiraClient*.cs has grown past the ADR-0006 budget. Split JiraClient into " +
            "partial class files along the resource axis (issues, projects, users, agile, " +
            "writes, core), on a commit of its own, and set the threshold from what the split " +
            "measures. ADR-0006's amendment rules out moving the number again.");
    }
}
