using System.Diagnostics;
using JiraServerMcp.Jira;
using JiraServerMcp.Profiles;
using JiraServerMcp.Tools;
using ModelContextProtocol.Protocol;

namespace JiraServerMcp.Tests;

/// <summary>
/// The client gives every call thirty seconds. What happens when that runs out is the tool's
/// problem: an agent that receives a raw protocol error learns nothing it can act on.
/// </summary>
public sealed class WhoamiToolTests
{
    [Fact]
    public async Task A_jira_that_never_answers_becomes_an_error_the_agent_can_act_on()
    {
        var tool = Tool(TimeSpan.FromMilliseconds(200));

        var result = await tool.WhoamiAsync(TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);
        Text(result).ShouldContain("did not answer");
        Text(result).ShouldContain("work");
    }

    [Fact]
    public async Task A_call_the_caller_abandoned_is_not_reported_as_a_jira_failure()
    {
        // The agent hung up. Answering it with a description of a Jira that is not broken would
        // be a lie, and there is nobody left to read it anyway.
        var tool = Tool(TimeSpan.FromSeconds(30));

        using var abandoned = new CancellationTokenSource();

        await abandoned.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => tool.WhoamiAsync(abandoned.Token));
    }

    private static WhoamiTool Tool(TimeSpan timeout) =>
        new(
            new JiraClient(new HttpClient(new NeverAnswers())
            {
                BaseAddress = new Uri("https://jira.example.com", UriKind.Absolute),
                Timeout = timeout,
            }),
            new ServedProfile("work"));

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;

    private sealed class NeverAnswers : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);

            throw new UnreachableException();
        }
    }
}
