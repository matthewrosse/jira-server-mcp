namespace JiraServerMcp.JiraIntegration.Tests;

/// <summary>
/// Jira-backed tests are trait-gated: the pull-request workflow excludes this category, and the
/// nightly Jira workflow includes it. The harness itself lands in Phase 7 (#22).
/// </summary>
public class JiraHarnessPlaceholderTests
{
    [Fact]
    [Trait("Category", "JiraIntegration")]
    public void Trait_gate_is_wired()
    {
        // Placeholder. Its only job today is to prove the trait filter selects and deselects
        // this project's tests.
        true.ShouldBeTrue();
    }
}
