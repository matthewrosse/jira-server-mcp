using System.Net;
using JiraServerMcp.Errors;
using JiraServerMcp.Jira.Errors;

namespace JiraServerMcp.Tests;

/// <summary>
/// What one refused write is told about the permission it claimed. Every sentence a permission
/// adds is authored in one place, so this is where the shapes a refusal can take — a <c>403</c>, a
/// <c>401</c>, an opted-in <c>400</c> — meet the four things Jira can say about a key.
/// </summary>
/// <remarks>
/// The lookup is a stubbed delegate on the claim: what is being proven is the interpretation, and
/// a real <c>mypermissions</c> read is proven at the protocol seam and against a live Jira. The
/// finished prose is read through <see cref="JiraToolError"/>, because a diagnosis that is never
/// assembled says nothing an agent could act on.
/// </remarks>
public sealed class PermissionDiagnosisTests
{
    [Fact]
    public async Task A_permission_the_account_lacks_is_named_before_the_callers_state_clause()
    {
        var message = await Refused(Claim("EDIT_ISSUES", ("EDIT_ISSUES", false)));

        message.ShouldContain("does not have EDIT_ISSUES on ABC-1");

        // Cause before consequence: what Jira refused, then why, then what that leaves behind.
        message.IndexOf("EDIT_ISSUES", StringComparison.Ordinal)
            .ShouldBeLessThan(message.IndexOf("Nothing was changed", StringComparison.Ordinal));
    }

    /// <summary>
    /// The branch that is common in practice. Only two of the eight writes reach a 403 for a
    /// missing permission on 8.20.7 — the rest answer 400 — so a 403 that arrives at all is more
    /// often read-only mode, throttling, or a header, and saying "you do have this" is the answer.
    /// </summary>
    [Fact]
    public async Task A_permission_the_account_holds_says_so_and_names_what_it_lacks_beside_it()
    {
        var message = await Refused(
            Claim("EDIT_ISSUES", ("EDIT_ISSUES", true), ("ASSIGN_ISSUES", false)));

        message.ShouldContain("does have EDIT_ISSUES on ABC-1");
        message.ShouldContain("ASSIGN_ISSUES");
    }

    [Fact]
    public async Task A_permission_answer_with_nothing_else_missing_says_that_rather_than_trailing_off()
    {
        var message = await Refused(Claim("EDIT_ISSUES", ("EDIT_ISSUES", true)));

        message.ShouldContain("every other write permission");
        message.ShouldContain("read-only");
    }

    /// <summary>
    /// The display name is admin-renameable, which makes it untrusted content — and untrusted
    /// content may only appear inside the framed region, never spliced into this server's prose.
    /// The bare key never needs framing, which is half of why it is what the sentence carries.
    /// </summary>
    [Fact]
    public async Task The_permission_sentence_is_this_servers_own_prose_and_needs_no_framing()
    {
        var message = await Refused(Claim("EDIT_ISSUES", ("EDIT_ISSUES", false)));

        message.ShouldNotContain("<jira-data");
    }

    /// <summary>
    /// The defect #142 was filed for. A write refused for a missing Jira permission answers 401 on
    /// 8.20.7, and the credential sentence sent the caller to mint a token that would fail the same
    /// way. The lookup answering at all is what proves the token is live.
    /// </summary>
    [Fact]
    public async Task A_401_that_is_a_refusal_reads_as_one_rather_than_as_a_credential_problem()
    {
        var message = await Refused(
            Claim("LINK_ISSUES", ("LINK_ISSUES", false)),
            HttpStatusCode.Unauthorized);

        message.ShouldContain("does not have LINK_ISSUES on ABC-1");
        message.ShouldNotContain("auth login");
    }

    [Fact]
    public async Task A_401_the_account_could_write_through_rules_out_the_token_as_well()
    {
        var message = await Refused(
            Claim("LINK_ISSUES", ("LINK_ISSUES", true)),
            HttpStatusCode.Unauthorized);

        message.ShouldContain("neither invalid nor revoked");

        // Read-only mode and throttling are 403's causes. Naming them under a 401 would be this
        // issue's own defect one status code along.
        message.ShouldNotContain("read-only");
    }

