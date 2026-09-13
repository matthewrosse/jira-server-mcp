using JiraServerMcp.Grants;
using JiraServerMcp.Jira.Capabilities;
using JiraServerMcp.Tools;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tests;

/// <summary>
/// The tool catalogue's gate as a value: every subset of the grants, against a licensed instance, an
/// unlicensed one, and a profile with no probe recorded at all. None of this launches a process —
/// <see cref="ToolCatalogue.ToolsToRegister"/> is a pure function of a grant set and a capability
/// probe.
/// <para>
/// What this narrows: both sides of every assertion here derive from <see
/// cref="ToolCatalogue.Entries"/>, so these tests prove that <c>ToolsToRegister</c> filters the
/// table correctly — not that a given tool is filed under the right requirement. That is held by
/// <see cref="ReadmeTests"/>, which asserts every registered tool's documented grant against its
/// row. A hand-kept list of tool names here is how that assertion rotted before.
/// </para>
/// </summary>
public sealed class ToolCatalogueTests
{
    private static readonly IReadOnlyList<string> _readTools =
    [
        .. ToolCatalogue.Entries
            .Where(entry => entry.RequiredGrant is null && !entry.RequiresSoftwareLicence)
            .Select(entry => entry.ToolType.Name),
    ];

    private static readonly IReadOnlyList<string> _softwareTools =
    [
        .. ToolCatalogue.Entries
            .Where(entry => entry.RequiresSoftwareLicence)
            .Select(entry => entry.ToolType.Name),
    ];

    private static readonly JiraCapabilities _licensed = Capabilities(softwareLicensed: true);

    private static readonly JiraCapabilities _unlicensed = Capabilities(softwareLicensed: false);

    /// <summary>
    /// Every subset of the grants — the full power set, so a fifth grant doubles the rows with no
    /// edit here — against each of the three probes. Each subset reaches
    /// <see cref="GrantSet.Parse"/> through <see cref="GrantSet.Name"/>, so every row also
    /// exercises the grant-to-name-to-grant round trip.
    /// </summary>
    public static IEnumerable<TheoryDataRow<string[], JiraCapabilities?>> Matrix()
    {
        var grants = Enum.GetValues<Grant>();
        JiraCapabilities?[] probes = [_licensed, _unlicensed, null];

        for (var subset = 0; subset < 1 << grants.Length; subset++)
        {
            string[] allowed =
            [
                .. grants
                    .Where((_, index) => (subset & (1 << index)) != 0)
                    .Select(GrantSet.Name),
            ];

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
        var names = Names(ToolCatalogue.ToolsToRegister(GrantSet.Parse(allowed), capabilities));

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
        var names = Names(ToolCatalogue.ToolsToRegister(GrantSet.Parse(allowed), capabilities));

        var expected = capabilities is { SoftwareLicensed: true };

        foreach (var tool in _softwareTools)
        {
            names.Contains(tool).ShouldBe(expected);
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Every_write_tool_follows_its_own_grant(
        string[] allowed,
        JiraCapabilities? capabilities)
    {
        var grants = GrantSet.Parse(allowed);
        var names = Names(ToolCatalogue.ToolsToRegister(grants, capabilities));

        foreach (var entry in ToolCatalogue.Entries.Where(entry => entry.RequiredGrant is not null))
        {
            names.Contains(entry.ToolType.Name)
                .ShouldBe(grants.Allows(entry.RequiredGrant!.Value));
        }
    }

    [Fact]
    public void No_probe_at_all_is_treated_the_same_as_an_unlicensed_one()
    {
        var withNoProbe = Names(ToolCatalogue.ToolsToRegister(GrantSet.Parse([]), null));
        var withUnlicensedProbe = Names(ToolCatalogue.ToolsToRegister(GrantSet.Parse([]), _unlicensed));

        withNoProbe.ShouldBe(withUnlicensedProbe);
    }

    [Fact]
    public void A_built_in_tool_is_named_by_the_attribute_the_sdk_reads()
    {
        ToolCatalogue.NameOf(typeof(WhoamiTool)).ShouldBe("jira_whoami");
    }

    [Theory]
    [InlineData(typeof(DeclaresNoTool))]
    [InlineData(typeof(DeclaresTwoTools))]
    public void A_type_without_exactly_one_tool_has_no_name_to_give_a_row(Type toolType)
    {
        Should.Throw<InvalidOperationException>(() => ToolCatalogue.NameOf(toolType))
            .Message.ShouldContain("must declare exactly one");
    }

    private static JiraCapabilities Capabilities(bool softwareLicensed) =>
        new("8.20.7", "Server", softwareLicensed, DateTimeOffset.UtcNow);

    private static HashSet<string> Names(IEnumerable<Type> types) =>
        [.. types.Select(type => type.Name)];

    private sealed class DeclaresNoTool
    {
        public static string Nothing() => string.Empty;
    }

    private sealed class DeclaresTwoTools
    {
        [McpServerTool(Name = "first")]
        public static string First() => string.Empty;

        [McpServerTool(Name = "second")]
        public static string Second() => string.Empty;
    }
}
