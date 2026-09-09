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
///
/// One entry point — <see cref="DiagnoseAsync"/> — decides whether to ask, asks at most once, and
/// authors every word a refusal gains. Interpreting what Jira said used to be spread over four
/// modules, two of which wrote permission prose of their own; concentrating it here does not remove
/// a state, and does not claim to (ADR-0013, amended). What it removes is the possibility of a
/// standing being handled in one file and forgotten in another. The next seam, if this file grows,
/// is the lookup against the prose.
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
    public static PermissionClaim OnIssue(
        JiraClient jira,
        string key,
        string issueKey,
        bool diagnoseBadRequest = false) =>
        new(
            key,
            issueKey,
            token => jira.GetMyPermissionsAsync(issueKey, null, token),
            diagnoseBadRequest);

    /// <summary>
    /// What a create claimed. There is no issue yet, so the project is the only scope there is.
    /// </summary>
    public static PermissionClaim OnProject(JiraClient jira, string key, string projectKey) =>
        new(key, $"project {projectKey}", token => jira.GetMyPermissionsAsync(null, projectKey, token));

    /// <summary>
    /// What one refused write is told about the permission it claimed: the finished trusted prose,
    /// and the bare key where and only where Jira confirmed the account lacks it. Null where there
    /// is nothing to say, which leaves the caller the wording it had before any lookup existed.
    /// </summary>
    /// <remarks>
    /// Every decision a permission needs is taken here. Whether to ask at all is read off the
    /// refusal shape and the claim, so a read — which claims nothing — is answered on the first
    /// line and costs no round trip; at most one lookup happens, and no caller sees a standing to
    /// re-interpret.
    /// </remarks>
    public static async Task<PermissionDiagnosis?> DiagnoseAsync(
        PermissionClaim? claim,
        RefusalShape shape,
        CancellationToken cancellationToken)
    {
        if (claim is null || !Asks(claim, shape))
        {
            return null;
        }

        var answer = await AskAsync(claim, cancellationToken);

        return Explain(answer, shape) is { } prose
            ? new PermissionDiagnosis(prose, answer.Missing)
            : null;
    }

    /// <summary>
    /// The sentence a create or an edit adds to its own field-error advice, where a Jira permission
    /// is a possibility the screen can rule in or out. It asserts nothing, because no lookup backs
    /// it: those two tools deliberately ask nothing after a <c>400</c> (ADR-0013, amended), and the
    /// decision to say this at all stays with them.
    /// </summary>
    /// <param name="key">The Jira permission the write claims.</param>
    /// <param name="screenTool">The tool that publishes the screen this issue type has.</param>
    public static string Possibility(string key, string screenTool) =>
        " Jira Server can return the same field-shaped refusal when the account lacks "
        + $"{key}. If {screenTool} says a refused field should be writable, investigate that Jira "
        + "permission with whoever administers the project.";

    /// <summary>
    /// Whether this shape of refusal is worth one lookup. A <c>403</c> and a <c>401</c> always are;
    /// a <c>400</c> only where the tool opted in, because a bad request is mostly ordinary field
    /// validation; and an empty published vocabulary always is, since discovery already succeeded.
    /// </summary>
    private static bool Asks(PermissionClaim claim, RefusalShape shape) =>
        shape switch
        {
            RefusalShape.Refused
            { Status: HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized } => true,

            RefusalShape.Refused { Status: HttpStatusCode.BadRequest } => claim.DiagnoseBadRequest,

            RefusalShape.Refused => false,

            _ => true,
        };

    /// <summary>
    /// What the answer means for this shape of refusal, or null where it means nothing the caller
    /// can print. A Jira refusal and an empty published vocabulary share this lookup and share no
    /// wording at all: the second has no HTTP status, omits the other permissions the account
    /// lacks, and words three of its four branches differently.
    /// </summary>
    private static string? Explain(PermissionAnswer answer, RefusalShape shape) =>
        shape switch
        {
            RefusalShape.Refused refused => AfterRefusal(answer, refused),

            RefusalShape.NoPublishedVocabulary vocabulary =>
                WithoutVocabulary(answer, vocabulary.Opening),

            _ => null,
        };

    /// <summary>
    /// Asks Jira, and answers a standing of <see cref="PermissionStanding.Unanswered"/> where it
    /// could not be asked. The lookup may not exist on the 8.14 support floor, may time out, and may itself be
    /// refused — and a diagnostic that reports its own failure teaches nothing about the write while
    /// reading like a third failure. The caller's cancellation is the only budget: a separate
    /// timeout for a diagnostic would be policy nobody asked for.
    /// </summary>
    /// <remarks>
    /// An answer is always returned, because a claim was always made by the time this is called.
    /// A read, which claimed nothing, never reaches here at all — which is what lets every standing
    /// mean "a write, and this is what Jira said" rather than one of them having to double as "no
    /// question was asked".
    ///
    /// The lookup travels on the same personal access token as the write, which is why it is its own
    /// discriminator: a token Jira has revoked cannot read <c>mypermissions</c> either, so an answer
    /// arriving at all proves the credential is live (ADR-0013, amended).
    /// </remarks>
    private static async Task<PermissionAnswer> AskAsync(
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
    /// The whole opening paragraph a refused write gains, or null where the lookup resolved nothing
    /// and the failure keeps the wording it had before.
    /// </summary>
    /// <remarks>
    /// Two openings, on purpose. Where Jira named the key, the opening stops asserting a missing
    /// permission, because the next line says whether there is one — and on the branch where the
    /// account holds what it claimed, the older opening would contradict it. Where it named
    /// nothing, a <c>401</c> still has something to say, because the token was under suspicion and
    /// only this lookup can lift it; a <c>403</c> and a <c>400</c> have not, and answer null so the
    /// formatter keeps its own status wording.
    /// </remarks>
    private static string? AfterRefusal(PermissionAnswer answer, RefusalShape.Refused refusal) =>
        Sentence(answer, refusal.Status) is { } why
            ? Opening(refusal) + "\n" + why
            : refusal.Status is HttpStatusCode.Unauthorized
                ? Unresolved(answer, refusal)
                : null;

    private static string Opening(RefusalShape.Refused refusal) =>
        $"Jira refused {refusal.Operation} on {refusal.Endpoint}. The request was not retried, "
        + "and repeating it will not help.";

    /// <summary>
    /// A <c>401</c> where the account does not demonstrably hold the key it claimed. Two bespoke
    /// paragraphs, because the two states differ in the one thing a <c>401</c> most needs to know:
    /// Jira answering at all proves the token is live, and a lookup nobody could make proves
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Where Jira answered but never named the key, sending this caller to mint a token would be
    /// the same wrong advice #142 removed, in a quieter voice. Where it could not be asked, the
    /// login command stays — it is still the likelier cause and still the right first move — and
    /// stops being asserted as the only one.
    /// </remarks>
    private static string Unresolved(PermissionAnswer answer, RefusalShape.Refused refusal) =>
        answer.Answered
            ? Opening(refusal)
              + " The account's permissions were readable with this same token, so it is neither "
              + $"invalid nor revoked, and a new one will not change this. This Jira did not report {answer.Key}, "
              + "which is the permission this write claims, so take that key to whoever "
              + "administers the project."
            : $"The personal access token for profile '{refusal.ProfileName}' may be invalid or "
              + $"revoked — run 'jira-server-mcp auth login {refusal.ProfileName}' to store a new "
              + "one. Jira also answers 401 when it refuses a write for a missing Jira "
              + "permission, so if a new token fails the same way, check the account's "
              + "permissions on this project rather than the token.";

    /// <summary>
    /// The one sentence a refusal gains. It says which permission the write claimed and whether the
    /// account has it, and — where it does — which other write permissions it lacks in the same
    /// scope, because that is what turns a create refused for <c>ASSIGN_ISSUES</c> from a dead end
    /// into an answer.
    /// </summary>
    /// <remarks>
    /// Null where Jira said nothing about the claimed key, which is what keeps a standing with no
    /// sentence from silently borrowing another standing's opening.
    /// </remarks>
    private static string? Sentence(PermissionAnswer answer, HttpStatusCode status) =>
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

        return status is HttpStatusCode.Forbidden && answer.OtherMissing.Count is 0
            ? opening
              + " Jira also answers 403 for an instance in read-only or maintenance mode and for a "
              + "throttled login."
            : opening;
    }

    /// <summary>
    /// An empty published vocabulary is the only local transition refusal that asks about a Jira
    /// permission, and it shares no wording with the HTTP shapes. Where Jira did not answer the
    /// claim, the two genuine causes stay ambiguous; where it did, only the claimed permission is
    /// discussed and unrelated missing permissions are deliberately omitted.
    /// </summary>
    private static string WithoutVocabulary(PermissionAnswer answer, string opening) =>
        answer.Standing switch
        {
            PermissionStanding.Absent =>
                opening + $" The account does not have {answer.Key} on {answer.Scope}. "
                + "That is the Jira permission this write claims, so a human with access to the "
                + "project's permission scheme has to grant it before the issue can be moved.",

            PermissionStanding.Held =>
                opening + $" The account does have {answer.Key} on {answer.Scope}, so "
                + "the issue's workflow or status conditions, rather than that permission, leave "
                + "no transition available.",

            _ =>
                opening + " Jira Server does not distinguish here between workflow or status "
                + $"conditions and an account that lacks {answer.Key}, so investigate both.",
        };

    /// <summary>
    /// What Jira said about the key one write claimed. <see cref="OtherMissing"/> is empty for every
    /// standing but <see cref="PermissionStanding.Held"/>: an account that lacks what it claimed has
    /// a complete answer already, and listing more would bury it.
    /// </summary>
    /// <remarks>
    /// Private to the seam. Every consumer used to re-read it and decide for itself what a standing
    /// meant, which is what made a wording change a ten-file change; what leaves this file now is a
    /// <see cref="PermissionDiagnosis"/> with nothing left to interpret.
    /// </remarks>
    private sealed record PermissionAnswer(
        string Key,
        string Scope,
        PermissionStanding Standing,
        IReadOnlyList<string> OtherMissing)
    {
        /// <summary>
        /// Whether Jira answered the lookup at all — which is to say whether the personal access
        /// token it travelled on is one Jira accepts. This, and not the standing, is what tells a
        /// refused write apart from a revoked credential on a <c>401</c>: a Jira that answered
        /// without naming the claimed key has still proved the token is live.
        /// </summary>
        public bool Answered => Standing is not PermissionStanding.Unanswered;

        /// <summary>
        /// What the structured half carries, and only on the absent branch (ADR-0009). Rule 3
        /// promises structure on every result rather than a field for every sentence, and a field is
        /// added and never removed — so the narrow field keeps the wider one available, while the
        /// wider one could not be taken back. A key Jira never named is not a key the account is
        /// missing.
        /// </summary>
        public string? Missing => Standing is PermissionStanding.Absent ? Key : null;
    }

    /// <summary>
    /// What became of one claim's lookup. Four states rather than a nullable flag, because two of
    /// them mean "nothing is known about the key" for opposite reasons, and a <c>401</c> has to tell
    /// those two apart: one proves the token is live and the other proves nothing at all.
    /// </summary>
    private enum PermissionStanding
    {
        /// <summary>
        /// Jira could not be asked — the endpoint may not exist on the 8.14 support floor, may have
        /// timed out, and may itself have been refused. Nothing follows from this, the token
        /// included.
        /// </summary>
        Unanswered,

        /// <summary>
        /// Jira answered and its enumeration did not name the claimed key. Whether the account holds
        /// it is unknown, but the answer arrived, so the token is not the problem.
        /// </summary>
        Unlisted,

        Held,

        Absent,
    }
}

