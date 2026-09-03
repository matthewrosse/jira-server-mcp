using System.Text.Json;
using ModelContextProtocol.Protocol;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// What an agent observes when a write is refused (ADR-0013): whether a second request went to
/// <c>mypermissions</c> at all, what scope it carried, and which of the three answers came back in
/// the prose and in the structured half. Tool-specific branching, which ADR-0008 puts here.
/// </summary>
public sealed class PermissionAdviceProtocolTests : IAsyncLifetime
{
    private const string Endpoint = "/rest/api/2/mypermissions";

    private ProtocolSeam _seam = null!;

    public async ValueTask InitializeAsync() => _seam = await ProtocolSeam.StartAsync();

    public async ValueTask DisposeAsync() => await _seam.DisposeAsync();

    [Fact]
    public async Task A_refused_update_names_the_permission_it_claimed_and_the_issue_it_claimed_it_on()
    {
        StubUpdate(403);
        StubPermissions(("EDIT_ISSUES", false));

        var result = await CallAsync("jira_update_issue", Update());

        Text(result).ShouldContain("EDIT_ISSUES");
        Text(result).ShouldContain("does not have");

        // Issue-scoped, not project-scoped: a scheme may grant Edit Issues to the current assignee
        // or reporter, and only this scope honours that.
        Asked().ShouldContain("issueKey=PROJ-42");
    }

    [Fact]
    public async Task The_permission_it_lacked_is_a_field_and_not_only_a_sentence()
    {
        StubUpdate(403);
        StubPermissions(("EDIT_ISSUES", false));

        var structured = Structured(await CallAsync("jira_update_issue", Update()));

        structured.GetProperty("missingPermission").GetString().ShouldBe("EDIT_ISSUES");
        structured.GetProperty("statusCode").GetInt32().ShouldBe(403);
    }

