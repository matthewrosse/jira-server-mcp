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
    /// Asks Jira, and answers null where it could not be asked. The lookup may not exist on the
    /// 8.14 support floor, may time out, and may itself be refused — and a diagnostic that reports
    /// its own failure teaches nothing about the write while reading like a third failure. The
    /// caller's cancellation is the only budget: a separate timeout for a diagnostic would be
    /// policy nobody asked for.
    /// </summary>
    public static async Task<PermissionAnswer?> AskAsync(
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
            return null;
        }

        // A key Jira did not answer for is not a key the account lacks: an older Jira may not know
        // it at all, and reporting an absence there would invent a permission scheme.
        if (!held.TryGetValue(claim.Key, out var hasClaimed))
        {
            return null;
        }

        return new PermissionAnswer(
            claim.Key,
            claim.Scope,
            hasClaimed,
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
    public static string Sentence(PermissionAnswer answer) =>
        answer.Held
            ? answer.OtherMissing.Count is 0
                ? $"The account does have {answer.Key} on {answer.Scope}, and every other write "
                  + "permission this server can claim there, so a missing permission is not the "
                  + "reason. Jira also answers 403 for an instance in read-only or maintenance "
                  + "mode and for a throttled login."
                : $"The account does have {answer.Key} on {answer.Scope}, so a missing permission "
                  + "is not the reason for this refusal. It does not have "
                  + $"{string.Join(", ", answer.OtherMissing)} there, which is what would refuse a "
                  + "write that claims one of those."
            : $"The account does not have {answer.Key} on {answer.Scope}. That is the Jira "
              + "permission this write claims, so a human with access to the project's permission "
              + "scheme has to grant it before the write can succeed.";
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
/// Jira's answer about one claim. <see cref="OtherMissing"/> is empty where the account lacks the
/// claimed key: that answer is already complete, and listing more would bury it.
/// </summary>
internal sealed record PermissionAnswer(
    string Key,
    string Scope,
    bool Held,
    IReadOnlyList<string> OtherMissing)
{
    /// <summary>
    /// What the structured half carries, and only on the absent branch (ADR-0009). Rule 3 promises
    /// structure on every result rather than a field for every sentence, and a field is added and
    /// never removed — so the narrow field keeps the wider one available, while the wider one could
    /// not be taken back.
    /// </summary>
    public string? Missing => Held ? null : Key;
}
