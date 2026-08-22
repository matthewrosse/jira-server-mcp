using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// The workflow prompt across the protocol seam (ADR-0008): whether a real client sees
/// <c>implement_issue</c> at all under a given grant set, and what <c>prompts/get</c> hands back.
/// The message is asserted by structure and by reference — the tools it names, and how the two
/// argument branches differ — never by snapshot, which would only be a second copy of the prompt.
/// </summary>
public sealed class ImplementIssuePromptProtocolTests : IAsyncLifetime
{
    private ProtocolSeam _seam = null!;

    public async ValueTask InitializeAsync() => _seam = await ProtocolSeam.StartAsync();

    public async ValueTask DisposeAsync() => await _seam.DisposeAsync();

    [Fact]
    public async Task A_client_granted_every_tool_the_procedure_calls_sees_the_prompt()
    {
        var client = await _seam.ConnectAsync("issues:write", "comments:write");

        client.ServerCapabilities.Prompts.ShouldNotBeNull();

        var prompts = await client.ListPromptsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var prompt = prompts.ShouldHaveSingleItem();

        prompt.Name.ShouldBe("implement_issue");

        // One optional argument. A caller that names no issue still gets a usable procedure.
        var argument = prompt.ProtocolPrompt.Arguments.ShouldNotBeNull().ShouldHaveSingleItem();

        argument.Name.ShouldBe("key");
        argument.Required.ShouldNotBe(true);
    }

    [Theory]
    [InlineData]
    [InlineData("issues:write")]
    [InlineData("comments:write")]
    [InlineData("comments:write", "worklogs:write")]
    public async Task A_client_short_of_those_tools_sees_no_prompt_at_all(params string[] grants)
    {
        // A procedure telling an agent to transition an issue, handed to a client with no
        // transition tool, would read as an instruction to do something impossible. With nothing
        // registered the server advertises no prompts capability at all, so such a client does not
        // see an empty list — it sees no prompt surface, and prompts/list is not even available.
        var client = await _seam.ConnectAsync(grants);

        client.ServerCapabilities.Prompts.ShouldBeNull();

        await Should.ThrowAsync<McpProtocolException>(
            () => client.ListPromptsAsync(
                cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task The_prompt_is_one_user_message_naming_the_tools_it_depends_on()
    {
        var text = await GetAsync(null);

        // MCP prompt messages carry only user and assistant roles, and a synthetic assistant turn
        // would put words in the model's mouth that it never said.
        text.ShouldContain("jira_get_issues");
        text.ShouldContain("jira_transition_issue");
        text.ShouldContain("jira_add_comment");

        // Never a status: the vocabulary is per-team, and this server does not know it.
        text.ShouldNotContain("In Progress");
        text.ShouldContain("transitions");
    }

    [Fact]
    public async Task Naming_an_issue_starts_there_and_naming_none_starts_from_the_callers_own()
    {
        var named = await GetAsync("PROJ-42");
        var unnamed = await GetAsync(null);

        named.ShouldContain("PROJ-42");
        named.ShouldNotContain("jira_my_open_issues");

        // Without a key the procedure has to say where to find one.
        unnamed.ShouldContain("jira_my_open_issues");
        unnamed.ShouldNotContain("PROJ-42");
    }

    [Fact]
    public async Task Fetching_the_prompt_asks_jira_for_nothing()
    {
        await GetAsync("PROJ-42");

        // Static text: no fetch to go stale, no failure at prompt-fetch time, and not a character
        // of Jira-authored content in the message.
        _seam.Jira.LogEntries.ShouldBeEmpty();
    }

    /// <summary>
    /// The prompt's single message, as a client receives it.
    /// </summary>
    private async Task<string> GetAsync(string? key)
    {
        var client = await _seam.ConnectAsync("issues:write", "comments:write");

        var result = await client.GetPromptAsync(
            "implement_issue",
            key is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?> { ["key"] = key },
            cancellationToken: TestContext.Current.CancellationToken);

        var message = result.Messages.ShouldHaveSingleItem();

        message.Role.ShouldBe(Role.User);

        return message.Content.ShouldBeOfType<TextContentBlock>().Text;
    }
}
