using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// Transitioning, commenting, and logging work across the protocol seam: what an agent naming a
/// transition gets, what the faked Jira received, and what an agent is told when it guessed wrong.
/// </summary>
public sealed class TransitionCommentWorklogProtocolTests : IAsyncLifetime
{
    private const string TransitionsPayload = """
        {
          "transitions": [
            { "id": "21", "name": "Start Progress", "to": { "name": "In Progress" } },
            {
              "id": "31",
              "name": "Done",
              "to": { "name": "Done" },
              "fields": { "resolution": { "name": "Resolution", "required": true } }
            }
          ]
        }
        """;

    private const string CommentPayload = """
        {
          "id": "10101",
          "created": "2026-08-16T10:00:00.000+0000",
          "body": "Whatever the agent wrote, echoed back at length by Jira."
        }
        """;

    private const string WorklogPayload = """
        { "id": "10200", "timeSpent": "3h 30m", "started": "2026-08-16T09:00:00.000+0000" }
        """;

    private ProtocolSeam _seam = null!;

    public async ValueTask InitializeAsync() => _seam = await ProtocolSeam.StartAsync();

    public async ValueTask DisposeAsync() => await _seam.DisposeAsync();

    [Fact]
    public async Task A_transition_named_in_any_casing_reaches_jira_as_its_identifier()
    {
        StubTransitions();
        StubTransition(204);

        var text = await CallAsync(
            await _seam.ConnectAsync("issues:write"),
            "jira_transition_issue",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["transition"] = "  sTaRt pRoGrEsS  ",
            });

        text.ShouldContain("PROJ-42");
        text.ShouldContain("Start Progress");

        var performed = Requests()[1];

