using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        var text = await CallAsync("jira_get_issues", new Dictionary<string, object?>
        {
            ["keys"] = new[] { _jira.Seeded.ExpandedIssueKey },
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

    /// <summary>
    /// The sub-tasks expansion against a real 8.20.7, which is the only place the shape Jira
    /// answers with — a key, and an embedded projection carrying the status — is proven rather
    /// than assumed. The parent's own line is asserted from the other end in the same call.
    /// </summary>
    [Fact]
    public async Task Sub_tasks_come_back_with_a_status_each_and_the_parent_carries_one_too()
    {
        var text = await CallAsync("jira_get_issues", new Dictionary<string, object?>
        {
            ["keys"] = new[] { _jira.Seeded.ParentIssueKey },
            ["include"] = new[] { "subtasks" },
        });

        text.ShouldContain("subtasks (3)");

        foreach (var key in _jira.Seeded.SubtaskKeys)
        {
            text.ShouldContain(key);
        }

        text.ShouldContain("Wire the reader to the new field");

        // The seeder transitioned the last sub-task and left the other two alone, so the section
        // carries two different status names rather than the same word three times.
        var statuses = Regex
            .Matches(text, @"^  [A-Z]+-\d+ \(([^)]+)\)", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToArray();

        statuses.Length.ShouldBe(3);
        statuses.Distinct().Count().ShouldBe(2);

        // The other end of the same relation: a sub-task read carries its parent with a status.
        var child = await CallAsync("jira_get_issues", new Dictionary<string, object?>
        {
            ["keys"] = new[] { _jira.Seeded.SubtaskKeys[0] },
        });

        child.ShouldContain($"parent: {_jira.Seeded.ParentIssueKey} (");
    }

    [Fact]
    public async Task Several_keys_with_expansions_render_in_one_call_and_a_key_that_does_not_exist_fails_alone()
    {
        var text = await CallAsync("jira_get_issues", new Dictionary<string, object?>
        {
            ["keys"] = _jira.Seeded.IssueKeys.Take(2).Append("PROJ-999999").ToArray(),
            ["include"] = new[] { "comments" },
        });

        foreach (var key in _jira.Seeded.IssueKeys.Take(2))
        {
            text.ShouldContain(key);
        }

        // The seeder put a comment on the expanded issue, so its absence would be a real failure.
        text.ShouldContain("comments expansion has something to return");

        text.ShouldContain("PROJ-999999: not found or not visible");
        text.ShouldContain("3 issues asked for, 2 returned");
    }

    [Fact]
    public async Task Untrusted_content_reaches_the_client_framed_as_data()
    {
        var text = await CallAsync("jira_get_issues", new Dictionary<string, object?>
        {
            ["keys"] = new[] { _jira.Seeded.ExpandedIssueKey },
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
    /// A WireMock double stubbed to our own assumption cannot catch a parameter name Jira does not
    /// take, so what the assignable search is anchored by is proven against a real 8.20.7.
    /// </summary>
    [Fact]
    public async Task The_assignable_search_answers_for_a_project_key_and_for_an_issue_key()
    {
        var project = await CallAsync("jira_search_users", new Dictionary<string, object?>
        {
            ["assignableTo"] = _jira.Seeded.ProjectKey,
        });

        project.ShouldContain($"users assignable on {_jira.Seeded.ProjectKey}");
        project.ShouldContain(_jira.Administrator.Username);

        var issue = await CallAsync("jira_search_users", new Dictionary<string, object?>
        {
            ["query"] = _jira.Administrator.Username,
            ["assignableTo"] = _jira.Seeded.TaskIssueKey,
        });

        issue.ShouldContain($"users assignable on {_jira.Seeded.TaskIssueKey}");
        issue.ShouldContain(_jira.Administrator.Username);
    }

    [Fact]
    public async Task An_anchor_this_jira_has_never_heard_of_is_answered_with_the_advice_for_it()
    {
        var result = await _session.Client.CallToolAsync(
            "jira_search_users",
            new Dictionary<string, object?>
            {
                ["query"] = "harness",
                ["assignableTo"] = "NOSUCH",
            },
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem()
            .Text.ShouldContain("NOSUCH was not found, or you cannot browse it");
    }

    /// <summary>
    /// The gap the whole parameter exists to close, from the other direction: the plain search
    /// matches an email address and the assignable search does not, so a query that works on one
    /// finds nobody on the other. Measured on 8.20.7 rather than assumed.
    /// </summary>
    [Fact]
    public async Task The_assignable_search_does_not_match_the_email_address_the_plain_search_does()
    {
        var email = _jira.Seeded.Usernames[1] + "@example.invalid";

        var directory = await CallAsync("jira_search_users", new Dictionary<string, object?>
        {
            ["query"] = email,
        });

        directory.ShouldContain(_jira.Seeded.Usernames[1]);

        var assignable = await CallAsync("jira_search_users", new Dictionary<string, object?>
        {
            ["query"] = email,
            ["assignableTo"] = _jira.Seeded.ProjectKey,
        });

        assignable.ShouldContain("none matched");
        assignable.ShouldContain("not email addresses");
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

    /// <summary>
    /// The one claim a fixture cannot make. Every custom field's JQL name is published from
    /// Jira's own <c>cfid</c>, and the alias join parses the number out of it — so if a real
    /// 8.20.7 stopped sending that property, or sent it in another shape, the whole labelling
    /// design would be built on a double that says what it was told to say.
    /// </summary>
    [Fact]
    public async Task A_real_instance_publishes_a_custom_fields_jql_name_in_the_bracket_form()
    {
        var text = await CallAsync("jira_get_jql_fields");

        Regex.Matches(text, @"cf\[\d+\]").Count.ShouldBeGreaterThan(0,
            "This Jira publishes no custom field in the cf[NNNNN] form the alias join parses.");

        // The identifier the write tools hand out is not a JQL name, and no row claims otherwise.
        text.Split('\n')
            .Where(line => line.StartsWith("  ", StringComparison.Ordinal))
            .ShouldNotContain(line => line.Contains("customfield_", StringComparison.Ordinal));
    }

    /// <summary>
    /// The saved filter path end to end, against a real 8.20.7. Everything load-bearing in this
    /// feature came from probing rather than from reasoning — that <c>jql</c> arrives in the
    /// listing without an expand, and that <c>filter = &lt;id&gt;</c> runs at all — and a double
    /// will keep agreeing with its fixture long after Jira stops.
    /// </summary>
    [Fact]
    public async Task A_favourited_filter_is_listed_and_runs_through_search_by_its_id()
    {
        // A fresh name per run: Jira refuses a second filter of the same name for one owner, and
        // the harness may be pointed at an instance an earlier run already seeded.
        var name = $"Harness favourite {Guid.NewGuid():N}";

        var id = await CreateFavouriteFilterAsync(
            name,
            $"project = {_jira.Seeded.ProjectKey} ORDER BY created ASC");

        var listed = await CallAsync("jira_list_saved_filters");

        listed.ShouldContain(id);
        listed.ShouldContain(name);
        listed.ShouldContain($"jql: project = {_jira.Seeded.ProjectKey}");

        // The whole point of listing them: the id is what a search names, and nothing in this
        // server runs a filter.
        var run = await CallAsync("jira_search", new Dictionary<string, object?>
        {
            ["jql"] = $"filter = {id}",
        });

        run.ShouldContain(_jira.Seeded.IssueKeys[0]);

        var narrowed = await CallAsync("jira_list_saved_filters", new Dictionary<string, object?>
        {
            ["startsWith"] = "Harness favourite",
        });

        narrowed.ShouldContain(id);
    }

    /// <summary>
    /// Stars a filter as the account the personal access token belongs to — the same account the
    /// server lists the favourites of, which is what makes the listing find it.
    /// </summary>
    private async Task<string> CreateFavouriteFilterAsync(string name, string jql)
    {
        using var response = await _session.JiraApi.PostAsJsonAsync(
            "/rest/api/2/filter",
            new
            {
                name,
                description = "Seeded by the read tests. Contains *wiki markup*.",
                jql,
                favourite = true,
            },
            TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.ShouldBeTrue(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var created = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        return created.GetProperty("id").GetString().ShouldNotBeNull();
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
