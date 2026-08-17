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