        performed.Method.ShouldBe("POST");
        performed.Path.ShouldBe("/rest/api/2/issue/PROJ-42/transitions");
        Body(performed).GetProperty("transition").GetProperty("id").GetString().ShouldBe("21");
    }

    [Fact]
    public async Task A_transition_is_resolved_at_the_moment_it_is_made_and_not_before()
    {
        StubTransitions();
        StubTransition(204);

        await CallAsync(
            await _seam.ConnectAsync("issues:write"),
            "jira_transition_issue",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["transition"] = "Done",
                ["fields"] = new Dictionary<string, object?>
                {
                    ["resolution"] = new Dictionary<string, object?> { ["name"] = "Fixed" },
                },
            });

        // The list an issue read handed over may be stale by now, so it is read again here.
        Requests()[0].Method.ShouldBe("GET");
        Requests()[0].Path.ShouldBe("/rest/api/2/issue/PROJ-42/transitions");
    }

    [Fact]
    public async Task A_transition_name_jira_does_not_offer_comes_back_with_the_ones_it_does()
    {
        StubTransitions();

        var text = await FailedCallAsync(
            await _seam.ConnectAsync("issues:write"),
            "jira_transition_issue",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["transition"] = "Close",
            });

        // The single most useful thing to tell an agent that guessed.
        text.ShouldContain("Close");
        text.ShouldContain("Start Progress");
        text.ShouldContain("Done");

        // And nothing was transitioned on the strength of a guess.
        Requests().Count.ShouldBe(1);
    }

    [Fact]
    public async Task One_name_on_two_transitions_moves_the_issue_nowhere()
    {
        // A workflow may offer a global transition and a local one under one name, going to
        // different statuses. Picking either would move the issue somewhere nobody asked for.
        StubTransitions(200, """
            {
              "transitions": [
                { "id": "31", "name": "Done", "to": { "name": "Done" } },
                { "id": "41", "name": "Done", "to": { "name": "Closed" } }
              ]
            }
            """);

        StubTransition(204);

        var text = await FailedCallAsync(
            await _seam.ConnectAsync("issues:write"),
            "jira_transition_issue",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["transition"] = "Done",
            });

        text.ShouldContain("Closed");
        Requests().Count(request => request.Method == "POST").ShouldBe(0);
    }

    [Fact]
    public async Task A_failure_reading_the_transitions_never_claims_a_transition_was_sent()
    {
        StubTransitions(404, """{ "errorMessages": ["Issue Does Not Exist"], "errors": {} }""");

        var text = await FailedCallAsync(
            await _seam.ConnectAsync("issues:write"),
            "jira_transition_issue",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["transition"] = "Done",
            });

        // Nothing was written, so nothing may have landed, and the agent must not be sent reading
        // the issue back to find out.
        text.ShouldNotContain("was sent once");
        text.ShouldContain("Nothing was transitioned");

        Requests().Count(request => request.Method == "POST").ShouldBe(0);
    }

    [Fact]
    public async Task A_transition_carrying_a_comment_and_screen_fields_is_one_request()
    {
        StubTransitions();
        StubTransition(204);

        await CallAsync(
            await _seam.ConnectAsync("issues:write"),
            "jira_transition_issue",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["transition"] = "Done",
                ["comment"] = "Fixed in the release branch.",
                ["fields"] = new Dictionary<string, object?>
                {
                    ["resolution"] = new Dictionary<string, object?> { ["name"] = "Fixed" },
                },
            });

        // One resolution, one POST: a transition demanding a resolution must succeed in one call.
        var performed = Requests().Single(request => request.Method is "POST");

        var body = Body(performed);

        body.GetProperty("fields").GetProperty("resolution").GetProperty("name").GetString()
            .ShouldBe("Fixed");

        body.GetProperty("update").GetProperty("comment").EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("add").GetProperty("body").GetString()
            .ShouldBe("Fixed in the release branch.");
    }

    [Fact]
    public async Task A_transition_jira_could_not_answer_is_asked_exactly_once()
    {
        StubTransitions();
        StubTransition(503);

        await FailedCallAsync(
            await _seam.ConnectAsync("issues:write"),
            "jira_transition_issue",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["transition"] = "Done",
            });

        Requests().Count(request => request.Method is "POST").ShouldBe(1);
    }

    [Fact]
    public async Task A_comment_comes_back_as_an_identifier_and_a_timestamp_and_nothing_else()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42/comment").UsingPost())
            .RespondWith(JiraResponse.Json(201, CommentPayload));

        var text = await CallAsync(
            await _seam.ConnectAsync("comments:write"),
            "jira_add_comment",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["body"] = "It has done it twice today.",
            });

        text.ShouldContain("10101");
        text.ShouldContain("2026-08-16T10:00:00.000+0000");

        // The agent wrote the body; echoing it back is context spent on nothing.
        text.ShouldNotContain("echoed back at length");

        Body(Requests().ShouldHaveSingleItem()).GetProperty("body").GetString()
            .ShouldBe("It has done it twice today.");
    }

    [Fact]
    public async Task An_empty_comment_is_refused_before_anything_is_sent()
    {
        var text = await FailedCallAsync(
            await _seam.ConnectAsync("comments:write"),
            "jira_add_comment",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["body"] = "   ",
            });

        text.ShouldContain("PROJ-42");
        Requests().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_comment_jira_could_not_answer_is_asked_exactly_once()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42/comment").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(503));

        await FailedCallAsync(
            await _seam.ConnectAsync("comments:write"),
            "jira_add_comment",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["body"] = "It has done it twice today.",
            });

        Requests().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Work_is_logged_in_jiras_own_duration_syntax()
    {
        StubWorklog(201);

        var text = await CallAsync(
            await _seam.ConnectAsync("worklogs:write"),
            "jira_add_worklog",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["timeSpent"] = "3h 30m",
                ["started"] = "2026-08-16T09:00:00+02:00",
                ["comment"] = "Tracked down the 500s.",
            });

        text.ShouldContain("3h 30m");

        var body = Body(Requests().ShouldHaveSingleItem());

        // Sent as the caller wrote it: Jira alone decides how long a working day is.
        body.GetProperty("timeSpent").GetString().ShouldBe("3h 30m");
        body.GetProperty("started").GetString().ShouldBe("2026-08-16T09:00:00.000+0200");
        body.GetProperty("comment").GetString().ShouldBe("Tracked down the 500s.");
    }

    [Fact]
    public async Task A_duration_reaches_jira_without_the_spaces_around_it()
    {
        StubWorklog(201);

        await CallAsync(
            await _seam.ConnectAsync("worklogs:write"),
            "jira_add_worklog",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["timeSpent"] = "  3h 30m  ",
            });

        // Checked trimmed and sent trimmed, or the check passes something Jira still refuses.
        Body(Requests().ShouldHaveSingleItem()).GetProperty("timeSpent").GetString()
            .ShouldBe("3h 30m");
    }

    [Fact]
    public async Task A_duration_jira_could_not_read_is_refused_before_anything_is_sent()
    {
        var text = await FailedCallAsync(
            await _seam.ConnectAsync("worklogs:write"),
            "jira_add_worklog",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["timeSpent"] = "three and a half hours",
            });

        // An example is what makes this fixable without a round trip.
        text.ShouldContain("3h 30m");

        Requests().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_start_time_jira_could_not_read_is_refused_before_anything_is_sent()
    {
        var text = await FailedCallAsync(
            await _seam.ConnectAsync("worklogs:write"),
            "jira_add_worklog",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["timeSpent"] = "3h 30m",
                ["started"] = "yesterday",
            });

        text.ShouldContain("2026");
        Requests().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_worklog_jira_could_not_answer_is_asked_exactly_once()
    {
        StubWorklog(503);

        await FailedCallAsync(
            await _seam.ConnectAsync("worklogs:write"),
            "jira_add_worklog",
            new Dictionary<string, object?>
            {
                ["key"] = "PROJ-42",
                ["timeSpent"] = "3h 30m",
            });

        Requests().Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_three_tools_say_honestly_whether_they_read_and_whether_they_destroy()
    {
        var client = await _seam.ConnectAsync("issues:write", "comments:write", "worklogs:write");

        var tools = await client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // A transition moves an issue out of the status it was in; adding a comment or a worklog
        // takes nothing away from what is already there.
        Annotations(tools, "jira_transition_issue").ReadOnlyHint.ShouldBe(false);
        Annotations(tools, "jira_transition_issue").DestructiveHint.ShouldBe(true);
        Annotations(tools, "jira_add_comment").ReadOnlyHint.ShouldBe(false);
        Annotations(tools, "jira_add_comment").DestructiveHint.ShouldBe(false);
        Annotations(tools, "jira_add_worklog").ReadOnlyHint.ShouldBe(false);
        Annotations(tools, "jira_add_worklog").DestructiveHint.ShouldBe(false);
    }

    private static ToolAnnotations Annotations(IList<McpClientTool> tools, string name) =>
        tools.Single(tool => tool.Name == name).ProtocolTool.Annotations.ShouldNotBeNull();

    private static JsonElement Body(IRequestMessage request) =>
        JsonDocument.Parse(request.Body ?? string.Empty).RootElement;

    private void StubTransitions() => StubTransitions(200, TransitionsPayload);

    private void StubTransitions(int status, string payload) =>
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42/transitions").UsingGet())
            .RespondWith(JiraResponse.Json(status, payload));

    private void StubTransition(int status) =>
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42/transitions").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(status));

    private void StubWorklog(int status) =>
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42/worklog").UsingPost())
            .RespondWith(JiraResponse.Json(status, WorklogPayload));

    private IReadOnlyList<IRequestMessage> Requests() =>
    [
        .. _seam.Jira.LogEntries.Select(entry => entry.RequestMessage).OfType<IRequestMessage>(),
    ];

    private async Task<string> CallAsync(
        McpClient client,
        string tool,
        IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(
            tool,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldNotBe(true);

        return result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;
    }

    private async Task<string> FailedCallAsync(
        McpClient client,
        string tool,
        IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(
            tool,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        return result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;
    }
}
