using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;

namespace JiraServerMcp.Tests;

/// <summary>
/// Alias resolution, which is a table lookup and nothing more. Pure logic, so it is proven here
/// (ADR-0008, clause 3); what the tools and the CLI make of it is proven at their own seams.
/// </summary>
public class FieldAliasTests
{
    [Fact]
    public void An_alias_resolves_to_the_field_it_stands_for()
    {
        Aliases().Resolve("story_points").ShouldBe("customfield_10010");
    }

    [Fact]
    public void An_identifier_resolves_to_itself_so_either_name_may_be_written()
    {
        // An alias is an additional name, never a rename: the identifier keeps working.
        Aliases().Resolve("customfield_10010").ShouldBe("customfield_10010");
    }

    [Fact]
    public void A_name_this_profile_does_not_know_is_passed_through_rather_than_refused()
    {
        // The field catalogue lives in Jira, not here. An unfamiliar name is far more likely to be
        // a real identifier than a mistake, and Jira is the one that can tell.
        Aliases().Resolve("summary").ShouldBe("summary");
        Aliases().Resolve("customfield_99999").ShouldBe("customfield_99999");
    }

    [Fact]
    public void Case_and_surrounding_space_do_not_make_an_alias_a_different_alias()
    {
        Aliases().Resolve("  Story_Points  ").ShouldBe("customfield_10010");
    }

    [Fact]
    public void A_read_labels_an_aliased_field_with_both_names()
    {
        Aliases().Label("customfield_10010").ShouldBe("story_points (customfield_10010)");
    }

    [Fact]
    public void A_field_with_no_alias_is_labelled_as_jira_names_it()
    {
        Aliases().Label("summary").ShouldBe("summary");
    }

    [Theory]
    [InlineData("customfield_10010")]
    [InlineData("CUSTOMFIELD_10010")]
    public void A_name_spelled_like_a_field_identifier_cannot_be_declared_as_an_alias(string alias)
    {
        // The collision case: an alias spelled like an identifier would leave "customfield_10010"
        // meaning two things, and an operator who wants a readable name has no reason to pick it.
        FieldAliases.IsDeclarable(alias).ShouldBeFalse();
    }

    [Theory]
    [InlineData("story_points")]
    [InlineData("customfield")]
    [InlineData("custom_field_10010")]
    public void A_readable_name_can_be_declared(string alias)
    {
        FieldAliases.IsDeclarable(alias).ShouldBeTrue();
    }

    [Fact]
    public void A_blank_name_cannot_be_declared()
    {
        FieldAliases.IsDeclarable("   ").ShouldBeFalse();
    }

    [Fact]
    public void A_profile_that_declares_none_resolves_and_labels_everything_as_jira_names_it()
    {
        FieldAliases.None.Resolve("customfield_10010").ShouldBe("customfield_10010");
        FieldAliases.None.Label("customfield_10010").ShouldBe("customfield_10010");
        FieldAliases.None.Any.ShouldBeFalse();
    }

    [Fact]
    public void The_field_projection_widens_by_alias_as_readily_as_by_identifier()
    {
        var widened = FieldProjection.Widen(["story_points"], Aliases());

        widened.ShouldContain("customfield_10010");
        widened.ShouldNotContain("story_points");

        // Widening adds; a caller reaching for one custom field does not lose the status.
        widened.ShouldContain("status");
    }

    [Fact]
    public void Two_aliases_for_one_field_both_resolve_and_the_label_picks_one()
    {
        var aliases = FieldAliases.For(new Dictionary<string, string>
        {
            ["points"] = "customfield_10010",
            ["story_points"] = "customfield_10010",
        });

        aliases.Resolve("points").ShouldBe("customfield_10010");
        aliases.Resolve("story_points").ShouldBe("customfield_10010");
        aliases.Label("customfield_10010").ShouldContain("customfield_10010");
    }

    private static FieldAliases Aliases() =>
        FieldAliases.For(new Dictionary<string, string>
        {
            ["story_points"] = "customfield_10010",
            ["severity"] = "customfield_10021",
        });
}
