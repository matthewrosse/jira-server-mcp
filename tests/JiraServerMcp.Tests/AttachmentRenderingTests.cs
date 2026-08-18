using System.Text;
using System.Text.Json;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Rendering;

namespace JiraServerMcp.Tests;

/// <summary>
/// One window of an attachment as an agent receives it: the framing around text that anyone with
/// a Jira account could have uploaded, the resume position that makes a large file readable at
/// all, and a binary described rather than inlined.
/// </summary>
public class AttachmentRenderingTests
{
    [Fact]
    public void A_small_text_file_is_delimited_and_carries_no_resume_position()
    {
        var rendered = AttachmentContent.Render(File(size: 14), "id,name\n1,Ada\n"u8, offset: 0);

        rendered.Text.ShouldContain("bytes 0-13 of 14");
        rendered.Text.ShouldContain("the rest of the file");
        rendered.Text.ShouldContain("1,Ada");

        // The whole of an attachment is untrusted content, with no case analysis: a file is the
        // least trustworthy text on a ticket.
        rendered.Text.ShouldContain(UntrustedContent.Preamble);

        Structure(rendered).ShouldBe(
            """
            {"outcome":"ok","attachmentId":"10100","fileName":"notes.csv","mediaType":"text/csv","size":14,"binary":false,"offset":0,"bytes":14,"bytesRemaining":0}
            """);
    }

    [Fact]
    public void A_file_larger_than_the_window_says_where_to_resume()
    {
        var window = Encoding.UTF8.GetBytes(new string('a', ResponseBudget.AttachmentWindow));

        var rendered = AttachmentContent.Render(
            File(size: ResponseBudget.AttachmentWindow * 3),
            window,
            offset: 0);

        var attachment = Deserialize(rendered);

        attachment.NextOffset.ShouldBe(ResponseBudget.AttachmentWindow);
        attachment.BytesRemaining.ShouldBe(ResponseBudget.AttachmentWindow * 2);
        rendered.Text.ShouldContain($"offset: {ResponseBudget.AttachmentWindow}");
    }

    [Fact]
    public void A_second_window_continues_where_the_first_stopped_and_ends_the_file()
    {
        var first = "0123456789"u8.ToArray();
        var second = "abcde"u8.ToArray();
        var attachment = File(size: first.Length + second.Length);

        var resumeAt = Deserialize(AttachmentContent.Render(attachment, first, offset: 0))
            .NextOffset.ShouldNotBeNull();

        var rendered = AttachmentContent.Render(attachment, second, resumeAt);
        var window = Deserialize(rendered);

        window.Offset.ShouldBe(10);
        window.Bytes.ShouldBe(5);
        window.NextOffset.ShouldBeNull();
        window.BytesRemaining.ShouldBe(0);
        rendered.Text.ShouldContain("bytes 10-14 of 15");
    }

    [Fact]
    public void A_windows_half_character_is_left_for_the_next_window_to_carry()
    {
        var whole = Encoding.UTF8.GetBytes("Rêverie");
        var attachment = File(size: whole.Length);

        // A window cut one byte into ê: what is rendered stops at the character before it, and
        // the resume position is that boundary rather than the window's end.
        var rendered = AttachmentContent.Render(attachment, whole.AsSpan(0, 2), offset: 0);

        Deserialize(rendered).NextOffset.ShouldBe(1);
        rendered.Text.ShouldContain("bytes 0-0 of 8");
    }

    [Fact]
    public void A_file_that_stops_being_text_partway_through_is_never_inlined()
    {
        // The window is text for its first pages and then is not. Deciding from a sample of the
        // front would inline the NUL, and everything after it, as text.
        var file = new byte[10_000];

        Array.Fill(file, (byte)'a');
        file[5_000] = 0x00;

        var rendered = AttachmentContent.Render(File(size: 10_000), file, offset: 0);
        var window = Deserialize(rendered);

        window.Binary.ShouldBe(true);
        window.Bytes.ShouldBe(0);
        rendered.Text.ShouldNotContain("aaaa");
        rendered.Text.ShouldNotContain("\uFFFD");
    }

