using JiraServerMcp.Jira.Capabilities;
using JiraServerMcp.Profiles;
using JiraServerMcp.Tools;

namespace JiraServerMcp.Tests;

/// <summary>
/// Which tools a profile's own queries become. Pure logic over a profile value (ADR-0008,
/// clause 3); that the SDK registers them and an agent can call them is proven at the protocol
/// seam, because that is the only place it can be.
/// </summary>
public class ProfileQuerySurfaceTests
{
    [Fact]
    public void A_profile_that_declares_none_contributes_none()
    {
        ProfileQuerySurface.ToolsToRegister(ProfileWith(), Services()).ShouldBeEmpty();
    }

    [Fact]
    public void Each_query_becomes_one_tool_named_under_the_fixed_prefix()
    {
        var tools = ProfileQuerySurface.ToolsToRegister(
            ProfileWith(Query("sprint_bugs"), Query("blocked")),
            Services());

        tools.Select(tool => tool.ProtocolTool.Name)
            .ShouldBe(["jira_q_sprint_bugs", "jira_q_blocked"]);
    }

    [Fact]
    public void The_prefix_is_what_keeps_an_operators_name_from_shadowing_a_built_in_tool()
    {
        // An operator naming a query "search" gets jira_q_search, not jira_search. With
        // operator-supplied names, collision protection is not optional.
        var tools = ProfileQuerySurface.ToolsToRegister(ProfileWith(Query("search")), Services());

        tools.ShouldHaveSingleItem().ProtocolTool.Name.ShouldBe("jira_q_search");
    }

    [Fact]
    public void The_operators_description_is_what_an_agent_reads()
    {
        var tools = ProfileQuerySurface.ToolsToRegister(
            ProfileWith(new ProfileQuery("blocked", "labels = blocked", "What is stuck.")),
            Services());

        var description = tools.ShouldHaveSingleItem().ProtocolTool.Description.ShouldNotBeNull();

        description.ShouldStartWith("What is stuck.");

        // And it says whose tool this is: an agent choosing between tools should know which belong
        // to this deployment rather than to this server.
        description.ShouldContain("defined on this deployment's profile");
    }

    [Fact]
    public void A_profile_carrying_more_than_the_cap_registers_the_cap_and_no_more()
    {
        // The CLI refuses an eleventh; a file edited by hand can still hold one, and the surface
        // is the last place that can hold the line.
        var queries = Enumerable.Range(1, 25).Select(number => Query($"q{number}")).ToArray();

        var tools = ProfileQuerySurface.ToolsToRegister(ProfileWith(queries), Services());

        tools.Count.ShouldBe(ProfileQuerySurface.Cap);
        tools.Count.ShouldBe(10);
    }

    [Fact]
    public void A_query_tool_is_read_only_because_a_canned_query_is_a_search()
    {
        var tools = ProfileQuerySurface.ToolsToRegister(ProfileWith(Query("blocked")), Services());

        var annotations = tools.ShouldHaveSingleItem().ProtocolTool.Annotations.ShouldNotBeNull();

        annotations.ReadOnlyHint.ShouldBe(true);
        annotations.DestructiveHint.ShouldBe(false);
    }

    [Fact]
    public void A_query_tool_takes_paging_and_nothing_that_changes_what_it_means()
    {
        // A query whose meaning changes with an argument is jira_search's job, which is the line
        // CONTEXT.md already draws for a canned query.
        var schema = ProfileQuerySurface.ToolsToRegister(ProfileWith(Query("blocked")), Services())
            .ShouldHaveSingleItem()
            .ProtocolTool.InputSchema;

        var properties = schema.GetProperty("properties");

        properties.TryGetProperty("startAt", out _).ShouldBeTrue();
        properties.TryGetProperty("maxResults", out _).ShouldBeTrue();
        properties.TryGetProperty("fields", out _).ShouldBeTrue();
        properties.TryGetProperty("jql", out _).ShouldBeFalse();

        // Every one of them optional: an agent calling the tool by name alone must reach Jira.
        if (schema.TryGetProperty("required", out var required))
        {
            required.EnumerateArray().ShouldBeEmpty();
        }
    }

    [Theory]
    [InlineData("sprint_bugs", true)]
    [InlineData("q1", true)]
    [InlineData("Sprint_Bugs", false)]
    [InlineData("sprint-bugs", false)]
    [InlineData("1sprint", false)]
    [InlineData("", false)]
    public void A_name_becomes_part_of_a_tool_name_so_the_grammar_is_narrow(string name, bool valid)
    {
        ProfileQueryName.IsValid(name).ShouldBe(valid);
    }

    private static ProfileQuery Query(string name) =>
        new(name, $"labels = {name}", $"The {name} query.");

    private static Profile ProfileWith(params ProfileQuery[] queries) =>
        new()
        {
            BaseUrl = new Uri("https://jira.example.com", UriKind.Absolute),
            Queries = queries,
            Capabilities = new JiraCapabilities("8.20.7", "Server", true, DateTimeOffset.UtcNow),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>
    /// A container nothing here resolves from: what is under test is which tools are built, and a
    /// tool only reaches its services when it is called.
    /// </summary>
    private static IServiceProvider Services() => new EmptyServices();

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
