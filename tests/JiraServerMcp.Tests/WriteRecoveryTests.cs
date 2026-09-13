using JiraServerMcp.Rendering;
using JiraServerMcp.Tools;

namespace JiraServerMcp.Tests;

/// <summary>
/// Recovery advice in each frame that can carry it, proven at the module's own signature. Under
/// ADR-0008 clause 3 this is where pure logic lifted out of a tool is proven; what an agent reads
/// after replaying a spent key is proven once at the protocol seam, in
/// <c>RetrySafeWritesProtocolTests</c>.
/// </summary>
public sealed class WriteRecoveryTests
{
    [Fact]
    public void Reading_an_expansion_names_the_issue_after_a_timeout_and_not_after_a_spent_key()
    {
        var recovery = WriteRecovery.ByExpansion("PROJ-42", Expansion.Comments);

        WriteRecovery.AfterTimeout("comment", recovery).ShouldBe(
            ". The comment was sent once and was not repeated, so read PROJ-42 with "
            + "jira_get_issues and the comments expansion to see whether it landed.");

        WriteRecovery.AfterKeyedTimeout("comment", recovery).ShouldBe(
            ". The comment was sent once and was not repeated, so read PROJ-42 with "
            + "jira_get_issues and the comments expansion before sending it again.");

        // The replay is told with this call's arguments, and a key reused against another issue
        // would otherwise send the caller to read the wrong one.
        WriteRecovery.AfterSpentKey("comment", recovery).ShouldBe(
            "This key was already used by a comment whose outcome is unknown: it was sent once "
            + "and no answer came back. Nothing was written again. Read the issue with "
            + "jira_get_issues and the comments expansion before sending it again under a new "
            + "key.");
    }

    [Fact]
    public void Reading_the_issue_names_no_expansion()
    {
        var recovery = WriteRecovery.ByReading("PROJ-42");

        WriteRecovery.AfterTimeout("transition", recovery).ShouldBe(
            ". The transition was sent once and was not repeated, so read PROJ-42 with "
            + "jira_get_issues to see whether it landed.");

        WriteRecovery.AfterKeyedTimeout("update", recovery).ShouldBe(
            ". The update was sent once and was not repeated, so read PROJ-42 with "
            + "jira_get_issues before sending it again.");

        WriteRecovery.AfterSpentKey("update", recovery).ShouldEndWith(
            "Nothing was written again. Read the issue with jira_get_issues before sending it "
            + "again under a new key.");
    }

    [Fact]
    public void Searching_says_the_issue_may_not_exist_in_every_frame()
    {
        var recovery = WriteRecovery.BySearch();

        WriteRecovery.AfterTimeout("create", recovery).ShouldBe(
            ". The create was sent once and was not repeated, so the issue may or may not exist: "
            + "search for the summary with jira_search to see whether it landed.");

        WriteRecovery.AfterKeyedTimeout("create", recovery).ShouldBe(
            ". The create was sent once and was not repeated, so the issue may or may not exist: "
            + "search for the summary with jira_search before sending it again.");

        WriteRecovery.AfterSpentKey("create", recovery).ShouldEndWith(
            "Nothing was written again. The issue may or may not exist: search for the summary "
            + "with jira_search before sending it again under a new key.");
    }

    /// <summary>
    /// The one shape a keyed frame cannot take: <see cref="WriteRecovery.Safe"/> answers with a
    /// type the other two renderers do not accept, so there is no third assertion to write.
    /// </summary>
    [Fact]
    public void A_safe_repeat_says_why_rather_than_what_to_read()
    {
        var recovery = WriteRecovery.Safe(
            "the URL identifies the link, so a repeat updates rather than duplicates");

        WriteRecovery.AfterTimeout("link", recovery).ShouldBe(
            ". The link was sent once and was not repeated. Sending it again is safe — the URL "
            + "identifies the link, so a repeat updates rather than duplicates.");
    }

    [Fact]
    public void A_warning_is_carried_by_the_recovery_into_every_frame()
    {
        var recovery = WriteRecovery.ByExpansion(
            "PROJ-42",
            Expansion.Attachments,
            warning: "Jira appends an attachment rather than replacing one, so a blind retry is a "
                     + "second copy of the file");

        const string Warned =
            " — Jira appends an attachment rather than replacing one, so a blind retry is a "
            + "second copy of the file.";

        WriteRecovery.AfterTimeout("attachment", recovery).ShouldEndWith(
            "attachments expansion to see whether it landed" + Warned);

        WriteRecovery.AfterKeyedTimeout("attachment", recovery).ShouldEndWith(
            "attachments expansion before sending it again" + Warned);

        WriteRecovery.AfterSpentKey("attachment", recovery).ShouldEndWith(
            "attachments expansion before sending it again under a new key" + Warned);
    }
}
