using JiraServerMcp.Tools;

namespace JiraServerMcp.Tests;

/// <summary>
/// The record of what this process has tried to write. Pure logic, so it is proven here (ADR-0008,
/// clause 3); what the tools make of it is proven at the protocol seam.
/// </summary>
public class WriteAttemptsTests
{
    [Fact]
    public void A_key_is_claimed_once_and_the_second_claim_finds_the_first()
    {
        var attempts = new WriteAttempts();

        attempts.TryBegin("jira_create_issue", "run-42", out var first).ShouldBeTrue();
        attempts.TryBegin("jira_create_issue", "run-42", out var second).ShouldBeFalse();

        second.ShouldBeSameAs(first);
    }

    [Fact]
    public void A_claim_starts_out_knowing_nothing_which_is_what_a_timeout_leaves_behind()
    {
        var attempts = new WriteAttempts();

        attempts.TryBegin("jira_create_issue", "run-42", out var attempt);

        // Nothing is reported back, as when the call never returns. The record still exists, and
        // that is the whole design: a repeat learns an attempt was made.
        attempt.Outcome.ShouldBe(WriteOutcome.Unknown);
        attempt.Detail.ShouldBeNull();

        attempts.TryBegin("jira_create_issue", "run-42", out var prior).ShouldBeFalse();
        prior.Outcome.ShouldBe(WriteOutcome.Unknown);
    }

    [Fact]
    public void A_write_that_came_back_says_what_it_produced()
    {
        var attempts = new WriteAttempts();

        attempts.TryBegin("jira_create_issue", "run-42", out var attempt);
        attempt.Succeeded("PROJ-42");

        attempts.TryBegin("jira_create_issue", "run-42", out var prior).ShouldBeFalse();

        prior.Outcome.ShouldBe(WriteOutcome.Ok);
        prior.Detail.ShouldBe("PROJ-42");
    }

    [Fact]
    public void A_write_jira_refused_is_told_apart_from_one_that_went_silent()
    {
        var attempts = new WriteAttempts();

        attempts.TryBegin("jira_create_issue", "run-42", out var attempt);
        attempt.Rejected();

        attempts.TryBegin("jira_create_issue", "run-42", out var prior).ShouldBeFalse();

        // The one ending that proves nothing was written, which is why it is worth distinguishing.
        prior.Outcome.ShouldBe(WriteOutcome.Rejected);
    }

    [Fact]
    public void One_key_used_by_two_different_tools_is_two_claims_rather_than_a_collision()
    {
        var attempts = new WriteAttempts();

        // An agent numbering the steps of a run should not have to know which of them happen to be
        // writes, nor which write tool each one reaches.
        attempts.TryBegin("jira_create_issue", "step-1", out var created).ShouldBeTrue();
        attempts.TryBegin("jira_add_comment", "step-1", out var commented).ShouldBeTrue();
        attempts.TryBegin("jira_add_worklog", "step-1", out _).ShouldBeTrue();

        created.ShouldNotBeSameAs(commented);

        attempts.TryBegin("jira_create_issue", "step-1", out var again).ShouldBeFalse();
        again.ShouldBeSameAs(created);
    }

    [Fact]
    public void Surrounding_space_does_not_make_a_key_a_different_key()
    {
        var attempts = new WriteAttempts();

        attempts.TryBegin("jira_add_comment", "run-42", out _).ShouldBeTrue();
        attempts.TryBegin("jira_add_comment", "  run-42  ", out _).ShouldBeFalse();
    }
}
