using JiraServerMcp.Grants;
using JiraServerMcp.Jira.Capabilities;
using JiraServerMcp.Prompts;
using JiraServerMcp.Tools;

namespace JiraServerMcp.Tests;

/// <summary>
/// The prompt surface as a value, exhaustively across grant sets. Like the tool surface it reads,
/// <see cref="PromptSurface.PromptsToRegister"/> is a pure function and nothing here launches a
/// process.
/// </summary>
public sealed class PromptSurfaceTests
{
    private static readonly JiraCapabilities _licensed = Capabilities(softwareLicensed: true);

    public static IEnumerable<TheoryDataRow<string[]>> ShortOfWhatItNeeds()
    {
        yield return new TheoryDataRow<string[]>([]);
        yield return new TheoryDataRow<string[]>(["issues:write"]);
        yield return new TheoryDataRow<string[]>(["comments:write"]);
        yield return new TheoryDataRow<string[]>(["worklogs:write"]);
        yield return new TheoryDataRow<string[]>(["worklogs:write", "links:write"]);
        yield return new TheoryDataRow<string[]>(["issues:write", "worklogs:write"]);
        yield return new TheoryDataRow<string[]>(["comments:write", "worklogs:write"]);
    }

    [Theory]
    [MemberData(nameof(ShortOfWhatItNeeds))]
    public void A_prompt_is_absent_unless_every_tool_it_names_is_registered(string[] grants)
    {
        // A procedure telling an agent to transition an issue, handed to a client that has no
        // transition tool, reads as an instruction to do something impossible.
        Registered(grants).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("issues:write", "comments:write")]
    [InlineData("issues:write", "comments:write", "worklogs:write")]
    [InlineData("comments:write", "issues:write", "links:write")]
    public void The_prompt_appears_once_its_tools_all_do(params string[] grants)
    {
        Registered(grants).ShouldBe([nameof(ImplementIssuePrompt)]);
    }

    [Fact]
    public void The_prompt_does_not_need_a_jira_software_licence()
    {
        // Nothing in the procedure touches the software API, so an unlicensed instance and a
        // profile with no probe at all both still get it.
        string[] grants = ["issues:write", "comments:write"];

        Registered(grants, Capabilities(softwareLicensed: false))
            .ShouldBe([nameof(ImplementIssuePrompt)]);

        Registered(grants, null).ShouldBe([nameof(ImplementIssuePrompt)]);
    }

    [Fact]
    public void Every_tool_a_prompt_names_is_a_tool_the_server_actually_registers()
    {
        // The gate is derived from the tool surface, so a prompt naming a tool that is not in that
        // table would be gated on something that can never be satisfied — and would vanish
        // silently rather than failing.
        var known = ToolSurface.Entries.Select(entry => entry.ToolType).ToHashSet();

        foreach (var entry in PromptSurface.Entries)
        {
            foreach (var required in entry.RequiredTools)
            {
                known.ShouldContain(
                    required,
                    $"{entry.PromptType.Name} names {required.Name}, which the tool surface does "
                    + "not register.");
            }
        }
    }

    private static IReadOnlyList<string> Registered(
        string[] grants,
        JiraCapabilities? capabilities = null) =>
    [
        .. PromptSurface.PromptsToRegister(GrantSet.Parse(grants), capabilities ?? _licensed)
            .Select(prompt => prompt.Name),
    ];

    private static JiraCapabilities Capabilities(bool softwareLicensed) =>
        new("8.20.7", "Server", softwareLicensed, DateTimeOffset.UtcNow);
}
