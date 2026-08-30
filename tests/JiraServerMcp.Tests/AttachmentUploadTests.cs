using JiraServerMcp.Tools;

namespace JiraServerMcp.Tests;

/// <summary>
/// The six rules an upload is held to, proven at the helper's own signature (ADR-0008 clause 3).
/// What an agent observes when one of them refuses is proven once each at the protocol seam; the
/// exhaustive pass is here, where it costs nothing to run.
/// </summary>
public class AttachmentUploadTests
{
    private const string Key = "PROJ-42";

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void A_file_with_no_name_is_refused(string fileName) =>
        Refuse(fileName).ShouldNotBeNull().ShouldContain("needs a file name");

    [Theory]
    [InlineData("notes\n.md")]
    [InlineData("notes\r.md")]
    [InlineData("notes\0.md")]
    [InlineData("notes\u0001.md")]
    public void A_control_character_is_refused_because_it_would_split_the_header(string fileName) =>
        // The file name is written into a multipart Content-Disposition header. A line break there
        // is header injection, not untidiness.
        Refuse(fileName).ShouldNotBeNull().ShouldContain("control character");

    [Theory]
    [InlineData("logs/server.log")]
    [InlineData("/etc/passwd")]
    [InlineData("..\\..\\secrets.txt")]
    public void A_separator_is_refused_because_a_name_is_not_a_path(string fileName) =>
        Refuse(fileName).ShouldNotBeNull().ShouldContain("'/' or '\\'");

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void A_name_that_is_only_dots_is_refused(string fileName) =>
        Refuse(fileName).ShouldNotBeNull().ShouldContain("names a directory");

    [Fact]
    public void A_name_longer_than_jiras_own_column_is_refused_and_told_its_length()
    {
        Refuse(new string('a', 255)).ShouldBeNull();

        var refusal = Refuse(new string('a', 256)).ShouldNotBeNull();

        refusal.ShouldContain("255");
        refusal.ShouldContain("256");
    }

    [Theory]
    [InlineData("release notes.md")]
    [InlineData("rapport-été.txt")]
    [InlineData("報告.log")]
    [InlineData(".gitignore")]
    [InlineData("a.b.c")]
    public void Anything_a_person_would_reasonably_type_is_a_name(string fileName) =>
        // It is a label on the issue rather than a path, and nothing here ever resolves one.
        Refuse(fileName).ShouldBeNull();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void An_empty_file_is_refused_as_an_empty_comment_is(string content) =>
        Refuse(content: content).ShouldNotBeNull().ShouldContain("empty file");

    [Fact]
    public void Content_carrying_a_NUL_is_refused_because_the_read_side_calls_that_binary() =>
        // Allowing it would let this tool create files jira_get_attachment refuses to inline.
        Refuse(content: "first\0second").ShouldNotBeNull().ShouldContain("NUL");

    [Theory]
    [InlineData("a log\nwith lines\n")]
    [InlineData("a log\r\nwith Windows lines\r\n")]
    [InlineData("columns\tseparated\tby\ttabs")]
    public void Tabs_and_line_endings_pass_because_a_log_has_them(string content) =>
        Refuse(content: content).ShouldBeNull();

    [Fact]
    public void Content_past_the_cap_is_refused_and_told_both_numbers()
    {
        Refuse(content: new string('x', AttachmentUpload.LongestContent)).ShouldBeNull();

        var refusal = Refuse(content: new string('x', AttachmentUpload.LongestContent + 1))
            .ShouldNotBeNull();

        refusal.ShouldContain(AttachmentUpload.LongestContent.ToString());
        refusal.ShouldContain((AttachmentUpload.LongestContent + 1).ToString());
    }

    [Fact]
    public void The_cap_the_tool_describes_is_the_cap_the_helper_enforces() =>
        // The description is an attribute argument and cannot interpolate the constant, so this is
        // what stops the sentence outliving the number beside it.
        AttachmentUpload.ContentLimit.ShouldContain(AttachmentUpload.LongestContent.ToString());

    [Fact]
    public void A_refusal_names_the_issue_it_did_not_write_to() =>
        Refuse(content: "").ShouldNotBeNull().ShouldContain(Key);

    [Fact]
    public void A_refused_file_name_is_reported_before_the_content_is_looked_at() =>
        // The name reaches a header; the content reaches a body. The header is checked first.
        Refuse("bad/name", content: "").ShouldNotBeNull().ShouldContain("'/' or '\\'");

    private static string? Refuse(string fileName = "notes.md", string content = "a line") =>
        AttachmentUpload.Refuse(Key, fileName, content);
}