    /// <summary>
    /// The clause hangs off both held branches under a 401, unlike the 403 tail, because ruling the
    /// token out is the whole reason this arm exists.
    /// </summary>
    [Fact]
    public async Task A_401_the_account_could_write_through_rules_the_token_out_beside_what_it_lacks_too()
    {
        var message = await Refused(
            Claim("LINK_ISSUES", ("LINK_ISSUES", true), ("ASSIGN_ISSUES", false)),
            HttpStatusCode.Unauthorized);

        message.ShouldContain("ASSIGN_ISSUES");
        message.ShouldContain("neither invalid nor revoked");
    }

    [Fact]
    public async Task A_403_the_account_could_write_through_still_names_the_causes_that_are_403s()
    {
        var message = await Refused(Claim("EDIT_ISSUES", ("EDIT_ISSUES", true)));

        message.ShouldContain("read-only");
        message.ShouldNotContain("neither invalid nor revoked");
    }

    [Fact]
    public async Task A_400_confirmed_absent_names_the_permission_without_403_specific_prose()
    {
        var message = await Refused(
            Claim("ADD_COMMENTS", ("ADD_COMMENTS", false)),
            HttpStatusCode.BadRequest);

        message.ShouldContain("does not have ADD_COMMENTS on ABC-1");
        message.ShouldNotContain("read-only");
    }

