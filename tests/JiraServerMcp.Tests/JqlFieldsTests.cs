using JiraServerMcp.Jira.Models;
using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;

namespace JiraServerMcp.Tests;

/// <summary>
/// The query catalogue as an agent reads it: what one field's line says, what the cap does to a
/// large instance, and what the substring narrows. The join to a declared alias is here too — it
/// is arithmetic on a bracket form rather than a lookup, because no part of this payload carries
/// the identifier an alias resolves to.
/// </summary>
public class JqlFieldsTests
{
    private static readonly JiraJqlField _summary = new(
        Name: "summary",
        CustomFieldId: null,
        Types: ["java.lang.String"],
        Operators: ["~", "!~", "is", "is not"],
        Orderable: true,
        Searchable: true);

    private static readonly JiraJqlField _storyPoints = new(
        Name: "\"Story Points\"",
        CustomFieldId: "cf[10107]",
        Types: ["java.lang.Number"],
        Operators: ["=", "!=", "in", "not in"],
        Orderable: true,
        Searchable: true);

    [Fact]
    public void A_fields_line_names_it_as_a_clause_must_and_lists_what_it_takes()
    {
        var text = Catalogue([_summary]).Text;

        // summary takes ~ and not =, which is the sort of thing an agent otherwise learns from a
        // 400 — and the last dot-segment is the whole of what the type is worth in prose.
        text.ShouldContain("  summary  String  ~, !~, is, is not");
    }

    [Fact]
    public void A_custom_field_is_published_under_the_names_it_is_queryable_by()
    {
        var text = Catalogue([_storyPoints]).Text;

        text.ShouldContain("""  "Story Points"  cf[10107]  Number""");

        // The identifier every write tool hands out is not a JQL name, so no row publishes it as
        // one: an unaliased custom field carries only what a clause may use. The header says the
        // spelling out loud, which is why this looks at the rows rather than at the whole text.
        Rows(text).ShouldNotContain(row => row.Contains("customfield_"));
    }

    [Fact]
    public void A_declared_alias_is_joined_to_the_field_by_the_number_in_its_bracket_form()
    {
        var aliases = FieldAliases.For(new Dictionary<string, string>
        {
            ["story_points"] = "customfield_10107",
        });

        var text = Catalogue([_storyPoints], aliases: aliases).Text;

        // All three names on one line: the operator declared the alias, the alias resolves to the
        // identifier, and the identifier is queryable under neither of its own spellings.
        text.ShouldContain("""  "Story Points"  cf[10107]  story_points (customfield_10107)  Number""");
    }

    [Fact]
    public void An_alias_for_another_field_leaves_this_one_alone()
    {
        var aliases = FieldAliases.For(new Dictionary<string, string>
        {
            ["sprint_of_record"] = "customfield_10104",
        });

        Catalogue([_storyPoints], aliases: aliases).Text.ShouldNotContain("sprint_of_record");
    }

    [Fact]
    public void Only_a_departure_from_the_default_is_marked()
    {
        var attachments = _summary with
        {
            Name = "attachments",
            Orderable = false,
            Operators = ["is", "is not"],
        };

        var text = Catalogue([_summary, attachments]).Text;

        text.ShouldContain("  attachments  String  is, is not; not sortable");

        // Almost every field is sortable and searchable. Saying so on each line would spend the
        // cap on what the caller already assumes.
        text.ShouldNotContain("summary  String  ~, !~, is, is not; ");
    }

    [Fact]
    public void A_large_instance_is_cut_at_the_cap_and_told_to_narrow_rather_than_to_page()
    {
        var fields = Enumerable.Range(0, ResponseBudget.JqlFieldCap + 31)
            .Select(index => _summary with { Name = $"field{index}" })
            .ToArray();

        var rendered = Catalogue(fields);

        rendered.Text.ShouldContain($"fields (showing {ResponseBudget.JqlFieldCap} of {fields.Length}");

        // Jira's endpoint has no page of its own, so there is no position to resume from and the
        // sentence must not borrow a page's words about resuming.
        rendered.Text.ShouldNotContain("startAt");
        rendered.Text.ShouldNotContain("resume");

        var structure = rendered.Structure.ShouldNotBeNull();

        structure.GetProperty("totalFields").GetInt32().ShouldBe(fields.Length);
        structure.GetProperty("fieldsTruncated").GetBoolean().ShouldBeTrue();
        structure.GetProperty("fields").GetArrayLength().ShouldBe(ResponseBudget.JqlFieldCap);
    }

    [Fact]
    public void A_substring_narrows_by_the_jql_name_and_by_the_bracket_form_alike()
    {
        Catalogue([_summary, _storyPoints], startsWith: "story").Text
            .ShouldContain("fields (1 of 2 matching 'story')");

        Catalogue([_summary, _storyPoints], startsWith: "10107").Text
            .ShouldContain("""  "Story Points"  cf[10107]""");

        Catalogue([_summary, _storyPoints], startsWith: "10107").Text
            .ShouldNotContain("  summary  ");
    }

    [Fact]
    public void The_structured_half_carries_jiras_own_type_names_in_full()
    {
        var structure = Catalogue([_storyPoints]).Structure.ShouldNotBeNull();

        var field = structure.GetProperty("fields").EnumerateArray().ShouldHaveSingleItem();

        field.GetProperty("name").GetString().ShouldBe("\"Story Points\"");
        field.GetProperty("customFieldId").GetString().ShouldBe("cf[10107]");
        field.GetProperty("types").EnumerateArray().Single().GetString()
            .ShouldBe("java.lang.Number");
    }

    [Fact]
    public void A_fields_values_are_published_exactly_as_a_clause_writes_them()
    {
        var rendered = JqlFields.Values(
            new JiraJqlSuggestions("status", ["Open", "\"In Progress\""]));

        rendered.Text.ShouldContain("\"In Progress\"");

        var structure = rendered.Structure.ShouldNotBeNull();

        structure.GetProperty("field").GetString().ShouldBe("status");
        structure.GetProperty("outcome").GetString().ShouldBe("ok");
    }

    [Fact]
    public void A_field_with_nothing_behind_it_is_refused_with_both_readings_named()
    {
        var rendered = JqlFields.NoValues(new JiraJqlSuggestions("notafield", []));

        // Jira answers 200 either way, so the tool says what Jira will not.
        rendered.Text.ShouldContain("may not be queryable under that name");
        rendered.Text.ShouldContain("enumerate nothing");
        rendered.Text.ShouldContain("jira_get_jql_fields");

        rendered.Structure.ShouldNotBeNull().GetProperty("outcome").GetString()
            .ShouldBe("refused");
    }

    /// <summary>The field and function lines, which are the indented ones.</summary>
    private static string[] Rows(string text) =>
        [.. text.Split(Environment.NewLine).Where(line => line.StartsWith("  ", StringComparison.Ordinal))];

    private static Rendered Catalogue(
        IReadOnlyList<JiraJqlField> fields,
        string? startsWith = null,
        FieldAliases? aliases = null) =>
        JqlFields.Catalogue(
            new JiraJqlCatalogue(fields, [new JiraJqlFunction("currentUser()", ["java.lang.String"])]),
            startsWith,
            aliases ?? FieldAliases.None);
}
