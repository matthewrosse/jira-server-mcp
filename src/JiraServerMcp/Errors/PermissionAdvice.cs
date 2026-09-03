using System.Net;
using JiraServerMcp.Jira;

namespace JiraServerMcp.Errors;

/// <summary>
/// Jira's half of "may this write happen", asked only once Jira has already refused one (ADR-0013).
///
/// A <c>grant</c> answers whether this client may attempt a write at all, locally, before a tool is
/// registered. This answers whether the account the server authenticates as holds the Jira
/// permission the write claimed — and it answers it as an explanation, never as a prediction. There
/// is no tool here and nothing an agent may call early: <c>/rest/api/2/mypermissions</c> is read on
/// the failure path and nowhere else, because a green answer would not mean the next write will
/// succeed.
/// </summary>
/// <remarks>
/// Permissions are per project and per issue and are never cached, so nothing here is a capability
/// probe and nothing is recorded on the profile.
/// </remarks>
internal static class PermissionAdvice
{
    /// <summary>
    /// Jira's own permission keys, written bare. Not the display name: an administrator can rename
    /// that, which makes it untrusted content and would drag a second envelope into a message that
    /// already carries one. The bare key is also the string a human searches the permission-scheme
    /// screen for.
    /// </summary>
    public const string CreateIssues = "CREATE_ISSUES";

    public const string EditIssues = "EDIT_ISSUES";

    public const string TransitionIssues = "TRANSITION_ISSUES";

    public const string AddComments = "ADD_COMMENTS";

    public const string WorkOnIssues = "WORK_ON_ISSUES";

    public const string CreateAttachments = "CREATE_ATTACHMENTS";

    public const string LinkIssues = "LINK_ISSUES";

    /// <summary>
    /// Claimed by no tool on its own. It is here because a create or an edit carrying an assignee
    /// is refused for it while the issue permission it claimed is held, which is exactly the
    /// refusal the second half of <see cref="Sentence"/> exists to explain.
    /// </summary>
    public const string AssignIssues = "ASSIGN_ISSUES";

    /// <summary>
    /// Every permission a write in this server can be refused for. One response carries all of
    /// them, so naming the others the account lacks in the same scope costs nothing beyond the
    /// round trip already spent.
    /// </summary>
    private static readonly string[] _writeKeys =
    [
        CreateIssues,
        EditIssues,
        TransitionIssues,
        AddComments,
        WorkOnIssues,
        CreateAttachments,
        LinkIssues,
        AssignIssues,
    ];

    /// <summary>
    /// What one write claimed, evaluated against the issue it names. Issue-scoped rather than
    /// project-scoped wherever a key exists: a scheme may grant Edit Issues to the current assignee
    /// or reporter, and only an issue-scoped evaluation honours that.
    /// </summary>
    public static PermissionClaim OnIssue(JiraClient jira, string key, string issueKey) =>
        new(key, issueKey, token => jira.GetMyPermissionsAsync(issueKey, null, token));

    /// <summary>
    /// What a create claimed. There is no issue yet, so the project is the only scope there is.
    /// </summary>
    public static PermissionClaim OnProject(JiraClient jira, string key, string projectKey) =>
        new(key, $"project {projectKey}", token => jira.GetMyPermissionsAsync(null, projectKey, token));

    /// <summary>
    /// Asks Jira, and answers a standing of <see cref="PermissionStanding.Unanswered"/> where it
    /// could not be asked. The lookup may not exist on the 8.14 support floor, may time out, and may itself be
    /// refused — and a diagnostic that reports its own failure teaches nothing about the write while
    /// reading like a third failure. The caller's cancellation is the only budget: a separate
    /// timeout for a diagnostic would be policy nobody asked for.
    /// </summary>
    /// <remarks>
    /// An answer is always returned, because a claim was always made by the time this is called —
    /// which is what lets a null <see cref="PermissionAnswer"/> mean "a read, claiming nothing"
    /// while an unanswered one means "a write, and this server could not find out". Under a
    /// <c>401</c> those two want different sentences, and under a <c>403</c> the unanswered one
    /// wants the sentence this server has always used.
    ///
    /// The lookup travels on the same personal access token as the write, which is why it is its own
    /// discriminator: a token Jira has revoked cannot read <c>mypermissions</c> either, so an answer
    /// arriving at all proves the credential is live (ADR-0013, amended).
    /// </remarks>
    public static async Task<PermissionAnswer> AskAsync(
        PermissionClaim claim,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, bool> held;

        try
        {
            held = await claim.LookUp(cancellationToken);
        }
        // Anything at all, except the caller hanging up: there is nobody waiting for an answer to
        // that, and a diagnostic must never become the failure it was asked to explain.
        catch (Exception exception) when (
            exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new PermissionAnswer(claim.Key, claim.Scope, PermissionStanding.Unanswered, []);
        }

        // A key Jira did not answer for is not a key the account lacks: an older Jira may not know
        // it at all, and reporting an absence there would invent a permission scheme. It is kept
        // apart from the unanswered case even so, because Jira did answer — and under a 401 that
        // answer is the whole proof that the token is live.
        if (!held.TryGetValue(claim.Key, out var hasClaimed))
        {
            return new PermissionAnswer(claim.Key, claim.Scope, PermissionStanding.Unlisted, []);
        }

        return new PermissionAnswer(
            claim.Key,
            claim.Scope,
            hasClaimed ? PermissionStanding.Held : PermissionStanding.Absent,
            hasClaimed
                ? [.. _writeKeys.Where(key => key != claim.Key
                                              && held.TryGetValue(key, out var has)
                                              && !has)]
                : []);
    }

