using System.Net;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Profiles;
using JiraServerMcp.Tools;
using ModelContextProtocol.Protocol;

namespace JiraServerMcp.Tests;

/// <summary>
/// The three ways a tool call fails, and the one way it succeeds. Every tool answers an agent
/// that cannot read this server's log, so the vocabulary lives here rather than in each tool.
/// </summary>
public sealed class ToolCallTests
{
    [Fact]
    public async Task Work_that_answers_becomes_the_result_text()
    {
        var result = await Run(() => Task.FromResult("display name: Ada"));

        result.IsError.ShouldNotBe(true);
        Text(result).ShouldBe("display name: Ada");
    }

    [Fact]
    public async Task A_jira_failure_is_described_with_the_profile_and_the_operation()
    {
        var result = await Run(() => throw new JiraApiException(
            HttpStatusCode.Unauthorized,
            "/rest/api/2/myself",
            [],
            new Dictionary<string, string>()));

        result.IsError.ShouldBe(true);
        Text(result).ShouldContain("work");
        Text(result).ShouldContain("jira-server-mcp auth login work");
    }

    [Fact]
    public async Task An_unreachable_jira_says_so_with_the_clause_the_tool_supplied()
    {
        var result = await Run(
            () => throw new HttpRequestException("no such host"),
            whenUnreachable: ", and nothing was written");

        result.IsError.ShouldBe(true);
        Text(result).ShouldBe("Could not reach Jira, and nothing was written: no such host");
    }

    [Fact]
    public async Task A_jira_that_did_not_answer_names_the_profile_and_the_advice_the_tool_supplied()
    {
        var result = await Run(
            () => throw new OperationCanceledException(),
            whenTimedOut: ", and the request was given up. Asking again usually helps.");

        result.IsError.ShouldBe(true);
        Text(result).ShouldBe(
            "Jira did not answer for profile 'work' in time, and the request was given up. "
            + "Asking again usually helps.");
    }

    [Fact]
    public async Task A_call_the_caller_abandoned_is_not_reported_as_a_jira_failure()
    {
        // The agent hung up. There is nobody left to read a description of a Jira that is fine.
        using var abandoned = new CancellationTokenSource();

        await abandoned.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => ToolCall.RunAsync(
                new ServedProfile("work"),
                "jira_whoami",
                whenUnreachable: string.Empty,
                whenTimedOut: ". Asking again usually helps.",
                () => throw new OperationCanceledException(),
                abandoned.Token));
    }

    [Fact]
    public async Task A_describe_api_failure_clause_overrides_the_default_wording()
    {
        var result = await ToolCall.RunAsync(
            new ServedProfile("work"),
            "jira_whoami",
            whenUnreachable: string.Empty,
            whenTimedOut: string.Empty,
            work: () => throw new JiraApiException(
                HttpStatusCode.BadRequest,
                "/rest/api/2/issue",
                [],
                new Dictionary<string, string>()),
            CancellationToken.None,
            describeApiFailure: _ => "the field map was rejected");

        result.IsError.ShouldBe(true);
        Text(result).ShouldBe("the field map was rejected");
    }

    [Fact]
    public async Task A_step_that_succeeds_hands_back_its_value()
    {
        var step = await ToolCall.StepAsync(
            new ServedProfile("work"),
            "reading the transitions available on PROJ-1",
            whenUnreachable: string.Empty,
            whenTimedOut: string.Empty,
            work: () => Task.FromResult(3),
            CancellationToken.None);

        step.Failed.ShouldBeFalse();
        step.Value.ShouldBe(3);
    }

    [Fact]
    public async Task A_step_that_fails_hands_back_a_finished_error_result_instead_of_a_value()
    {
        var step = await ToolCall.StepAsync<int>(
            new ServedProfile("work"),
            "reading the transitions available on PROJ-1",
            whenUnreachable: ", and PROJ-1 was not transitioned",
            whenTimedOut: string.Empty,
            work: () => throw new HttpRequestException("no such host"),
            CancellationToken.None);

        step.Failed.ShouldBeTrue();
        step.Error.IsError.ShouldBe(true);
        Text(step.Error).ShouldBe(
            "Could not reach Jira, and PROJ-1 was not transitioned: no such host");
    }

    [Fact]
    public async Task A_step_honours_its_own_describe_api_failure_clause()
    {
        var step = await ToolCall.StepAsync<int>(
            new ServedProfile("work"),
            "reading the transitions available on PROJ-1",
            whenUnreachable: string.Empty,
            whenTimedOut: string.Empty,
            work: () => throw new JiraApiException(
                HttpStatusCode.BadRequest,
                "/rest/api/2/issue/PROJ-1/transitions",
                [],
                new Dictionary<string, string>()),
            CancellationToken.None,
            describeApiFailure: _ => "Nothing was transitioned: PROJ-1 is as it was.");

        step.Failed.ShouldBeTrue();
        Text(step.Error).ShouldBe("Nothing was transitioned: PROJ-1 is as it was.");
    }

    private static Task<CallToolResult> Run(
        Func<Task<string>> work,
        string whenUnreachable = "",
        string whenTimedOut = ", and the request was given up.") =>
        ToolCall.RunAsync(
            new ServedProfile("work"),
            "jira_whoami",
            whenUnreachable,
            whenTimedOut,
            work,
            CancellationToken.None);

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;
}
