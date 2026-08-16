using JiraServerMcp.Tools;

namespace JiraServerMcp.Tests;

/// <summary>
/// What a worklog is allowed to say before anything is sent to Jira. Both checks exist so that a
/// value Jira would reject is refused while the agent can still fix it, rather than costing a round
/// trip and coming back as a bare 400.
/// </summary>
public sealed class WorklogInputTests
{
    [Theory]
    [InlineData("3h 30m")]
    [InlineData("30m")]
    [InlineData("1w 2d 3h 30m")]
    [InlineData("1.5h")]
    [InlineData("  3h   30m  ")]
    [InlineData("3H 30M")]
    [InlineData("3h30m")]
    public void Jiras_own_duration_syntax_is_accepted(string duration) =>
        WorklogInput.IsDuration(duration).ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("three hours")]
    [InlineData("3 hours")]
    [InlineData("PT3H30M")]
    [InlineData("3h\n30m")]
    [InlineData("-3h")]
    [InlineData("3y")]
    public void Anything_that_is_not_jiras_duration_syntax_is_refused(string duration) =>
        WorklogInput.IsDuration(duration).ShouldBeFalse();

    [Fact]
    public void A_bare_number_is_refused_because_the_unit_it_means_is_an_administrator_setting() =>
        // Jira reads it as whatever the instance's default unit is, so "90" is not a duration the
        // caller can be sure of. Naming the unit costs one character.
        WorklogInput.IsDuration("90").ShouldBeFalse();

    [Theory]
    [InlineData("2026-08-16T09:00:00+02:00", "2026-08-16T09:00:00.000+0200")]
    [InlineData("2026-08-16T09:00:00Z", "2026-08-16T09:00:00.000+0000")]
    [InlineData("2026-08-16T09:00:00.250-05:30", "2026-08-16T09:00:00.250-0530")]
    public void A_start_time_is_rewritten_into_the_form_jira_accepts(string given, string expected)
    {
        WorklogInput.TryStartTime(given, out var started).ShouldBeTrue();

        started.ShouldBe(expected);
    }

    [Theory]
    [InlineData("yesterday")]
    [InlineData("2026-08-16")]
    [InlineData("")]
    public void A_start_time_jira_could_not_read_is_refused(string given) =>
        WorklogInput.TryStartTime(given, out _).ShouldBeFalse();

    [Fact]
    public void A_start_time_without_an_offset_is_refused_rather_than_guessed_at() =>
        // Assuming this machine's offset would log the work at the wrong time on any instance whose
        // users are not all in one place.
        WorklogInput.TryStartTime("2026-08-16T09:00:00", out _).ShouldBeFalse();
}
