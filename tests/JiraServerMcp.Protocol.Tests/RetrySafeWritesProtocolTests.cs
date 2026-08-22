using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// The idempotency key across the protocol seam (ADR-0008): what a second call carrying a spent
/// key is told, and — the point of the whole feature — that it does not write. The case that
/// matters most is the one nobody can reconstruct afterwards: a first attempt that was sent and
/// never came back.
/// </summary>
public sealed class RetrySafeWritesProtocolTests : IAsyncLifetime
{
    private ProtocolSeam _seam = null!;

    private McpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _seam = await ProtocolSeam.StartAsync();

        _client = await _seam.ConnectAsync(
            "issues:write", "comments:write", "worklogs:write");
    }

    public async ValueTask DisposeAsync() => await _seam.DisposeAsync();

    [Fact]
    public async Task Every_write_that_takes_a_key_offers_it_and_no_other_write_does()
    {
        var tools = await _client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        foreach (var name in new[] { "jira_create_issue", "jira_add_comment", "jira_add_worklog" })
        {
            tools.Single(tool => tool.Name == name)
                .JsonSchema.GetProperty("properties")
                .TryGetProperty("idempotencyKey", out _)
                .ShouldBeTrue($"{name} should take an idempotency key.");
        }

        // Repeating these changes nothing beyond an audit-trail entry, and a key on every write
        // lengthens every description for something most calls will not use.
        foreach (var name in new[] { "jira_update_issue", "jira_transition_issue" })
        {
            tools.Single(tool => tool.Name == name)
                .JsonSchema.GetProperty("properties")
                .TryGetProperty("idempotencyKey", out _)
                .ShouldBeFalse($"{name} is idempotent in effect and should take no key.");
        }
    }

    [Fact]
    public async Task A_second_create_under_a_spent_key_writes_nothing_and_names_what_the_first_made()
    {
        StubCreate();

        var first = await CreateAsync("run-42-step-1");
        var second = await CreateAsync("run-42-step-1");

        TextOf(first).ShouldContain("Created PROJ-42");

        second.IsError.ShouldNotBe(true);
        TextOf(second).ShouldContain("already used");
        TextOf(second).ShouldContain("PROJ-42");

        // A loop repeating a step wants "that is already done", so the structured half carries the
        // key the first call produced rather than an error to handle.
        second.StructuredContent.ShouldNotBeNull()
            .GetProperty("key").GetString().ShouldBe("PROJ-42");

        Posts("/rest/api/2/issue").ShouldBe(1);
    }

    [Fact]
    public async Task A_first_attempt_that_never_came_back_leaves_a_record_and_the_repeat_refuses()
    {
        // The case the feature exists for. Jira drops the connection without answering, which is
        // what a timeout leaves behind too: the write may have committed, and nothing in the
        // answer says whether it did.
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue").UsingPost())
            .RespondWith(Response.Create().WithFault(FaultType.EMPTY_RESPONSE));

        var first = await CreateAsync("run-42-step-1");

        first.IsError.ShouldBe(true);

        var attempted = Posts("/rest/api/2/issue");

        // Jira is healthy again, so nothing but the record itself can stop a second write.
        _seam.Jira.Reset();
        StubCreate();

        var second = await CreateAsync("run-42-step-1");

        second.IsError.ShouldBe(true);
        TextOf(second).ShouldContain("outcome is unknown");
        TextOf(second).ShouldContain("Nothing was written again");
        TextOf(second).ShouldContain("jira_search");

        Posts("/rest/api/2/issue").ShouldBe(0);
        attempted.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task A_key_spent_on_a_create_jira_rejected_is_not_reusable_for_the_corrected_call()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue").UsingPost())
            .RespondWith(JiraResponse.Json(400, """{"errorMessages":[],"errors":{"summary":"is required"}}"""));

        var first = await CreateAsync("run-42-step-1");

        first.IsError.ShouldBe(true);

        _seam.Jira.Reset();
        StubCreate();

        var second = await CreateAsync("run-42-step-1");

        // Nothing was written then either, so this is safe to repeat — but a key names one
        // attempt, and letting it be respent would make "already attempted" mean two things.
        second.IsError.ShouldBe(true);
        TextOf(second).ShouldContain("rejected");
        TextOf(second).ShouldContain("new key");

        Posts("/rest/api/2/issue").ShouldBe(0);
    }

    [Fact]
    public async Task A_gateway_error_is_not_proof_that_jira_stayed_out_of_it()
    {
        // A proxy in front of a Jira that has already committed the create answers 502, and from
        // here that is indistinguishable from a refusal. Recording it as one would send the next
        // call away to write again under a new key — the duplicate this feature exists to prevent.
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue").UsingPost())
            .RespondWith(JiraResponse.Json(502, """{"errorMessages":["Bad gateway"],"errors":{}}"""));

        var first = await CreateAsync("run-42-step-1");

        first.IsError.ShouldBe(true);

        _seam.Jira.Reset();
        StubCreate();

        var second = await CreateAsync("run-42-step-1");

        second.IsError.ShouldBe(true);
        TextOf(second).ShouldContain("outcome is unknown");
        TextOf(second).ShouldNotContain("rejected");

        Posts("/rest/api/2/issue").ShouldBe(0);
    }

    [Fact]
    public async Task A_replay_carries_the_identifiers_the_first_answer_carried()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42/comment").UsingPost())
            .RespondWith(JiraResponse.Json(201, """{ "id": "10200", "created": "2026-08-18T09:00:00.000+0200" }"""));

        var first = await CommentAsync("run-42-step-2");
        var second = await CommentAsync("run-42-step-2");

        second.IsError.ShouldNotBe(true);

        // A caller that read the comment id out of the first answer finds it in the second.
        var structure = second.StructuredContent.ShouldNotBeNull();

        structure.GetProperty("commentId").GetString().ShouldBe("10200");
        structure.GetProperty("key").GetString().ShouldBe("PROJ-42");
        first.StructuredContent.ShouldNotBeNull().GetProperty("commentId").GetString()
            .ShouldBe("10200");

        Posts("/rest/api/2/issue/PROJ-42/comment").ShouldBe(1);
    }

    [Fact]
    public async Task A_key_of_nothing_but_space_is_refused_rather_than_claiming_the_empty_key()
    {
        StubCreate();

        // Trimming is what makes "  run-42  " the same key as "run-42"; it also makes every
        // whitespace-only key the same key, so a tool that accepted one would refuse an unrelated
        // write as a replay of it.
        await CreateAsync(" ");
        await CreateAsync("\n");

        Posts("/rest/api/2/issue").ShouldBe(2);
    }

    [Fact]
    public async Task One_key_across_two_write_tools_is_two_attempts_rather_than_a_collision()
    {
        StubCreate();

        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42/comment").UsingPost())
            .RespondWith(JiraResponse.Json(201, """{ "id": "10200", "created": "2026-08-18T09:00:00.000+0200" }"""));

        var created = await CreateAsync("step-1");

        var commented = await _client.CallToolAsync(
            "jira_add_comment",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["body"] = "Looked at it.",
                ["idempotencyKey"] = "step-1",
            },
            cancellationToken: TestContext.Current.CancellationToken);

        // An agent numbering the steps of a run should not have to know which of them are writes,
        // nor which write tool each one reaches.
        created.IsError.ShouldNotBe(true);
        commented.IsError.ShouldNotBe(true);
        TextOf(commented).ShouldContain("Added comment 10200");

        Posts("/rest/api/2/issue").ShouldBe(1);
        Posts("/rest/api/2/issue/PROJ-42/comment").ShouldBe(1);
    }

    [Fact]
    public async Task A_write_with_no_key_is_repeated_exactly_as_it_always_was()
    {
        StubCreate();

        await CreateAsync(idempotencyKey: null);
        await CreateAsync(idempotencyKey: null);

        // The key is opt-in. Without one there is nothing to remember and nothing to refuse.
        Posts("/rest/api/2/issue").ShouldBe(2);
    }

    private async Task<CallToolResult> CreateAsync(string? idempotencyKey)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["projectKey"] = "PROJ",
            ["issueType"] = "Bug",
            ["summary"] = "It fell over",
        };

        if (idempotencyKey is not null)
        {
            arguments["idempotencyKey"] = idempotencyKey;
        }

        return await _client.CallToolAsync(
            "jira_create_issue",
            arguments,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private async Task<CallToolResult> CommentAsync(string idempotencyKey) =>
        await _client.CallToolAsync(
            "jira_add_comment",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["body"] = "Looked at it.",
                ["idempotencyKey"] = idempotencyKey,
            },
            cancellationToken: TestContext.Current.CancellationToken);

    private static string TextOf(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;

    private int Posts(string path) =>
        _seam.Jira.LogEntries
            .Select(entry => entry.RequestMessage)
            .OfType<WireMock.IRequestMessage>()
            .Count(request =>
                request.Path == path
                && request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase));

    private void StubCreate() =>
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue").UsingPost())
            .RespondWith(JiraResponse.Json(201, """{ "id": "10500", "key": "PROJ-42" }"""));
}
