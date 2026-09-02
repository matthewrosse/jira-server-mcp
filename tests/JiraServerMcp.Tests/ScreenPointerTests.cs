using JiraServerMcp.Jira.Models;
using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;

namespace JiraServerMcp.Tests;

/// <summary>
/// The rows a screen carries for fields the write tools beside it cannot touch. Under ADR-0008
/// clause 3 this is pure rendering, proven at the module rather than at the seam: what an agent
/// observes of a whole screen is pinned at the protocol seam, and the pointer is a property of the
/// row.
/// </summary>
public class ScreenPointerTests
{
    [Fact]
    public void A_field_jira_will_only_add_to_names_the_tool_that_adds_to_it()
    {
        // jira_update_issue refuses issuelinks in its own add map, so a screen that stopped at
        // "add only" would be telling an agent to call the tool that will not do it.
        Rendered("issuelinks", "Linked Issues", ["add"])
            .ShouldContain("issuelinks (Linked Issues) — array; add only — links are made with "
                           + "jira_link_issues");
    }

    [Fact]
    public void A_field_no_write_touches_names_the_tool_that_does()
    {
        Rendered("attachment", "Attachment", [])
            .ShouldContain("attachment (Attachment) — array; not writable — files are attached "
                           + "with jira_add_attachment");

        Rendered("comment", "Comment", ["add", "edit", "remove"])
            .ShouldContain("comment (Comment) — array; add/edit/remove only — comments are added "
                           + "with jira_add_comment");
    }

    [Fact]
    public void The_field_nothing_here_serves_is_left_saying_only_that_it_is_not_writable()
    {
        var text = Rendered("issuetype", "Issue Type", []);

        // Nothing in this server makes an issue's type writable, so a pointer would name a tool
        // that does not exist. "Not writable" is the whole truth here.
        text.ShouldContain("issuetype (Issue Type) — array; not writable" + Environment.NewLine);
    }

    [Fact]
    public void An_ordinary_field_carries_no_pointer_and_no_operations_line()
    {
        Rendered("summary", "Summary", ["set"])
            .ShouldContain("summary (Summary) — array" + Environment.NewLine);
    }

    [Fact]
    public void The_create_screen_carries_the_same_pointers_because_the_section_is_one_module()
    {
        // Pinned rather than left to follow: the two screens share ScreenFields.Section, so a
        // change made for the edit screen lands on the create screen whether or not it was meant
        // to, and the create screen is where an agent reads before its first write.
        CreateFields.Render(
                new JiraCreateFields(
                    "PROJ",
                    "Bug",
                    [new JiraScreenField("attachment", "Attachment", "array", false, [], [])]),
                FieldAliases.None).Text
            .ShouldContain("not writable — files are attached with jira_add_attachment");
    }

    private static string Rendered(string id, string name, IReadOnlyList<string> operations) =>
        EditFields.Render(
            new JiraEditFields(
                "PROJ-42",
                [new JiraScreenField(id, name, "array", Required: false, [], operations)]),
            FieldAliases.None).Text;
}