    [Fact]
    public async Task A_400_confirmed_held_says_so_without_borrowing_the_403_tail()
    {
        var message = await Refused(
            Claim("WORK_ON_ISSUES", ("WORK_ON_ISSUES", true)),
            HttpStatusCode.BadRequest);

        message.ShouldContain("does have WORK_ON_ISSUES on ABC-1");
        message.ShouldNotContain("read-only");
        message.ShouldNotContain("neither invalid nor revoked");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_unresolved_400_keeps_the_prior_failure_wording(bool jiraAnswered)
    {
        var claim = jiraAnswered
            ? Claim("ADD_COMMENTS")
            : Unanswerable("ADD_COMMENTS");
        var message = await Refused(claim, HttpStatusCode.BadRequest);

        message.ShouldContain("updating ABC-1 failed");
        message.ShouldNotContain("ADD_COMMENTS");
    }

    /// <summary>
    /// A revoked token cannot read <c>mypermissions</c> either, so this is the shape a genuinely
    /// revoked one takes on a write. The login command stays — it is still the right first move —
    /// and the other cause is named beside it rather than the first being asserted alone.
    /// </summary>
    [Fact]
    public async Task A_401_whose_lookup_answered_nothing_names_both_causes_rather_than_asserting_one()
    {
        var message = await Refused(
            Unanswerable("LINK_ISSUES"),
            HttpStatusCode.Unauthorized);

        message.ShouldContain("jira-server-mcp auth login work");
        message.ShouldContain("missing Jira permission");

        // A diagnostic that reports its own failure teaches nothing about the write (ADR-0013), and
        // a key nobody could confirm is not a key to name.
        message.ShouldNotContain("LINK_ISSUES");
    }

    /// <summary>
    /// The state the four-way standing exists for. Jira answered — so the token is live, proved by
    /// the answer itself — but its enumeration never named the claimed key, which an 8.14-era
    /// instance may do. Sending this caller to mint a token would be #142's own defect in a quieter
    /// voice: a rotation spent on a credential that demonstrably works.
    /// </summary>
    [Fact]
    public async Task A_401_whose_lookup_never_named_the_key_still_rules_the_token_out()
    {
        var message = await Refused(Claim("LINK_ISSUES"), HttpStatusCode.Unauthorized);

        message.ShouldContain("neither invalid nor revoked");
        message.ShouldContain("LINK_ISSUES");
        message.ShouldNotContain("auth login");

        // Unknown, not absent: Jira never said the account lacks it.
        message.ShouldNotContain("does not have LINK_ISSUES");
    }

    /// <summary>
    /// The same standing under a 403 changes nothing. Ruling the token out is worth a sentence only
    /// where the token was under suspicion, and on a 403 it never was.
    /// </summary>
    [Fact]
    public async Task A_403_whose_lookup_never_named_the_key_says_exactly_what_it_always_said()
    {
        var message = await Refused(Claim("EDIT_ISSUES"));

        message.ShouldContain("does not have permission for it on");
        message.ShouldNotContain("EDIT_ISSUES");
    }

    [Fact]
    public async Task A_403_whose_lookup_answered_nothing_says_exactly_what_it_always_said()
    {
        var message = await Refused(Unanswerable("EDIT_ISSUES"));

        message.ShouldContain("does not have permission for it on");
        message.ShouldNotContain("EDIT_ISSUES");
    }

    /// <summary>
    /// The seam is entered by every failed call, so what it does when there is nothing to ask about
    /// is what keeps a read costing one round trip rather than two. The counting double is the
    /// proof: it is never asked.
    /// </summary>
    [Theory]
    // A read, which claimed no permission at all.
    [InlineData(HttpStatusCode.Forbidden, true)]
    // A failure that is not a refusal.
    [InlineData(HttpStatusCode.InternalServerError, false)]
    // A 400 this tool did not opt into, which is ordinary field validation far more often than not.
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task A_failure_with_no_permission_to_ask_about_never_reaches_the_lookup(
        HttpStatusCode status,
        bool aRead)
    {
        var asked = 0;
        var claim = new PermissionClaim(
            "EDIT_ISSUES",
            "ABC-1",
            _ =>
            {
                asked++;

                return Task.FromResult<IReadOnlyDictionary<string, bool>>(
                    new Dictionary<string, bool> { ["EDIT_ISSUES"] = false });
            });

        var diagnosis = await PermissionAdvice.DiagnoseAsync(
            aRead ? null : claim,
            new RefusalShape.Refused(status, "/rest/api/2/issue/ABC-1", "updating ABC-1", "work"),
            TestContext.Current.CancellationToken);

        diagnosis.ShouldBeNull();
        asked.ShouldBe(0);
    }

    /// <summary>
    /// The whole answer an agent reads, as the tool assembles it: the seam's prose, the tool's own
    /// state clause, and the status line.
    /// </summary>
    private static async Task<string> Refused(
        PermissionClaim claim,
        HttpStatusCode status = HttpStatusCode.Forbidden)
    {
        var exception = new JiraApiException(
            status,
            "/rest/api/2/issue/ABC-1",
            [],
            new Dictionary<string, string>());

        var diagnosis = await PermissionAdvice.DiagnoseAsync(
            claim,
            new RefusalShape.Refused(status, exception.Endpoint, "updating ABC-1", "work"),
            TestContext.Current.CancellationToken);

        return JiraToolError.Describe(
            exception,
            "work",
            "updating ABC-1",
            advice: "Nothing was changed: ABC-1 is as it was.",
            diagnosis);
    }

    /// <summary>
    /// A claim whose lookup answers with exactly these permissions. Naming none is the 8.14-era
    /// instance whose enumeration does not carry the claimed key. The <c>400</c> opt-in is on,
    /// because the only claims that reach this seam with a <c>400</c> are the two that opted in.
    /// </summary>
    private static PermissionClaim Claim(
        string key,
        params (string Key, bool Held)[] permissions) =>
        new(
            key,
            "ABC-1",
            _ => Task.FromResult<IReadOnlyDictionary<string, bool>>(
                permissions.ToDictionary(row => row.Key, row => row.Held)),
            DiagnoseBadRequest: true);

    /// <summary>
    /// A claim whose lookup cannot be made at all: an 8.14 without the endpoint, a timeout, a
    /// diagnostic that is itself refused.
    /// </summary>
    private static PermissionClaim Unanswerable(string key) =>
        new(
            key,
            "ABC-1",
            _ => throw new HttpRequestException("mypermissions is not answering"),
            DiagnoseBadRequest: true);
}
