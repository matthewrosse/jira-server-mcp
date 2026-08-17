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
            ["issues:write", "comments:write", "worklogs:write"],
            TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();

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
    }

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
