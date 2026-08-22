using System.ComponentModel;
using System.Reflection;
using JiraServerMcp.Rendering;
using JiraServerMcp.Tools;

namespace JiraServerMcp.Tests;

/// <summary>
/// The table that says everything one expansion is called. These are the guards that make a
/// seventh expansion impossible to half-add: a row that names no mechanism reaches Jira asking for
/// nothing, and a name the tool description never mentions is invisible to the agent that would
/// ask for it — neither of which fails a rendering test, because both answer "there are none of
/// those" rather than erroring.
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
}
