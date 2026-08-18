using System.Net;
using System.Text.Json;
using JiraServerMcp.Jira.Errors;
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
        attempt.Succeeded("PROJ-42", Structure());

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

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Jira_reading_the_request_and_refusing_it_proves_nothing_was_written(
        HttpStatusCode status)
    {
        var attempts = new WriteAttempts();

        attempts.TryBegin("jira_create_issue", "run-42", out var attempt);

        await Should.ThrowAsync<JiraApiException>(
            () => WriteAttempts.SendAsync<string>(attempt, () => throw Failed(status)));

        attempt.Outcome.ShouldBe(WriteOutcome.Rejected);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_status_that_does_not_prove_jira_stayed_out_of_it_leaves_the_outcome_unknown(
        HttpStatusCode status)
    {
        var attempts = new WriteAttempts();

        attempts.TryBegin("jira_create_issue", "run-42", out var attempt);

        await Should.ThrowAsync<JiraApiException>(
            () => WriteAttempts.SendAsync<string>(attempt, () => throw Failed(status)));

        // A proxy answering 502 in front of a Jira that already committed the write looks exactly
        // like a refusal from here. Calling it one would tell the next call, with false certainty,
        // that nothing was written — and it would write again.
        attempt.Outcome.ShouldBe(WriteOutcome.Unknown);
    }

    [Fact]
    public async Task A_write_that_never_answered_at_all_leaves_the_outcome_unknown()
    {
        var attempts = new WriteAttempts();

        attempts.TryBegin("jira_create_issue", "run-42", out var attempt);

        await Should.ThrowAsync<HttpRequestException>(
            () => WriteAttempts.SendAsync<string>(
                attempt,
                () => throw new HttpRequestException("the connection was reset")));

        attempt.Outcome.ShouldBe(WriteOutcome.Unknown);
    }

    [Fact]
    public void A_replay_hands_back_the_structured_half_the_first_call_answered_with()
    {
        var attempts = new WriteAttempts();

        attempts.TryBegin("jira_add_comment", "run-42", out var attempt);
        attempt.Succeeded("comment 10200 on PROJ-42", Structure());

        attempts.TryBegin("jira_add_comment", "run-42", out var prior);

        // A caller that read an identifier out of the first answer finds the same identifier in
        // the second, which is the whole of what "already done" should mean.
        prior.Structure.ShouldNotBeNull().GetProperty("commentId").GetString().ShouldBe("10200");
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

    [Fact]
    public void Two_keys_that_are_nothing_but_space_are_the_same_key_which_is_why_tools_refuse_them()
    {
        // The trim that makes "  run-42  " one key also makes " " and "\n" one key. A tool that
        // accepted either would refuse a genuinely different write as a replay of the first, so
        // the tools require a key with something in it.
        var attempts = new WriteAttempts();

        attempts.TryBegin("jira_add_comment", " ", out _).ShouldBeTrue();
        attempts.TryBegin("jira_add_comment", "\n", out _).ShouldBeFalse();
    }

    private static JiraApiException Failed(HttpStatusCode status) =>
        new(status, "/rest/api/2/issue", [], new Dictionary<string, string>());

    private static JsonElement Structure() =>
        JsonSerializer.Deserialize<JsonElement>("""{"outcome":"ok","commentId":"10200"}""");
}