    [Fact]
    public void A_window_that_stops_being_text_is_described_as_the_window_it_is()
    {
        var file = new byte[100];

        Array.Fill(file, (byte)'a');
        file[50] = 0x00;

        // The caller already holds readable text from earlier in this file, so telling it the file
        // is not text would contradict what it is holding.
        var rendered = AttachmentContent.Render(File(size: 20_000), file, offset: 16_000);

        rendered.Text.ShouldContain("stops being text at byte 16000");
        rendered.Text.ShouldNotContain("notes.csv is not text");
        Deserialize(rendered).Offset.ShouldBe(16_000);
    }

    [Fact]
    public void A_file_jira_gave_no_size_for_says_there_is_probably_more_rather_than_claiming_the_end()
    {
        // Jira's size is missing or unreadable, which reaches the renderer as zero. Reading that
        // as "an empty file" would tell a caller holding the first window that it holds the whole
        // file.
        var window = Encoding.UTF8.GetBytes(new string('a', ResponseBudget.AttachmentWindow));

        var rendered = AttachmentContent.Render(
            new JiraAttachment("10100", "notes.csv", 0, "text/csv", Content),
            window,
            offset: 0);

        var page = Deserialize(rendered);

        page.NextOffset.ShouldBe(ResponseBudget.AttachmentWindow);
        rendered.Text.ShouldContain("did not say how large this file is");

        // A paging field only where Jira gave the number (ADR-0009, as amended): absence is
        // unknown, where a zero would say the file is empty.
        page.Size.ShouldBeNull();
        page.BytesRemaining.ShouldBeNull();
    }

    [Fact]
    public void A_short_window_from_a_file_of_unknown_size_is_the_end_of_it()
    {
        var rendered = AttachmentContent.Render(
            new JiraAttachment("10100", "notes.csv", 0, "text/csv", Content),
            "id,name\n"u8,
            offset: 0);

        Deserialize(rendered).NextOffset.ShouldBeNull();
        rendered.Text.ShouldContain("the rest of the file");
    }

    [Fact]
    public void A_binary_is_described_rather_than_read_whatever_jira_claims_it_is()
    {
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0, 0, 0, 0 };

        // Jira claims text/plain, which is exactly the mislabelling the byte check exists for.
        var rendered = AttachmentContent.Render(
            new JiraAttachment("10101", "screenshot.png", 90_000, "text/plain", Content),
            png,
            offset: 0);

        rendered.Text.ShouldContain("is not text");
        rendered.Text.ShouldContain("90000 bytes");
        rendered.Text.ShouldContain("Jira claims it is text/plain");

        // Nothing decoded means nothing to delimit, and no resume position to offer.
        Structure(rendered).ShouldBe(
            """
            {"outcome":"ok","attachmentId":"10101","fileName":"screenshot.png","mediaType":"text/plain","size":90000,"binary":true,"offset":0,"bytes":0,"bytesRemaining":89992}
            """);
    }

    [Fact]
    public void An_offset_past_the_end_of_the_file_says_so_rather_than_looking_like_an_empty_file()
    {
        var rendered = AttachmentContent.Render(File(size: 14), [], offset: 14);

        rendered.Text.ShouldContain("nothing to read at byte 14");
        Deserialize(rendered).NextOffset.ShouldBeNull();
    }

    private const string Content = "https://jira.example.invalid/secure/attachment/10100/notes.csv";

    private static JiraAttachment File(long size) =>
        new("10100", "notes.csv", size, "text/csv", Content);

    private static string Structure(Rendered rendered) =>
        rendered.Structure.ShouldNotBeNull().GetRawText();

    private static AttachmentOutput Deserialize(Rendered rendered) =>
        rendered.Structure.ShouldNotBeNull().Deserialize<AttachmentOutput>()
        ?? throw new InvalidOperationException("The structured half deserialized to nothing.");
}
