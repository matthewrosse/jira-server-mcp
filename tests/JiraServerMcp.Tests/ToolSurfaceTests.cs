using JiraServerMcp.Grants;
using JiraServerMcp.Jira.Capabilities;
using JiraServerMcp.Tools;

namespace JiraServerMcp.Tests;

/// <summary>
/// The tool surface as a value: every combination of the three grants, against a licensed
/// instance, an unlicensed one, and a profile with no probe recorded at all. None of this
/// launches a process — <see cref="ToolSurface.ToolsToRegister"/> is a pure function of a grant
/// set and a capability probe.
/// </summary>
public sealed class ToolSurfaceTests
{
    private static readonly string[] _readTools =
    [
        "WhoamiTool", "SearchTool", "GetIssueTool", "ListProjectsTool", "GetProjectTool",
        "GetCreateFieldsTool", "SearchUsersTool",
    ];

    private static readonly string[] _softwareTools =
    [
        "ListBoardsTool", "ListSprintsTool", "GetSprintIssuesTool", "GetBacklogTool",
    ];

    private static readonly JiraCapabilities _licensed = Capabilities(softwareLicensed: true);

    private static readonly JiraCapabilities _unlicensed = Capabilities(softwareLicensed: false);

    public static IEnumerable<TheoryDataRow<string[], JiraCapabilities?>> Matrix()
    {
        string[][] combinations =
        [
            [],
            ["issues:write"],
            ["comments:write"],
            ["worklogs:write"],
            ["issues:write", "comments:write"],
            ["issues:write", "worklogs:write"],
            ["comments:write", "worklogs:write"],
            ["issues:write", "comments:write", "worklogs:write"],
        ];

        JiraCapabilities?[] probes = [_licensed, _unlicensed, null];

        foreach (var allowed in combinations)
        {
            foreach (var probe in probes)
            {
                yield return new TheoryDataRow<string[], JiraCapabilities?>(allowed, probe);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void The_read_tools_are_always_registered(string[] allowed, JiraCapabilities? capabilities)
    {
        var names = Names(ToolSurface.ToolsToRegister(GrantSet.Parse(allowed), capabilities));

        foreach (var tool in _readTools)
        {
            names.ShouldContain(tool);
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void The_software_tools_appear_only_when_the_probe_says_licensed(
        string[] allowed,
        JiraCapabilities? capabilities)
    {
        var names = Names(ToolSurface.ToolsToRegister(GrantSet.Parse(allowed), capabilities));

        var expected = capabilities is { SoftwareLicensed: true };

        foreach (var tool in _softwareTools)
        {
            names.Contains(tool).ShouldBe(expected);
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Each_write_grant_registers_exactly_its_own_tools(
        string[] allowed,
        JiraCapabilities? capabilities)
    {
        var grants = GrantSet.Parse(allowed);
        var names = Names(ToolSurface.ToolsToRegister(grants, capabilities));

        var issues = grants.Allows(Grant.IssuesWrite);
        var comments = grants.Allows(Grant.CommentsWrite);
        var worklogs = grants.Allows(Grant.WorklogsWrite);

        names.Contains("CreateIssueTool").ShouldBe(issues);
        names.Contains("UpdateIssueTool").ShouldBe(issues);
        names.Contains("TransitionIssueTool").ShouldBe(issues);
        names.Contains("AddCommentTool").ShouldBe(comments);
        names.Contains("AddWorklogTool").ShouldBe(worklogs);
    }

    [Fact]
    public void No_probe_at_all_is_treated_the_same_as_an_unlicensed_one()
    {
        var withNoProbe = Names(ToolSurface.ToolsToRegister(GrantSet.Parse([]), null));
        var withUnlicensedProbe = Names(ToolSurface.ToolsToRegister(GrantSet.Parse([]), _unlicensed));

        withNoProbe.ShouldBe(withUnlicensedProbe);
    }

    private static JiraCapabilities Capabilities(bool softwareLicensed) =>
        new("8.20.7", "Server", softwareLicensed, DateTimeOffset.UtcNow);

    private static HashSet<string> Names(IEnumerable<Type> types) =>
        [.. types.Select(type => type.Name)];
}
