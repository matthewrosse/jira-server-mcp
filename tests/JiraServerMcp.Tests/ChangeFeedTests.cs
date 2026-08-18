using System.Text.Json;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Tools;

namespace JiraServerMcp.Tests;

/// <summary>
/// The change feed's arithmetic: the window a tick asks for, and the watermark the next one
/// resumes from. Under ADR-0008 this is pure logic and is proven here rather than at the protocol
/// seam — and the timezone handling is the part most likely to be quietly wrong, because a window
/// an hour out returns a response that looks perfectly fine.
/// </summary>
public class ChangeFeedTests
{
    /// <summary>
    /// Two hours east of UTC, which is what the fixtures below call the zone Jira reads this
    /// account's queries in.
    /// </summary>
    private static readonly TimeSpan Zone = TimeSpan.FromHours(2);

    [Fact]
    public void A_moment_carrying_an_offset_is_read_as_that_moment()
    {
        ChangeFeed.TryReadSince("2026-08-18T09:00:00+02:00", out var moment).ShouldBeTrue();

        moment.ShouldBe(new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void A_moment_in_utc_is_read_as_that_moment()
    {
        ChangeFeed.TryReadSince("2026-08-18T07:00:00Z", out var moment).ShouldBeTrue();

        moment.ShouldBe(new DateTimeOffset(2026, 8, 18, 7, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("2026-08-18T09:00:00")]
    [InlineData("2026-08-18 09:00")]
    [InlineData("2026-08-18")]
    [InlineData("yesterday")]
    [InlineData("")]
    public void A_moment_with_no_offset_is_refused_rather_than_read_in_this_machines_zone(
        string since)
    {
        // Reading one of these as local time is the timezone mistake this tool exists to take off
        // the caller, and making it silently is worse than refusing.
        ChangeFeed.TryReadSince(since, out _).ShouldBeFalse();
    }

    [Fact]
    public void The_zone_is_the_accounts_own_because_that_is_the_one_jira_reads_a_query_in()
    {
        // Jira evaluates a bare date literal in the zone of the account running the query, not in
        // the instance's default. An account six hours west of its server would otherwise get a
        // window six hours out, and a window shifted forward skips changes invisibly.
        ChangeFeed.TryZoneOffset(
                "America/New_York",
                new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero),
                out var offset)
            .ShouldBeTrue();

        offset.ShouldBe(TimeSpan.FromHours(-4));
    }

    [Fact]
    public void The_offset_is_the_one_in_force_at_that_moment_rather_than_the_zones_standard_one()
    {
        var winter = new DateTimeOffset(2026, 1, 18, 9, 0, 0, TimeSpan.Zero);
        var summer = new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);

        ChangeFeed.TryZoneOffset("Europe/Warsaw", winter, out var inWinter).ShouldBeTrue();
        ChangeFeed.TryZoneOffset("Europe/Warsaw", summer, out var inSummer).ShouldBeTrue();

        inWinter.ShouldBe(TimeSpan.FromHours(1));
        inSummer.ShouldBe(TimeSpan.FromHours(2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Middle/Earth")]
    public void A_zone_this_machine_cannot_resolve_is_said_so_rather_than_guessed(string? timeZone)
    {
        ChangeFeed.TryZoneOffset(
                timeZone,
                new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero),
                out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void The_query_states_the_window_in_that_zone_oldest_change_first()
    {
        // 07:20 UTC is 09:20 in the zone the query is read in, and it is Jira's reading of the
        // literal that decides which issues come back.
        var jql = ChangeFeed.Jql(
            new DateTimeOffset(2026, 8, 18, 7, 20, 0, TimeSpan.Zero),
            Zone,
            project: null);

        jql.ShouldBe("""updated >= "2026/08/18 09:20" ORDER BY updated ASC""");
    }

    [Fact]
    public void A_project_narrows_the_window_without_changing_what_it_means()
    {
        var jql = ChangeFeed.Jql(
            new DateTimeOffset(2026, 8, 18, 9, 20, 0, Zone),
            Zone,
            project: "PROJ");

        jql.ShouldBe("""project = PROJ AND updated >= "2026/08/18 09:20" ORDER BY updated ASC""");
    }

    [Fact]
    public void The_watermark_is_the_start_of_the_last_seen_minute_not_the_last_change()
    {
        // Jira Server records update times to the minute on some versions, so a watermark taken
        // from 09:31:47 would exclude every other change made during 09:31.
        var next = ChangeFeed.NextSince(
            [
                Changed("PROJ-1", "2026-08-18T09:30:11.000+0200"),
                Changed("PROJ-2", "2026-08-18T09:31:47.412+0200"),
            ],
            new DateTimeOffset(2026, 8, 18, 9, 0, 0, Zone),
            Zone);

        next.ShouldBe("2026-08-18T09:31:00+02:00");
    }

    [Fact]
    public void The_watermark_is_restated_in_that_zone_whatever_zone_jira_wrote_it_in()
    {
        var next = ChangeFeed.NextSince(
            [Changed("PROJ-1", "2026-08-18T07:31:47.412Z")],
            new DateTimeOffset(2026, 8, 18, 9, 0, 0, Zone),
            Zone);

        next.ShouldBe("2026-08-18T09:31:00+02:00");
    }

    [Fact]
    public void A_page_with_nothing_on_it_hands_back_the_window_it_was_given()
    {
        var next = ChangeFeed.NextSince(
            [],
            new DateTimeOffset(2026, 8, 18, 9, 14, 32, Zone),
            Zone);

        // Floored for the same reason a seen change is: the loop's window is always a whole minute.
        next.ShouldBe("2026-08-18T09:14:00+02:00");
    }

    [Fact]
    public void A_row_whose_timestamp_cannot_be_read_moves_nothing()
    {
        var next = ChangeFeed.NextSince(
            [
                Changed("PROJ-1", "not a timestamp"),
                new JiraIssue("PROJ-2", new Dictionary<string, JsonElement>()),
            ],
            new DateTimeOffset(2026, 8, 18, 9, 0, 0, Zone),
            Zone);

        next.ShouldBe("2026-08-18T09:00:00+02:00");
    }

    [Fact]
    public void The_watermark_never_moves_backwards()
    {
        // An issue whose update time predates the window — a clock the instance itself moved, or
        // a row Jira ordered by something else. Handing its timestamp back would make the next
        // tick re-read everything since, forever.
        var next = ChangeFeed.NextSince(
            [Changed("PROJ-1", "2026-08-18T08:00:00+02:00")],
            new DateTimeOffset(2026, 8, 18, 9, 0, 0, Zone),
            Zone);

        next.ShouldBe("2026-08-18T09:00:00+02:00");
    }

    private static JiraIssue Changed(string key, string updated) =>
        new(
            key,
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                $$"""{ "updated": "{{updated}}" }""")!);
}
