using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Rendering;
using JiraServerMcp.Tools;

namespace JiraServerMcp.Tests;

/// <summary>
/// The table that says everything one expansion is called. These are the guards that make an
/// eighth expansion impossible to half-add: a row that names no mechanism reaches Jira asking for
/// nothing, and a name the tool description never mentions is invisible to the agent that would
/// ask for it — neither of which fails a rendering test, because both answer "there are none of
/// those" rather than erroring. The last two hold the rendering to the same standard: a row that
/// names every mechanism correctly and renders no section answers "there are none of those" too.
/// </summary>
public class ExpansionTableTests
{
    [Fact]
    public void Every_expansion_has_exactly_one_row()
    {
        foreach (var expansion in Enum.GetValues<Expansion>())
        {
            Expansions.Table.Count(row => row.Id == expansion).ShouldBe(1,
                $"{expansion} needs exactly one row in Expansions.Table.");
        }

        Expansions.Table.Count.ShouldBe(Enum.GetValues<Expansion>().Length);
    }

    /// <summary>
    /// A row with no mechanism is an expansion an agent may ask for that asks Jira for nothing.
    /// <c>Field</c> and <c>Expand</c> are the two ways of asking the same GET and no row needs
    /// both; <c>SeparateRequest</c> is a second call, so links — which are a field on the issue
    /// and, for the links out of Jira, a call of their own — carries it alongside a field.
    /// </summary>
    [Fact]
    public void Every_row_names_a_mechanism_and_never_asks_one_get_two_ways()
    {
        foreach (var row in Expansions.Table)
        {
            var named = new[] { row.Field is not null, row.Expand is not null, row.SeparateRequest };

            named.ShouldContain(true,
                $"{row.Name} names no mechanism, so asking for it would return nothing.");

            (row.Field is not null && row.Expand is not null).ShouldBeFalse(
                $"{row.Name} asks the same request both ways.");
        }
    }

    [Fact]
    public void The_collection_fields_are_exactly_the_rows_that_name_a_field()
    {
        Expansions.CollectionFields.ShouldBe(
            Expansions.Table.Select(row => row.Field).OfType<string>(),
            ignoreOrder: true);
    }

    [Fact]
    public void The_names_an_agent_is_offered_are_the_tables_own()
    {
        Expansions.Names.ShouldBe(string.Join(", ", Expansions.Table.Select(row => row.Name)));
    }

    /// <summary>
    /// Read off the attribute rather than matched against the source, so the description an agent
    /// is actually served is the thing under test. Attachments shipped without this and were
    /// invisible for it.
    /// </summary>
    [Fact]
    public void The_include_parameter_offers_every_expansion_by_name()
    {
        var include = typeof(GetIssuesTool)
            .GetMethod(nameof(GetIssuesTool.GetIssuesAsync), BindingFlags.Public | BindingFlags.Instance)
            .ShouldNotBeNull()
            .GetParameters()
            .Single(parameter => parameter.Name == "include");

        var description = include.GetCustomAttribute<DescriptionAttribute>().ShouldNotBeNull();

        foreach (var row in Expansions.Table)
        {
            description.Description.ShouldContain(row.Name);
        }
    }

    /// <summary>
    /// Every section prints its heading even when Jira answered with nothing, so an issue with no
    /// content at all is enough: what is asserted is that asking added something, not what.
    /// Asserting the section's own text is <c>IssueRenderingTests</c>' job.
    /// </summary>
    [Fact]
    public void Every_expansion_renders_a_section_when_it_is_asked_for()
    {
        foreach (var expansion in Enum.GetValues<Expansion>())
        {
            IssueDetail.Render(Empty, [expansion]).ShouldNotBe(IssueDetail.Render(Empty, []),
                $"{expansion} renders no section, so asking for it reads as there being none.");
        }
    }

    /// <summary>
    /// The mirror: an arm that renders whether or not it was asked for. Asserted as the whole
    /// body rather than expansion by expansion, because the body of an issue with no fields and
    /// nothing asked for is the key and nothing else — which covers an eighth expansion the day
    /// it is added without naming any of the seven.
    /// </summary>
    [Fact]
    public void No_expansion_renders_a_section_when_it_was_not_asked_for()
    {
        IssueDetail.Render(Empty, []).ShouldBe("PROJ-12");
    }

    /// <summary>
    /// An issue Jira answered with nothing on. <c>remoteLinks</c> is empty rather than null:
    /// null is the read having been refused (ADR-0007), which renders its own sentence, and a
    /// guard that passed through that branch would keep passing if the ordinary one broke.
    /// </summary>
    private static JiraIssueDetail Empty =>
        new("PROJ-12", new Dictionary<string, JsonElement>(), [], null, null, [], [], null, [], []);
}
