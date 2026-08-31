using System.Net.Http.Headers;
using JiraServerMcp.Jira;
using JiraServerMcp.JiraIntegration.Tests.Harness;

namespace JiraServerMcp.JiraIntegration.Tests;

/// <summary>
/// The permission lookup against a genuine Jira Server 8.20.7 (ADR-0013). What a double cannot
/// settle is what this endpoint actually answers on the version the primary users run: whether the
/// keys the operation table names exist at all, whether Jira honours a <c>permissions=</c> filter,
/// and what shape the body has. Every one of those was assumed by an earlier draft, and #125 set
/// the precedent that a permission fact is checked against a real Jira rather than against a double
/// that agrees with whatever its author assumed.
/// </summary>
/// <remarks>
/// What is deliberately not here is an end-to-end refused write. Producing one needs a second
/// account and a permission scheme narrowed around it, which is a harness of its own — and it was
/// run by hand while this was built. The measured result is recorded in ADR-0013: on 8.20.7 only
/// the attachment and the remote link answer <c>403</c> for a missing permission at all.
/// </remarks>
[Trait("Category", "JiraIntegration")]
public sealed class JiraPermissionAdviceTests(JiraHarness harness) : IAsyncLifetime
{
    /// <summary>
    /// Every key the operation table can claim, plus the one it only ever reports beside another.
    /// A key Jira does not answer for is reported as nothing at all, so a table naming a key this
    /// Jira has never heard of would fail silently rather than loudly.
    /// </summary>
    private static readonly string[] _claimable =
    [
        "CREATE_ISSUES",
        "EDIT_ISSUES",
        "TRANSITION_ISSUES",
        "ADD_COMMENTS",
        "WORK_ON_ISSUES",
        "CREATE_ATTACHMENTS",
        "LINK_ISSUES",
        "ASSIGN_ISSUES",
    ];

    private ProvisionedJira _jira = null!;

    private HttpClient _http = null!;

    private JiraClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _jira = await harness.ReadyAsync(TestContext.Current.CancellationToken);

        _http = new HttpClient { BaseAddress = _jira.BaseUrl };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _jira.PersonalAccessToken);

        _client = new JiraClient(_http);
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Every_permission_the_operation_table_names_is_one_this_jira_answers_for()
    {
        var held = await _client.GetMyPermissionsAsync(
            _jira.Seeded.TaskIssueKey, null, TestContext.Current.CancellationToken);

        foreach (var key in _claimable)
        {
            held.ShouldContainKey(
                key,
                $"Jira Server 8.20.7 does not know the permission key '{key}', so a refusal " +
                "claiming it would be explained with a key nobody can grant.");
        }
    }

    [Fact]
    public async Task An_account_that_may_write_is_reported_as_holding_what_it_claims()
    {
        var held = await _client.GetMyPermissionsAsync(
            _jira.Seeded.TaskIssueKey, null, TestContext.Current.CancellationToken);

        // The token belongs to the administrator, who may write here — so the held branch is the
        // one this run can prove, and proving it proves the flag is read the right way round.
        held["EDIT_ISSUES"].ShouldBeTrue();
        held["ADD_COMMENTS"].ShouldBeTrue();
    }

    [Fact]
    public async Task A_project_is_a_scope_as_well_as_an_issue_because_a_create_has_no_issue_yet()
    {
        var held = await _client.GetMyPermissionsAsync(
            null, _jira.Seeded.ProjectKey, TestContext.Current.CancellationToken);

        held["CREATE_ISSUES"].ShouldBeTrue();
    }

    /// <summary>
    /// The fact the whole design rests on. The <c>permissions=</c> filter is a Jira Cloud v3
    /// addition; Server ignores it and answers with the full enumeration whatever is asked. That is
    /// why the client filters locally, and it is also what makes naming the *other* permissions the
    /// account lacks cost nothing beyond the round trip already spent.
    /// </summary>
    [Fact]
    public async Task Jira_server_has_no_permissions_filter_and_answers_with_the_whole_enumeration()
    {
        using var whole = await _http.GetAsync(
            $"rest/api/2/mypermissions?issueKey={_jira.Seeded.TaskIssueKey}",
            TestContext.Current.CancellationToken);

        using var filtered = await _http.GetAsync(
            $"rest/api/2/mypermissions?issueKey={_jira.Seeded.TaskIssueKey}"
            + "&permissions=EDIT_ISSUES",
            TestContext.Current.CancellationToken);

        whole.EnsureSuccessStatusCode();
        filtered.EnsureSuccessStatusCode();

        var everything = await _client.GetMyPermissionsAsync(
            _jira.Seeded.TaskIssueKey, null, TestContext.Current.CancellationToken);

        everything.Count.ShouldBeGreaterThan(_claimable.Length);

        (await whole.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Length
            .ShouldBe(
                (await filtered.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
                .Length,
                "Jira Server answered a filtered request differently, so it now honours " +
                "'permissions=' and the client may stop asking for everything.");
    }
}
