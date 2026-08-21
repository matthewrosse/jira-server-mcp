using System.Text.Json;
using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;
using JiraServerMcp.Tools;
using ModelContextProtocol.Protocol;

namespace JiraServerMcp.Tests;

/// <summary>
/// The five steps a keyed write owes its caller, in the one order that makes a retry safe: the
/// whitespace check that decides whether there is a key at all, the claim, the replay, the send,
/// the recording. Under ADR-0008 clause 3 this is proven here rather than at the protocol seam —
/// the write is a delegate, so the seam is <see cref="RetrySafeWrite"/>'s own signature and no
/// HTTP is staged to reach it.
/// </summary>
public sealed class RetrySafeWriteTests
{
    [Fact]
    public async Task A_key_spent_by_a_write_that_succeeded_replays_its_detail_as_a_success()
    {
        var attempts = new WriteAttempts();

        await Run(attempts, "k", () => Task.FromResult(
            new Written(new Rendered("Added comment 10200 to PROJ-42.", Structure()), "comment 10200 on PROJ-42")));

        var replay = await Run(attempts, "k", NeverCalled);

        replay.IsError.ShouldNotBe(true);
        Text(replay).ShouldBe(
            "This key was already used by a comment that succeeded: comment 10200 on PROJ-42. "
            + "Nothing was written again.");
    }

    /// <summary>
    /// The identifier the first call answered with is the identifier the second answers with,
    /// which is the whole of what "already done" should mean to a caller reading a field.
    /// </summary>
    [Fact]
    public async Task A_replayed_success_hands_back_the_first_call_s_structure()
    {
        var attempts = new WriteAttempts();

        await Run(attempts, "k", () => Task.FromResult(
            new Written(new Rendered("Added comment 10200 to PROJ-42.", Structure()), "comment 10200 on PROJ-42")));

        var replay = await Run(attempts, "k", NeverCalled);

        replay.StructuredContent!.Value.GetProperty("commentId").GetString().ShouldBe("10200");
    }

    [Fact]
    public async Task A_key_spent_by_a_write_jira_refused_replays_as_an_error_naming_a_new_key()
    {
        var attempts = new WriteAttempts();

        await Run(attempts, "k", () => throw Refusal());

        var replay = await Run(attempts, "k", NeverCalled);

        replay.IsError.ShouldBe(true);
        Text(replay).ShouldContain("that Jira rejected");
        Text(replay).ShouldContain("under a new key");
    }

    [Fact]
    public async Task A_key_spent_by_a_write_that_never_came_back_replays_with_the_tool_s_advice()
    {
        var attempts = new WriteAttempts();

        await Run(attempts, "k", () => throw new OperationCanceledException());

        var replay = await Run(attempts, "k", NeverCalled);

        replay.IsError.ShouldBe(true);
        Text(replay).ShouldBe(
            "This key was already used by a comment whose outcome is unknown: it was sent once "
            + "and no answer came back. Nothing was written again. Read the issue first.");
    }

    /// <summary>
    /// The ordering the whole design rests on. A write that threw something no arm can read as a
    /// refusal must still have left its key claimed, because that is exactly the case in which
    /// Jira may have committed it.
    /// </summary>
    [Fact]
    public async Task A_write_that_threw_before_answering_still_left_its_key_claimed()
    {
        var attempts = new WriteAttempts();

        await Run(attempts, "k", () => throw new HttpRequestException("no such host"));

        attempts.TryBegin("jira_add_comment", "k", out var prior).ShouldBeFalse();
        prior.Outcome.ShouldBe(WriteOutcome.Unknown);
    }

    /// <summary>
    /// The claim happens before the write is sent rather than after it comes back, so a second
    /// call arriving while the first is still in flight is answered rather than sent.
    /// </summary>
    [Fact]
    public async Task A_second_call_arriving_while_the_first_is_still_in_flight_writes_nothing()
    {
        var attempts = new WriteAttempts();
        var inFlight = new TaskCompletionSource();

        var first = Run(attempts, "k", async () =>
        {
            await inFlight.Task;

            return new Written(new Rendered("Added comment 10200 to PROJ-42."), "comment 10200");
        });

        var replay = await Run(attempts, "k", NeverCalled);

        Text(replay).ShouldContain("outcome is unknown");

        inFlight.SetResult();
        await first;
    }

    [Fact]
    public async Task A_key_of_nothing_but_whitespace_is_no_key_at_all()
    {
        var attempts = new WriteAttempts();

        await Run(attempts, "   ", Wrote);

        var second = await Run(attempts, "   ", Wrote);

        second.IsError.ShouldNotBe(true);
        Text(second).ShouldBe("Added comment 10200 to PROJ-42.");
    }

    [Fact]
    public async Task No_key_at_all_leaves_the_write_unrecorded_and_repeatable()
    {
        var attempts = new WriteAttempts();

        await Run(attempts, key: null, Wrote);

        Text(await Run(attempts, key: null, Wrote)).ShouldBe("Added comment 10200 to PROJ-42.");
    }

    private static Task<CallToolResult> Run(
        WriteAttempts attempts,
        string? key,
        Func<Task<Written>> write) =>
        RetrySafeWrite.RunAsync(
            attempts,
            "jira_add_comment",
            key,
            noun: "comment",
            howToCheck: "Read the issue first.",
            new ServedProfile("work"),
            "commenting on PROJ-42",
            whenUnreachable: ", and PROJ-42 was not commented on",
            whenTimedOut: ". The comment was sent once and was not repeated.",
            write,
            CancellationToken.None);

    private static Task<Written> Wrote() => Task.FromResult(
        new Written(new Rendered("Added comment 10200 to PROJ-42."), "comment 10200 on PROJ-42"));

    private static Task<Written> NeverCalled() =>
        throw new Xunit.Sdk.XunitException("A spent key must not reach the write.");

    private static JiraServerMcp.Jira.Errors.JiraApiException Refusal() =>
        new(
            System.Net.HttpStatusCode.BadRequest,
            "/rest/api/2/issue/PROJ-42/comment",
            [],
            new Dictionary<string, string>());

    private static JsonElement Structure() =>
        JsonDocument.Parse("""{"outcome":"ok","commentId":"10200"}""").RootElement;

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;
}
