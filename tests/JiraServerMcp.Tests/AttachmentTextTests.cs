using System.Text;
using JiraServerMcp.Rendering;

namespace JiraServerMcp.Tests;

/// <summary>
/// Whether an attachment reads as text, decided by its bytes. Pure logic, so it is proven here
/// (ADR-0008, clause 3) — and it is worth proving exhaustively, because the alternative rule this
/// replaces, believing Jira's media type, fails silently in both directions on the instances this
/// project targets.
/// </summary>
public class AttachmentTextTests
{
    [Fact]
    public void A_plain_ascii_file_is_text()
    {
        AttachmentText.IsText("id,name\n1,Ada\n"u8).ShouldBeTrue();
    }

    [Fact]
    public void A_file_with_a_nul_byte_is_binary_whatever_surrounds_it()
    {
        // A PNG opens with bytes that read as text and then carries NULs. Nearly every binary
        // format does something like this, and no UTF-8 text carries a NUL at all.
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 };

        AttachmentText.IsText(png).ShouldBeFalse();
    }

    [Fact]
    public void A_file_whose_bytes_are_not_valid_utf8_is_binary()
    {
        // 0xC3 opens a two-byte sequence and 0x28 cannot continue one.
        AttachmentText.IsText(new byte[] { 0xC3, 0x28, 0x41 }).ShouldBeFalse();
    }

    [Fact]
    public void A_utf8_file_whose_leading_window_is_pure_ascii_is_text()
    {
        var log = Encoding.UTF8.GetBytes(new string('a', 200) + " — a café's naïve façade");

        AttachmentText.IsText(log).ShouldBeTrue();
    }

    [Fact]
    public void An_empty_file_is_text_because_it_is_a_file_with_nothing_in_it()
    {
        // Answering "this is a binary" here would be a wrong answer to a real question, and the
        // caller would stop looking for content that is genuinely absent rather than unreadable.
        AttachmentText.IsText([]).ShouldBeTrue();
    }

    [Fact]
    public void Binary_bytes_past_the_first_pages_still_make_the_window_binary()
    {
        // A SQL dump before its first blob, an mbox before its first attachment, a log with
        // something binary pasted into it: pages of clean text and then bytes that are not. A
        // check that sampled only the front of the window would pass this and hand the caller the
        // NUL and everything after it as though it were text.
        var file = new byte[10_000];

        Array.Fill(file, (byte)'a');
        file[5_000] = 0x00;
        file[5_001] = 0xFF;

        AttachmentText.IsText(file).ShouldBeFalse();
    }

    [Fact]
    public void An_invalid_sequence_in_the_middle_is_not_mistaken_for_a_cut_off_character()
    {
        // 0xC3 0x28 is invalid rather than incomplete, and it is nowhere near the end, so the
        // window is not text however much valid text surrounds it.
        var file = Encoding.UTF8.GetBytes(new string('a', 500) + "  " + new string('b', 500))
            .ToArray();

        file[500] = 0xC3;
        file[501] = 0x28;

        AttachmentText.IsText(file).ShouldBeFalse();

        // And what was readable is measurable, so a caller can be told where text stopped rather
        // than handed replacement marks for the rest.
        AttachmentText.Usable(file).ShouldBe(500);
    }

    [Fact]
    public void A_window_ending_mid_character_is_still_text()
    {
        var whole = Encoding.UTF8.GetBytes("Rêverie");

        // Cut one byte into the two-byte ê: the sequence is incomplete, not invalid, and a window
        // is a fixed number of bytes so this is the ordinary case rather than a corrupt file.
        AttachmentText.IsText(whole.AsSpan(0, 2)).ShouldBeTrue();
    }

    [Fact]
    public void The_half_character_at_a_windows_end_belongs_to_the_next_window()
    {
        var whole = Encoding.UTF8.GetBytes("Rêverie");
        var window = whole.AsSpan(0, 2);

        var usable = AttachmentText.Usable(window);

        usable.ShouldBe(1);

        // "R", with no replacement mark the caller could not tell from one the file contains.
        AttachmentText.Read(window[..usable]).ShouldBe("R");
    }

    [Fact]
    public void A_window_that_ends_on_a_character_boundary_is_used_whole()
    {
        var whole = Encoding.UTF8.GetBytes("Rê");

        AttachmentText.Usable(whole).ShouldBe(whole.Length);
        AttachmentText.Read(whole).ShouldBe("Rê");
    }
}
