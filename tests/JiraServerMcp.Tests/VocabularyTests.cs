using JiraServerMcp.Tools;

namespace JiraServerMcp.Tests;

/// <summary>
/// The matching rule two tools share, proven at the module's own signature. Under ADR-0008 clause
/// 3 this is where branch-heavy logic lifted out of a tool is proven; what an agent observes when
/// a word names none or two is proven at the protocol seam, and those suites are untouched.
/// </summary>
public class VocabularyTests
{
    private sealed record Row(params string[] Words);

    private static Vocabulary.Resolved<Row> Resolve(string term, params Row[] rows) =>
        Vocabulary.Resolve(rows, row => row.Words, term);

    [Fact]
    public void Casing_and_surrounding_space_are_forgiven()
    {
        var row = new Row("Start Progress");

        Resolve("  start progress ", row).ShouldBe(new Vocabulary.Matched<Row>(row, 0));
    }

    [Fact]
    public void A_word_Jira_did_not_publish_is_skipped_rather_than_matched()
    {
        var gap = new Row("", "blocks");

        // The blank word is passed over, so the row matches at index 1 rather than at the gap.
        Resolve("blocks", gap).ShouldBe(new Vocabulary.Matched<Row>(gap, 1));
        Resolve("   ", gap).ShouldBeOfType<Vocabulary.Unmatched<Row>>();
    }

    [Fact]
    public void A_row_publishing_one_word_from_both_ends_matches_once_on_the_first()
    {
        var relates = new Row("relates to", "relates to");

        Resolve("relates to", relates).ShouldBe(new Vocabulary.Matched<Row>(relates, 0));
    }

    [Fact]
    public void The_index_says_which_of_a_rows_words_matched()
    {
        var blocks = new Row("blocks", "is blocked by");

        Resolve("is blocked by", blocks).ShouldBe(new Vocabulary.Matched<Row>(blocks, 1));
    }

    [Fact]
    public void One_word_published_by_two_rows_is_ambiguous_and_carries_both_in_publish_order()
    {
        var first = new Row("Done");
        var second = new Row("done");

        Resolve("Done", first, second)
            .ShouldBeOfType<Vocabulary.Ambiguous<Row>>()
            .Rows.ShouldBe([first, second]);
    }

    [Fact]
    public void A_term_matching_nothing_is_unmatched()
    {
        Resolve("Ship it", new Row("Done"), new Row("Start Progress"))
            .ShouldBeOfType<Vocabulary.Unmatched<Row>>();
    }

    [Fact]
    public void An_empty_list_of_rows_is_unmatched()
    {
        Resolve("Done").ShouldBeOfType<Vocabulary.Unmatched<Row>>();
    }
}
