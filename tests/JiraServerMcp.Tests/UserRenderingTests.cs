using JiraServerMcp.Jira.Models;
using JiraServerMcp.Rendering;

namespace JiraServerMcp.Tests;

/// <summary>
/// The header a user search puts above its rows. Every branch here is a sentence an agent acts on:
/// whether the count is of the directory or of who may be assigned somewhere, whether an empty
/// answer means "nobody" or "you searched by something this endpoint does not match", and what
/// became of the inactive accounts.
/// </summary>
public class UserRenderingTests
{
    [Fact]
    public void An_unanchored_count_is_a_count_of_the_directory()
    {
        var header = Header(Render([Ada], assignableTo: null));

        header.ShouldContain("users: 1, usernames first");
        header.ShouldContain("Inactive users were excluded");
        header.ShouldNotContain("assignable");
    }

    [Fact]
    public void An_anchored_count_says_what_it_is_a_count_of()
    {
        // A count narrowed by an assignment permission that does not say so reads as a claim about
        // the directory, and the rows carry nothing that says which of the two it is.
        Header(Render([Ada], assignableTo: "PROJ-42"))
            .ShouldContain("users assignable on PROJ-42: 1, usernames first");
    }

    [Fact]
    public void An_anchored_answer_says_inactive_users_could_not_have_been_included()
    {
        var header = Header(Render([Ada], assignableTo: "PROJ", includeInactive: true));

        header.ShouldContain("Inactive users cannot be included when assignableTo is set");
        header.ShouldNotContain("Inactive users were included.");
    }

    [Fact]
    public void An_empty_anchored_answer_carries_what_the_search_matches_on()
    {
        // The moment an agent is about to conclude that a person cannot be assigned, when what
        // happened is that it searched by an address this endpoint never matches.
        var header = Header(Render([], assignableTo: "PROJ"));

        header.ShouldContain("users assignable on PROJ: none matched");
        header.ShouldContain("not email addresses");
        header.ShouldContain("from the start of a name");
    }

    [Fact]
    public void An_empty_unanchored_answer_keeps_todays_sentence()
    {
        var header = Header(Render([], assignableTo: null));

        header.ShouldContain("users: none matched.");
        header.ShouldNotContain("email addresses");
    }

    [Fact]
    public void A_full_anchored_page_still_says_where_the_next_one_starts()
    {
        var header = Header(Render([Ada], assignableTo: "PROJ", maxResults: 1));

        header.ShouldContain("a full page");
        header.ShouldContain("startAt: 1");
    }

    private static JiraUser Ada => new("Ada Lovelace", "ada", "ada@example.invalid", true);

    /// <summary>The header is what sits above the delimited region, and only that.</summary>
    private static string Header(string rendered) =>
        rendered[..rendered.IndexOf(UntrustedContent.Preamble, StringComparison.Ordinal)];

    private static string Render(
        JiraUser[] users,
        string? assignableTo,
        bool includeInactive = false,
        int maxResults = 25) =>
        UserResults.Render(users, startAt: 0, maxResults, includeInactive, assignableTo).Text;
}
