using JiraServerMcp.Configuration;
using JiraServerMcp.Grants;

namespace JiraServerMcp.Tests;

/// <summary>
/// ADR-0005: grants arrive as launch arguments, so what one MCP client may do is visible in the
/// configuration file its operator already reads.
/// </summary>
public class GrantTests
{
    [Fact]
    public void No_allow_argument_grants_nothing()
    {
        var grants = GrantSet.Parse([]);

        grants.Allows(Grant.IssuesWrite).ShouldBeFalse();
        grants.Allows(Grant.CommentsWrite).ShouldBeFalse();
        grants.Allows(Grant.WorklogsWrite).ShouldBeFalse();
    }

    [Fact]
    public void One_name_grants_that_category_and_no_other()
    {
        var grants = GrantSet.Parse(["issues:write"]);

        grants.Allows(Grant.IssuesWrite).ShouldBeTrue();
        grants.Allows(Grant.CommentsWrite).ShouldBeFalse();
        grants.Allows(Grant.WorklogsWrite).ShouldBeFalse();
    }

    [Fact]
    public void Names_may_be_separated_by_commas_within_one_argument()
    {
        var grants = GrantSet.Parse(["issues:write,worklogs:write"]);

        grants.Allows(Grant.IssuesWrite).ShouldBeTrue();
        grants.Allows(Grant.WorklogsWrite).ShouldBeTrue();
        grants.Allows(Grant.CommentsWrite).ShouldBeFalse();
    }

    [Fact]
    public void The_argument_may_be_repeated_and_may_repeat_a_name()
    {
        var grants = GrantSet.Parse(["issues:write", "comments:write", "issues:write"]);

        grants.Allows(Grant.IssuesWrite).ShouldBeTrue();
        grants.Allows(Grant.CommentsWrite).ShouldBeTrue();
    }

    [Fact]
    public void Surrounding_space_and_capitals_are_forgiven()
    {
        var grants = GrantSet.Parse([" Issues:Write , comments:write "]);

        grants.Allows(Grant.IssuesWrite).ShouldBeTrue();
        grants.Allows(Grant.CommentsWrite).ShouldBeTrue();
    }

    [Fact]
    public void An_unknown_name_is_refused_and_the_valid_ones_are_listed()
    {
        var refusal = Should.Throw<ConfigurationException>(
            () => GrantSet.Parse(["issues:write", "issues:delete"]));

        refusal.Message.ShouldContain("issues:delete");
        refusal.Message.ShouldContain("issues:write");
        refusal.Message.ShouldContain("comments:write");
        refusal.Message.ShouldContain("worklogs:write");
    }

    [Fact]
    public void Every_grant_has_a_name_the_operator_can_write()
    {
        // The direct guard on the name table: a grant added to the enum without a row there fails
        // here, rather than in the help text an operator reads and no test checks.
        foreach (var grant in Enum.GetValues<Grant>())
        {
            var grants = GrantSet.Parse([GrantSet.Name(grant)]);

            foreach (var other in Enum.GetValues<Grant>())
            {
                grants.Allows(other).ShouldBe(other == grant);
            }
        }
    }

    [Fact]
    public void An_empty_name_is_refused_rather_than_ignored()
    {
        // "--allow issues:write," is a typo, and treating it as the grant the operator meant to
        // type would hide it.
        Should.Throw<ConfigurationException>(() => GrantSet.Parse(["issues:write,"]));
    }
}