    /// <summary>
    /// The one sentence a refusal gains. It says which permission the write claimed and whether the
    /// account has it, and — where it does — which other write permissions it lacks in the same
    /// scope, because that is what turns a create refused for <c>ASSIGN_ISSUES</c> from a dead end
    /// into an answer.
    /// </summary>
    /// <remarks>
    /// Null where Jira said nothing about the claimed key, which is the caller's signal to keep the
    /// wording it had before the lookup existed. Returning it rather than leaving the caller to
    /// guard is what keeps a state with no sentence from silently borrowing another state's.
    /// </remarks>
    public static string? Sentence(PermissionAnswer answer, HttpStatusCode status) =>
        answer.Standing switch
        {
            PermissionStanding.Held => Held(answer, status),

            PermissionStanding.Absent =>
                $"The account does not have {answer.Key} on {answer.Scope}. That is the Jira "
                + "permission this write claims, so a human with access to the project's "
                + "permission scheme has to grant it before the write can succeed.",

            _ => null,
        };

    /// <summary>
    /// The branch where the account holds what it claimed, so the refusal is something else — and
    /// what that something else can be depends on the status.
    /// </summary>
    /// <remarks>
    /// The <c>403</c> tail names causes that belong to <c>403</c> alone, so writing it under a
    /// <c>401</c> would state a falsehood in place of the one this change removes. The <c>401</c>
    /// tail is the useful half of the same thought and hangs off both branches rather than only the
    /// one, because ruling the token out is the whole reason a <c>401</c> reaches here at all.
    /// </remarks>
    private static string Held(PermissionAnswer answer, HttpStatusCode status)
    {
        var opening = answer.OtherMissing.Count is 0
            ? $"The account does have {answer.Key} on {answer.Scope}, and every other write "
              + "permission this server can claim there, so a missing permission is not the reason."
            : $"The account does have {answer.Key} on {answer.Scope}, so a missing permission "
              + "is not the reason for this refusal. It does not have "
              + $"{string.Join(", ", answer.OtherMissing)} there, which is what would refuse a "
              + "write that claims one of those.";

        if (status is HttpStatusCode.Unauthorized)
        {
            return opening
                   + " The lookup that answered was made with this same token, so the token is "
                   + "neither invalid nor revoked.";
        }

        return answer.OtherMissing.Count is 0
            ? opening
              + " Jira also answers 403 for an instance in read-only or maintenance mode and for a "
              + "throttled login."
            : opening;
    }
}

/// <summary>
/// What a write claimed and where: the permission key, the scope as the message names it, and the
/// lookup itself. The delegate is closed over by the tool rather than reached for by
/// <see cref="Tools.ToolCall"/>, which has never held a <see cref="JiraClient"/> — giving it one
/// would put an unused parameter at every read tool's call site.
/// </summary>
internal sealed record PermissionClaim(
    string Key,
    string Scope,
    Func<CancellationToken, Task<IReadOnlyDictionary<string, bool>>> LookUp);

/// <summary>
/// What Jira said about the key one write claimed. <see cref="OtherMissing"/> is empty for every
/// standing but <see cref="PermissionStanding.Held"/>: an account that lacks what it claimed has a
/// complete answer already, and listing more would bury it.
/// </summary>
/// <remarks>
/// A null <see cref="PermissionAnswer"/> is a fifth thing and means a read, which claimed no
/// permission at all. Under a <c>401</c> that read keeps the credential sentence, while every
/// standing here says something a write's <c>401</c> needs and a read's does not.
/// </remarks>
internal sealed record PermissionAnswer(
    string Key,
    string Scope,
    PermissionStanding Standing,
    IReadOnlyList<string> OtherMissing)
{
    /// <summary>
    /// Whether Jira answered the lookup at all — which is to say whether the personal access token
    /// it travelled on is one Jira accepts. This, and not the standing, is what tells a refused
    /// write apart from a revoked credential on a <c>401</c>: a Jira that answered without naming
    /// the claimed key has still proved the token is live.
    /// </summary>
    public bool Answered => Standing is not PermissionStanding.Unanswered;

    /// <summary>
    /// What the structured half carries, and only on the absent branch (ADR-0009). Rule 3 promises
    /// structure on every result rather than a field for every sentence, and a field is added and
    /// never removed — so the narrow field keeps the wider one available, while the wider one could
    /// not be taken back. A key Jira never named is not a key the account is missing.
    /// </summary>
    public string? Missing => Standing is PermissionStanding.Absent ? Key : null;
}

/// <summary>
/// What became of one claim's lookup. Four states rather than a nullable flag, because two of them
/// mean "nothing is known about the key" for opposite reasons, and a <c>401</c> has to tell those
/// two apart: one proves the token is live and the other proves nothing at all.
/// </summary>
internal enum PermissionStanding
{
    /// <summary>
    /// Jira could not be asked — the endpoint may not exist on the 8.14 support floor, may have
    /// timed out, and may itself have been refused. Nothing follows from this, the token included.
    /// </summary>
    Unanswered,

    /// <summary>
    /// Jira answered and its enumeration did not name the claimed key. Whether the account holds it
    /// is unknown, but the answer arrived, so the token is not the problem.
    /// </summary>
    Unlisted,

    Held,

    Absent,
}
