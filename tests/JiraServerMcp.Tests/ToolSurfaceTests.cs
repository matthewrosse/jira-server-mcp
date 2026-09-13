using JiraServerMcp.Grants;
using JiraServerMcp.Jira.Capabilities;
using JiraServerMcp.Profiles;
using JiraServerMcp.Tools;

namespace JiraServerMcp.Tests;

/// <summary>
/// The tool surface as a value: the gated tool catalogue, plus this profile's operator-defined
/// queries, and nothing else. The gate itself is <see cref="ToolCatalogueTests"/>' matrix and the
/// queries' own rules are <see cref="ProfileQuerySurfaceTests"/>'; what is proven here is only that
/// the composition drops neither half and adds nothing. That the SDK registers what this value
/// says is proven over the wire, in the protocol project, because that is the only place it can be.
/// </summary>
public sealed class ToolSurfaceTests
{
    private static readonly JiraCapabilities _licensed =
        new("8.20.7", "Server", SoftwareLicensed: true, DateTimeOffset.UtcNow);

    public static IEnumerable<TheoryDataRow<string[], JiraCapabilities?>> Standings() =>
    [
        new([], null),
        new(["issues:write"], _licensed),
        new([.. Enum.GetValues<Grant>().Select(GrantSet.Name)], _licensed),
    ];

    [Theory]
    [MemberData(nameof(Standings))]
    public void The_surface_is_the_gated_catalogue_plus_the_profiles_queries(
        string[] allowed,
        JiraCapabilities? capabilities)
    {
        var grants = GrantSet.Parse(allowed);
        var profile = ProfileWith(capabilities, Query("sprint_bugs"), Query("blocked"));

        ToolSurface.For(grants, profile).Names.ShouldBe(
            [.. GatedCatalogue(grants, capabilities), "jira_q_sprint_bugs", "jira_q_blocked"],
            ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(Standings))]
    public void A_profile_declaring_no_queries_leaves_the_surface_the_gated_catalogue(
        string[] allowed,
        JiraCapabilities? capabilities)
    {
        var grants = GrantSet.Parse(allowed);

        ToolSurface.For(grants, ProfileWith(capabilities)).Names
            .ShouldBe(GatedCatalogue(grants, capabilities), ignoreOrder: true);
    }

    [Fact]
    public void Each_row_says_which_kind_of_tool_it_is()
    {
        var surface = ToolSurface.For(GrantSet.Parse([]), ProfileWith(_licensed, Query("blocked")));

        surface.Rows.OfType<ToolSurfaceRow.QueryRow>().ShouldHaveSingleItem()
            .Name.ShouldBe("jira_q_blocked");

        surface.Rows.OfType<ToolSurfaceRow.BuiltInRow>().Select(row => row.ToolType)
            .ShouldBe(ToolCatalogue.ToolsToRegister(GrantSet.Parse([]), _licensed), ignoreOrder: true);
    }

    private static IEnumerable<string> GatedCatalogue(
        GrantSet grants,
        JiraCapabilities? capabilities) =>
        ToolCatalogue.ToolsToRegister(grants, capabilities).Select(ToolCatalogue.NameOf);

    private static ProfileQuery Query(string name) =>
        new(name, $"labels = {name}", $"The {name} query.");

    private static Profile ProfileWith(JiraCapabilities? capabilities, params ProfileQuery[] queries) =>
        new()
        {
            BaseUrl = new Uri("https://jira.example.com", UriKind.Absolute),
            Queries = queries,
            Capabilities = capabilities,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
}
