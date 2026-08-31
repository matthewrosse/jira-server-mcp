using System.Text.Json;
using JiraServerMcp.JiraIntegration.Tests.Harness;
using ModelContextProtocol.Protocol;

namespace JiraServerMcp.JiraIntegration.Tests;

/// <summary>
/// Representative writes, driven through a real MCP client against a genuine Jira Server 8.20.7.
/// Every one of them is asserted twice: what the tool reported, and what Jira actually holds
/// afterwards, read back over the platform API.
/// </summary>
[Trait("Category", "JiraIntegration")]
public sealed class JiraWriteTests(JiraHarness harness) : IAsyncLifetime
{
    private HarnessSession _session = null!;

    private ProvisionedJira _jira = null!;

    public async ValueTask InitializeAsync()
    {
        _jira = await harness.ReadyAsync(TestContext.Current.CancellationToken);

        _session = await HarnessSession.StartAsync(
            _jira,
            ["issues:write", "comments:write", "worklogs:write", "links:write"],
            TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();

    /// <summary>
    /// The one claim fixtures cannot make. A recorded payload proves the reader parses what it was
    /// handed; that two issue types in one project publish different edit screens is a fact about
    /// Jira, and only a real Jira can be asked.
    /// </summary>
    [Fact]
    public async Task The_edit_screen_differs_between_two_issue_types_of_one_project()
    {
        var bug = await CallAsync("jira_get_edit_fields", new Dictionary<string, object?>
        {
            ["key"] = _jira.Seeded.BugIssueKey,
        });

        var task = await CallAsync("jira_get_edit_fields", new Dictionary<string, object?>
        {
            ["key"] = _jira.Seeded.TaskIssueKey,
        });

        bug.ShouldContain("environment");

        // Named rather than by identifier: both screens carry fixVersions, whose identifier
        // contains "versions", so the identifier alone cannot tell the two screens apart.
        bug.ShouldContain("Affects Version/s");

        // jira_update_issue takes a key, not a type — so answering "what may I set here" through
        // the create screen answers about the wrong screen.
        task.ShouldNotContain("environment");
        task.ShouldNotContain("Affects Version/s");
    }

    [Fact]
    public async Task Creating_an_issue_puts_it_in_jira()
    {
        var summary = "Created through the protocol " + Guid.NewGuid().ToString("N")[..8];

        var text = await CallAsync("jira_create_issue", new Dictionary<string, object?>
        {
            ["projectKey"] = _jira.Seeded.ProjectKey,
            ["issueType"] = "Task",
            ["summary"] = summary,
        });

        var key = KeyFrom(text);

        var issue = await _session.ReadIssueAsync(key, TestContext.Current.CancellationToken);

        issue.GetProperty("fields").GetProperty("summary").GetString().ShouldBe(summary);
        issue.GetProperty("fields").GetProperty("project").GetProperty("key").GetString()
            .ShouldBe(_jira.Seeded.ProjectKey);
    }

    [Fact]
    public async Task Updating_an_issue_changes_it_in_jira()
    {
        var key = await CreateIssueAsync("To be updated");
        var summary = "Updated through the protocol " + Guid.NewGuid().ToString("N")[..8];

        await CallAsync("jira_update_issue", new Dictionary<string, object?>
        {
            ["key"] = key,
            ["fields"] = new Dictionary<string, object?> { ["summary"] = summary },
        });

        var issue = await _session.ReadIssueAsync(key, TestContext.Current.CancellationToken);

        issue.GetProperty("fields").GetProperty("summary").GetString().ShouldBe(summary);
    }

    [Fact]
    public async Task Transitioning_an_issue_moves_its_status_in_jira()
    {
        var key = await CreateIssueAsync("To be transitioned");

        var before = await _session.ReadIssueAsync(key, TestContext.Current.CancellationToken);
        var statusBefore = StatusOf(before);

        // Asking for the transitions first is what the tool's own description tells an agent to
        // do, and it makes this test independent of the workflow's spelling.
        var offered = await CallAsync("jira_get_issues", new Dictionary<string, object?>
        {
            ["keys"] = new[] { key },
            ["include"] = new[] { "transitions" },
        });

        var transition = TransitionLeavingStatus(offered, statusBefore);

        await CallAsync("jira_transition_issue", new Dictionary<string, object?>
        {
            ["key"] = key,
            ["transition"] = transition,
        });

        var after = await _session.ReadIssueAsync(key, TestContext.Current.CancellationToken);

        StatusOf(after).ShouldNotBe(statusBefore);
    }

    [Fact]
    public async Task Commenting_puts_the_comment_in_jira()
    {
        var key = await CreateIssueAsync("To be commented on");
        var body = "A comment written through the protocol " + Guid.NewGuid().ToString("N")[..8];

        await CallAsync("jira_add_comment", new Dictionary<string, object?>
        {
            ["key"] = key,
            ["body"] = body,
        });

        var comments = await _session.ReadAsync(
            $"/rest/api/2/issue/{key}/comment", TestContext.Current.CancellationToken);

        comments.GetProperty("comments").EnumerateArray()
            .Select(comment => comment.GetProperty("body").GetString())
            .ShouldContain(body);
    }

    [Fact]
    public async Task Logging_work_puts_the_worklog_in_jira()
    {
        var key = await CreateIssueAsync("To have work logged against it");

        await CallAsync("jira_add_worklog", new Dictionary<string, object?>
        {
            ["key"] = key,
            ["timeSpent"] = "2h",
            ["comment"] = "Logged through the protocol.",
        });

        var worklogs = await _session.ReadAsync(
            $"/rest/api/2/issue/{key}/worklog", TestContext.Current.CancellationToken);

        var logged = worklogs.GetProperty("worklogs").EnumerateArray().ToArray();

        logged.Length.ShouldBe(1);
        logged[0].GetProperty("timeSpent").GetString().ShouldBe("2h");
        logged[0].GetProperty("comment").GetString().ShouldBe("Logged through the protocol.");
    }

    /// <summary>
    /// What a worklog does to the remaining estimate, which no recorded payload can hold: Jira's
    /// answer to a worklog POST carries the worklog and nothing about the issue's time tracking,
    /// so only a real Jira can be asked what moved.
    /// </summary>
    [Fact]
    public async Task Logging_work_reduces_the_remaining_estimate_unless_it_is_asked_not_to()
    {
        var key = await CreateEstimatedIssueAsync("8h");

        await CallAsync("jira_add_worklog", new Dictionary<string, object?>
        {
            ["key"] = key,
            ["timeSpent"] = "2h",
        });

        // In seconds, because Jira renders a duration in its own working days — "8h" comes back
        // as "1d" on an eight-hour day — and this is a claim about the number, not the wording.
        RemainingEstimateSeconds(await ReadTimeTrackingAsync(key)).ShouldBe(6 * 3600);

        await CallAsync("jira_add_worklog", new Dictionary<string, object?>
        {
            ["key"] = key,
            ["timeSpent"] = "2h",
            ["leaveRemainingEstimate"] = true,
        });

        var timeTracking = await ReadTimeTrackingAsync(key);

        // The second worklog was logged — the estimate is what stayed still, not the time spent.
        RemainingEstimateSeconds(timeTracking).ShouldBe(6 * 3600);
        timeTracking.GetProperty("timeSpentSeconds").GetInt32().ShouldBe(4 * 3600);
    }

    [Fact]
    public async Task Linking_two_issues_puts_the_link_in_jira()
    {
        var blocker = await CreateIssueAsync("To block another issue");
        var blocked = await CreateIssueAsync("To be blocked by another issue");

        await CallAsync("jira_link_issues", new Dictionary<string, object?>
        {
            ["from"] = blocker,
            ["to"] = blocked,
            ["relation"] = "blocks",
        });

        // Read from the blocked end, where Jira words the link the other way round: that the two
        // wordings describe one link is the whole premise of the relation phrase (ADR-0010).
        var issue = await _session.ReadAsync(
            $"/rest/api/2/issue/{blocked}?fields=issuelinks", TestContext.Current.CancellationToken);

        var link = issue.GetProperty("fields").GetProperty("issuelinks").EnumerateArray()
            .ShouldHaveSingleItem();

        link.GetProperty("type").GetProperty("name").GetString().ShouldBe("Blocks");

        // Named rather than indexed: Jira puts the *other* issue under the end that other issue is
        // on, so read from the blocked end the blocker arrives as the outwardIssue — the end the
        // outward wording "blocks" starts from. A missing property is the interesting answer here
        // and a KeyNotFoundException hides it.
        //
        // This assertion was inwardIssue and a real 8.20.7 falsified it twice, on two instances
        // seeded from scratch. IssueDetailReader.Link reads the same payload the other way round —
        // it takes outwardIssue to mean this issue is on the outward end — so the links expansion
        // words this link "blocks" when read from the blocked end. Only the wording is wrong, and
        // fixing it is a change to the reader, filed as #137.
        link.TryGetProperty("outwardIssue", out var outward).ShouldBeTrue(
            $"read from the blocked end, Jira described the link as: {link}");

        outward.GetProperty("key").GetString().ShouldBe(blocker);
    }

    [Fact]
    public async Task A_remote_link_appears_on_the_issue_and_the_same_url_twice_is_one_link()
    {
        var key = await CreateIssueAsync("To carry a pull request");
        var url = "https://github.com/acme/web/pull/" + Guid.NewGuid().ToString("N")[..8];

        await CallAsync("jira_add_remote_link", new Dictionary<string, object?>
        {
            ["key"] = key,
            ["url"] = url,
            ["title"] = "The pull request",
            ["relationship"] = "pull request",
        });

        var repeated = await CallAsync("jira_add_remote_link", new Dictionary<string, object?>
        {
            ["key"] = key,
            ["url"] = url,
            ["title"] = "The pull request, merged",
            ["relationship"] = "pull request",
        });

        repeated.ShouldContain("already attached");

        var links = await _session.ReadAsync(
            $"/rest/api/2/issue/{key}/remotelink", TestContext.Current.CancellationToken);

        // The URL is the globalId, so the second call updated the link rather than adding one.
        var link = links.EnumerateArray().ShouldHaveSingleItem();

        link.GetProperty("globalId").GetString().ShouldBe(url);
        link.GetProperty("object").GetProperty("title").GetString()
            .ShouldBe("The pull request, merged");
    }

    /// <summary>
    /// The write tools this client was granted, and no others: worklogs and comments and issues
    /// were asked for, so all three are registered.
    /// </summary>
    [Fact]
    public async Task The_granted_write_tools_are_the_ones_registered()
    {
        var tools = await _session.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var names = tools.Select(tool => tool.Name).ToArray();

        names.ShouldContain("jira_create_issue");
        names.ShouldContain("jira_update_issue");
        names.ShouldContain("jira_transition_issue");
        names.ShouldContain("jira_add_comment");
        names.ShouldContain("jira_add_worklog");
        names.ShouldContain("jira_link_issues");
        names.ShouldContain("jira_add_remote_link");
    }

    /// <summary>
    /// An issue carrying an original estimate, created the way an operator would — through the
    /// tool, with Jira's own <c>timetracking</c> field. The harness puts that field on the
    /// project's screens, which the scrum template does not.
    /// </summary>
    private async Task<string> CreateEstimatedIssueAsync(string originalEstimate)
    {
        var text = await CallAsync("jira_create_issue", new Dictionary<string, object?>
        {
            ["projectKey"] = _jira.Seeded.ProjectKey,
            ["issueType"] = "Task",
            ["summary"] = "To have work logged against an estimate "
                + Guid.NewGuid().ToString("N")[..8],
            ["fields"] = new Dictionary<string, object?>
            {
                ["timetracking"] = new Dictionary<string, object?>
                {
                    ["originalEstimate"] = originalEstimate,
                },
            },
        });

        return KeyFrom(text);
    }

    private async Task<JsonElement> ReadTimeTrackingAsync(string key)
    {
        var issue = await _session.ReadAsync(
            $"/rest/api/2/issue/{key}?fields=timetracking", TestContext.Current.CancellationToken);

        return issue.GetProperty("fields").GetProperty("timetracking");
    }

    private static int RemainingEstimateSeconds(JsonElement timeTracking) =>
        timeTracking.GetProperty("remainingEstimateSeconds").GetInt32();

    private async Task<string> CreateIssueAsync(string summary)
    {
        var text = await CallAsync("jira_create_issue", new Dictionary<string, object?>
        {
            ["projectKey"] = _jira.Seeded.ProjectKey,
            ["issueType"] = "Task",
            ["summary"] = summary + " " + Guid.NewGuid().ToString("N")[..8],
        });

        return KeyFrom(text);
    }

    private string KeyFrom(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            text, _jira.Seeded.ProjectKey + @"-\d+");

        match.Success.ShouldBeTrue($"No issue key in the tool's reply: {text}");

        return match.Value;
    }

    /// <summary>
    /// A transition that actually leaves the current status, off the rendered transitions section.
    /// </summary>
    /// <remarks>
    /// Jira's default workflow offers the issue's own status back as a transition — a new issue in
    /// "To Do" is offered "To Do", "In Progress" and "Done", in that order. Taking the first one
    /// moves nothing, so the target status is what this selects on.
    /// </remarks>
    private static string TransitionLeavingStatus(string rendered, string currentStatus)
    {
        // "  In Progress (id 21) to In Progress" — optionally followed by " — requires: …".
        var offered = rendered.Split('\n')
            .SkipWhile(line => !line.StartsWith("transitions", StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => line.StartsWith("  ", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .Where(line => line.Contains(" (id ", StringComparison.Ordinal))
            .Select(line => new
            {
                Name = line.Split(" (id ")[0],
                Target = line.Split(" to ") is [_, var target, ..]
                    ? target.Split(" — ")[0].Trim()
                    : string.Empty,
            })
            .ToArray();

        offered.ShouldNotBeEmpty($"No transitions were offered: {rendered}");

        var moving = offered.FirstOrDefault(
            transition => !string.Equals(transition.Target, currentStatus, StringComparison.Ordinal));

        moving.ShouldNotBeNull(
            $"Every transition offered leads back to {currentStatus}: {rendered}");

        return moving.Name;
    }

    private static string StatusOf(JsonElement issue) =>
        issue.GetProperty("fields").GetProperty("status").GetProperty("name").GetString()!;

    private async Task<string> CallAsync(string tool, IReadOnlyDictionary<string, object?> arguments)
    {
        var result = await _session.Client.CallToolAsync(
            tool, arguments, cancellationToken: TestContext.Current.CancellationToken);

        var text = string.Join(
            "\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

        result.IsError.ShouldNotBe(true, $"{tool} answered with an error: {text}");

        return text;
    }
}
