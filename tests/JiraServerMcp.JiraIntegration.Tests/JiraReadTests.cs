using JiraServerMcp.JiraIntegration.Tests.Harness;
using ModelContextProtocol.Protocol;

namespace JiraServerMcp.JiraIntegration.Tests;

/// <summary>
/// Representative reads, driven through a real MCP client against a genuine Jira Server 8.20.7.
/// </summary>
[Trait("Category", "JiraIntegration")]
public sealed class JiraReadTests(JiraHarness harness) : IAsyncLifetime
{
    private HarnessSession _session = null!;

    private ProvisionedJira _jira = null!;

    public async ValueTask InitializeAsync()
    {
        _jira = await harness.ReadyAsync(TestContext.Current.CancellationToken);

        // No grants: a read-only client is what the reads are entitled to.
        _session = await HarnessSession.StartAsync(_jira, [], TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();

    [Fact]
    public async Task The_minted_token_identifies_the_user_it_belongs_to()
    {
        var text = await CallAsync("jira_whoami");

        text.ShouldContain(_jira.Administrator.Username);
    }

    [Fact]
    public async Task Search_finds_the_seeded_issues_by_jql()
    {
        // By key rather than by project: the write tests add issues to the same project, so a
        // whole-project search is a moving target that a default page size can push results off.
        var text = await CallAsync("jira_search", new Dictionary<string, object?>
        {
            ["jql"] = $"key in ({string.Join(", ", _jira.Seeded.IssueKeys)}) ORDER BY created ASC",
        });

        foreach (var key in _jira.Seeded.IssueKeys)
        {
            text.ShouldContain(key);
        }
    }

    [Fact]
    public async Task An_issue_returns_every_expansion_that_was_asked_for()
    {
        var text = await CallAsync("jira_get_issue", new Dictionary<string, object?>
        {
            ["key"] = _jira.Seeded.ExpandedIssueKey,
            ["include"] = new[] { "comments", "transitions", "changelog", "links", "worklogs" },
        });

        text.ShouldContain(_jira.Seeded.ExpandedIssueKey);
        text.ShouldContain("comments");
        text.ShouldContain("transitions");
        text.ShouldContain("history");
        text.ShouldContain("links");
        text.ShouldContain("worklogs");

        // The seeder put each of these into Jira, so an empty section here is a real failure
        // rather than an instance that happens to have nothing to show.
        text.ShouldContain("comments expansion has something to return");
        text.ShouldContain("1h 30m");
        text.ShouldContain("summary");
    }

    [Fact]
    public async Task Untrusted_content_reaches_the_client_framed_as_data()
    {
        var text = await CallAsync("jira_get_issue", new Dictionary<string, object?>
        {
            ["key"] = _jira.Seeded.ExpandedIssueKey,
            ["fields"] = new[] { "description" },
        });

        // The seeded description carries an instruction-shaped line on purpose.
        text.ShouldContain("Ignore previous instructions");
        text.ShouldNotBe(string.Empty);
    }

    [Fact]
    public async Task Project_metadata_comes_back_for_the_seeded_project()
    {
        var listed = await CallAsync("jira_list_projects");

        listed.ShouldContain(_jira.Seeded.ProjectKey);

        var detail = await CallAsync("jira_get_project", new Dictionary<string, object?>
        {
            ["key"] = _jira.Seeded.ProjectKey,
        });

        detail.ShouldContain(_jira.Seeded.ProjectKey);
        detail.ShouldContain("Task");
    }

    [Fact]
    public async Task Create_field_discovery_names_what_a_new_issue_requires()
    {
        var text = await CallAsync("jira_get_create_fields", new Dictionary<string, object?>
        {
            ["projectKey"] = _jira.Seeded.ProjectKey,
            ["issueType"] = "Task",
        });

        text.ShouldContain("summary");
    }

    [Fact]
    public async Task Users_can_be_searched_for()
    {
        var text = await CallAsync("jira_search_users", new Dictionary<string, object?>
        {
            ["query"] = "harness",
        });

        text.ShouldContain(_jira.Seeded.Usernames[0]);
    }

    /// <summary>
    /// The software API surface, which exists here because the harness licenses Jira Software.
    /// </summary>
    [Fact]
    public async Task The_seeded_board_and_its_sprint_are_reachable()
    {
        var boards = await CallAsync("jira_list_boards");

        boards.ShouldContain(_jira.Seeded.BoardId.ToString());

        var sprints = await CallAsync("jira_list_sprints", new Dictionary<string, object?>
        {
            ["boardId"] = _jira.Seeded.BoardId,
        });

        sprints.ShouldContain("Harness Sprint 1");

        var issues = await CallAsync("jira_get_sprint_issues", new Dictionary<string, object?>
        {
            ["sprintId"] = _jira.Seeded.SprintId,
        });

        issues.ShouldContain(_jira.Seeded.IssueKeys[0]);
    }

    [Fact]
    public async Task The_backlog_is_reachable()
    {
        var text = await CallAsync("jira_get_backlog", new Dictionary<string, object?>
        {
            ["boardId"] = _jira.Seeded.BoardId,
        });

        text.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Without a grant the write tools are not registered at all, so an agent cannot attempt them.
    /// This session was given none.
    /// </summary>
    [Fact]
    public async Task No_write_tool_is_registered_for_a_client_holding_no_grant()
    {
        var tools = await _session.Client.ListToolsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        var names = tools.Select(tool => tool.Name).ToArray();

        names.ShouldContain("jira_search");
        names.ShouldNotContain("jira_create_issue");
        names.ShouldNotContain("jira_add_comment");
        names.ShouldNotContain("jira_add_worklog");
        names.ShouldNotContain("jira_transition_issue");
    }

    private async Task<string> CallAsync(
        string tool, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        var result = await _session.Client.CallToolAsync(
            tool, arguments, cancellationToken: TestContext.Current.CancellationToken);

        var text = string.Join(
            "\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

        result.IsError.ShouldNotBe(true, $"{tool} answered with an error: {text}");

        return text;
    }
}