    [Fact]
    public async Task A_permission_the_account_does_hold_is_reported_as_held_beside_what_it_lacks()
    {
        StubUpdate(403);
        StubPermissions(("EDIT_ISSUES", true), ("ASSIGN_ISSUES", false));

        var result = await CallAsync("jira_update_issue", Update());

        // The whole point of the held branch: the refusal was something else, and the one write
        // permission the account is actually short of is named while the answer is in hand.
        Text(result).ShouldContain("does have EDIT_ISSUES");
        Text(result).ShouldContain("ASSIGN_ISSUES");

        // The field is absent rather than null on this branch — it names a permission that is
        // missing, and here none is.
        Structured(result).TryGetProperty("missingPermission", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_lookup_that_fails_leaves_the_refusal_saying_exactly_what_it_said_before()
    {
        StubUpdate(403);

        _seam.Jira.Given(Request.Create().WithPath(Endpoint).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        var result = await CallAsync("jira_update_issue", Update());

        Text(result).ShouldContain("does not have permission for it on");
        Text(result).ShouldNotContain("EDIT_ISSUES");

        // A diagnostic that reports its own failure teaches nothing about the write and reads like
        // a third failure.
        Text(result).ShouldNotContain("mypermissions");
        Structured(result).TryGetProperty("missingPermission", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_create_is_asked_about_by_project_because_there_is_no_issue_to_ask_about()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(403));

        StubPermissions(("CREATE_ISSUES", false));

        var result = await CallAsync(
            "jira_create_issue",
            new Dictionary<string, object?>
            {
                ["projectKey"] = "PROJ",
                ["issueType"] = "Bug",
                ["summary"] = "The login page returns 500",
            });

        Text(result).ShouldContain("CREATE_ISSUES");
        Asked().ShouldContain("projectKey=PROJ");
    }

    [Fact]
    public async Task A_refused_read_asks_nothing_because_a_read_claims_no_permission()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(403));

        StubPermissions(("BROWSE_PROJECTS", false));

        await CallAsync(
            "jira_search",
            new Dictionary<string, object?> { ["jql"] = "project = PROJ" });

        // A read that lacks Browse Projects is answered 404, never 403, so there is no browse row
        // to report — and a lookup on every refused read would spend a round trip on nothing.
        Paths().ShouldNotContain(Endpoint);
    }

    [Fact]
    public async Task A_failure_that_is_not_a_refusal_asks_nothing_either()
    {
        StubUpdate(500);
        StubPermissions(("EDIT_ISSUES", false));

        await CallAsync("jira_update_issue", Update());

        Paths().ShouldNotContain(Endpoint);
    }

    /// <summary>
    /// The refusal #142 was filed for. A link refused for a missing Jira permission answers 401 on
    /// 8.20.7 — the same status a revoked token answers — and the lookup is what tells the two
    /// apart: it is made with the same token, so an answer coming back at all proves the credential
    /// is live. The write is a stand-in for the link here; the gate is on the status and the claim,
    /// not on the endpoint.
    /// </summary>
    [Fact]
    public async Task A_401_that_is_a_refusal_names_the_permission_instead_of_the_login_command()
    {
        StubUpdate(401);
        StubPermissions(("EDIT_ISSUES", false));

        var result = await CallAsync("jira_update_issue", Update());

        Text(result).ShouldContain("EDIT_ISSUES");
        Text(result).ShouldContain("does not have");

        // The whole defect: an unattended loop told to mint a token burns a credential rotation on
        // a permission problem.
        Text(result).ShouldNotContain("auth login");

        Asked().ShouldContain("issueKey=PROJ-42");
    }

    [Fact]
    public async Task A_401_that_is_a_refusal_carries_the_permission_as_a_field_too()
    {
        StubUpdate(401);
        StubPermissions(("EDIT_ISSUES", false));

        var structured = Structured(await CallAsync("jira_update_issue", Update()));

        structured.GetProperty("missingPermission").GetString().ShouldBe("EDIT_ISSUES");
        structured.GetProperty("statusCode").GetInt32().ShouldBe(401);
    }

    /// <summary>
    /// The token really is revoked. It cannot read <c>mypermissions</c> either, so the lookup
    /// answers nothing and the message falls back — still naming the login command, because that is
    /// still the right first move, and now also naming the other cause rather than asserting one.
    /// </summary>
    [Fact]
    public async Task A_401_nobody_could_ask_about_still_names_the_login_command_and_the_other_cause()
    {
        StubUpdate(401);

        _seam.Jira.Given(Request.Create().WithPath(Endpoint).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(401));

        var result = await CallAsync("jira_update_issue", Update());

        Text(result).ShouldContain("jira-server-mcp auth login work");
        Text(result).ShouldContain("missing Jira permission");
        Text(result).ShouldNotContain("EDIT_ISSUES");

        // A diagnostic that reports its own failure teaches nothing about the write (ADR-0013).
        Text(result).ShouldNotContain("mypermissions");
        Structured(result).TryGetProperty("missingPermission", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Both causes ruled out at once, which is the one genuinely new sentence this change makes: the
    /// lookup answered, so the token is live, and it answered "held", so the permission is not it
    /// either. Without this an agent has nothing to stop it looping on <c>auth login</c>.
    /// </summary>
    [Fact]
    public async Task A_401_where_the_account_holds_the_permission_rules_out_the_token_as_well()
    {
        StubUpdate(401);
        StubPermissions(("EDIT_ISSUES", true), ("ASSIGN_ISSUES", false));

        var result = await CallAsync("jira_update_issue", Update());

        Text(result).ShouldContain("does have EDIT_ISSUES");
        Text(result).ShouldContain("neither invalid nor revoked");

        // The 403 tail names causes that are 403's alone; saying them under a 401 would be the same
        // defect this issue exists to fix, one status code along.
        Text(result).ShouldNotContain("read-only");
    }

    [Fact]
    public async Task A_read_refused_401_asks_nothing_because_a_read_claims_no_permission()
    {
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(401));

        StubPermissions(("BROWSE_PROJECTS", false));

        var result = await CallAsync(
            "jira_search",
            new Dictionary<string, object?> { ["jql"] = "project = PROJ" });

        Paths().ShouldNotContain(Endpoint);

        // The sentence a read has always had, unhedged: there is no permission story to tell here.
        Text(result).ShouldContain("is invalid or revoked");
    }

    private static Dictionary<string, object?> Update() =>
        new()
        {
            ["key"] = "PROJ-42",
            ["fields"] = new Dictionary<string, object?> { ["summary"] = "Renamed" },
        };

    private void StubUpdate(int status) =>
        _seam.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PROJ-42").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(status));

    /// <summary>
    /// Jira Server answers with its whole enumeration whatever is asked for — there is no
    /// <c>permissions=</c> filter on 8.20.7 — so the double answers with a map too, and the keys
    /// nobody names are simply not in it.
    /// </summary>
    private void StubPermissions(params (string Key, bool Held)[] permissions)
    {
        var rows = permissions.Select(permission =>
            $$"""
              "{{permission.Key}}": {
                "id": "10", "key": "{{permission.Key}}", "name": "A name an admin may change",
                "type": "PROJECT", "havePermission": {{(permission.Held ? "true" : "false")}}
              }
              """);

        _seam.Jira.Given(Request.Create().WithPath(Endpoint).UsingGet())
            .RespondWith(JiraResponse.Json(
                200, $$"""{ "permissions": { {{string.Join(",", rows)}} } }"""));
    }

    /// <summary>
    /// The lookup itself, which every asserting test expects to have happened exactly once. Once,
    /// because the round trip is affordable on a path that has already failed and would not be if
    /// it repeated.
    /// </summary>
    private string Asked() =>
        _seam.Jira.LogEntries
            .Select(entry => entry.RequestMessage.ShouldNotBeNull())
            .Where(request => request.Path == Endpoint)
            .ShouldHaveSingleItem()
            .Url
            .ShouldNotBeNull();

    /// <summary>Every path the double was asked for, for the tests that assert an absence.</summary>
    private IEnumerable<string> Paths() =>
        _seam.Jira.LogEntries.Select(entry => entry.RequestMessage.ShouldNotBeNull().Path);

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().ShouldHaveSingleItem().Text;

    private static JsonElement Structured(CallToolResult result) =>
        result.StructuredContent.ShouldNotBeNull();

    private async Task<CallToolResult> CallAsync(
        string tool,
        IReadOnlyDictionary<string, object?> arguments)
    {
        var client = await _seam.ConnectAsync("issues:write");

        var result = await client.CallToolAsync(
            tool,
            arguments,
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsError.ShouldBe(true);

        return result;
    }
}