/// <summary>
/// What a write claimed and where: the permission key, the scope as the message names it, and the
/// lookup itself. The delegate is closed over by the tool rather than reached for by
/// <see cref="Tools.ToolCall"/>, which has never held a <see cref="JiraClient"/> — giving it one
/// would put an unused parameter at every read tool's call site.
/// </summary>
/// <remarks>
/// <see cref="DiagnoseBadRequest"/> stays here rather than on the refusal shape: which tool opted
/// its <c>400</c> in is a fact about the tool, not about the way Jira answered.
/// </remarks>
internal sealed record PermissionClaim(
    string Key,
    string Scope,
    Func<CancellationToken, Task<IReadOnlyDictionary<string, bool>>> LookUp,
    bool DiagnoseBadRequest = false);

/// <summary>
/// The closed set of ways a write can be refused such that a Jira permission is worth asking about.
/// It decides whether a lookup happens and which tail the answer gets, and never what the account
/// holds.
/// </summary>
internal abstract record RefusalShape
{
    /// <summary>
    /// Jira refused the write itself: a <c>403</c>, a <c>401</c>, or a <c>400</c> the tool opted
    /// into. What the seam needs to author the opening paragraph travels with it, because a
    /// refusal names one operation on one endpoint for one profile.
    /// </summary>
    internal sealed record Refused(
        HttpStatusCode Status,
        string Endpoint,
        string Operation,
        string ProfileName) : RefusalShape;

    /// <summary>
    /// Transition discovery succeeded and published nothing this account can use. Not an HTTP
    /// failure at all, so it carries no status — and the refusal is local, which is why the tool's
    /// own opening comes in and the finished sentence goes back out.
    /// </summary>
    internal sealed record NoPublishedVocabulary(string Opening) : RefusalShape;
}

/// <summary>
/// The finished explanation of one refused write: this server's trusted prose, and — only where
/// Jira confirmed the account lacks the key it claimed — the bare key that reaches structured
/// content. Produced once, from one place, and never re-derived by a consumer.
/// </summary>
internal sealed record PermissionDiagnosis(string Prose, string? Missing);
