namespace JiraServerMcp.JiraIntegration.Tests;

/// <summary>
/// Jira-backed tests stay off the pull-request path: that workflow runs the other three test
/// projects by name and never this one. The trait is carried for the nightly Jira workflow to
/// select on, and the harness itself lands in Phase 7 (#22).
/// </summary>
public class JiraHarnessPlaceholderTests
{
    [Fact]
    [Trait("Category", "JiraIntegration")]
    public void Placeholder_until_the_harness_lands()
    {
        // The project needs one test so the runner has something to discover. Nothing here
        // touches Jira.
        true.ShouldBeTrue();
    }
}
