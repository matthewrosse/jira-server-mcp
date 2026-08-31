using JiraServerMcp.Grants;
using JiraServerMcp.Jira.Capabilities;
using JiraServerMcp.Profiles;

namespace JiraServerMcp.Tools;

/// <summary>
/// Which tools a client gets: nothing, a named grant, or a Jira Software licence, named once here
/// as a value rather than as control flow scattered through the serve verb. A tool that satisfies
/// nobody is absent from registration, so an agent never discovers it, attempts it, and burns
/// context learning it is forbidden.
/// </summary>
internal static class ToolSurface
{
    /// <summary>
    /// Exposed for the README test, which holds the catalogue to this table rather than to a
    /// second copy of the mapping.
    /// </summary>
    internal static IReadOnlyList<ToolSurfaceEntry> Entries => _entries;

    private static readonly IReadOnlyList<ToolSurfaceEntry> _entries =
    [
        new(typeof(WhoamiTool)),
        new(typeof(SearchTool)),
        new(typeof(GetJqlFieldsTool)),
        new(typeof(ListSavedFiltersTool)),
        new(typeof(MyOpenIssuesTool)),
        new(typeof(ChangedSinceTool)),
        new(typeof(GetIssuesTool)),
        new(typeof(GetAttachmentTool)),
        new(typeof(ListProjectsTool)),
        new(typeof(GetProjectTool)),
        new(typeof(GetCreateFieldsTool)),
        new(typeof(SearchUsersTool)),
        new(typeof(ListBoardsTool), RequiresSoftwareLicence: true),
        new(typeof(ListSprintsTool), RequiresSoftwareLicence: true),
        new(typeof(GetSprintIssuesTool), RequiresSoftwareLicence: true),
        new(typeof(GetBacklogTool), RequiresSoftwareLicence: true),
        new(typeof(CreateIssueTool), RequiredGrant: Grant.IssuesWrite),
        new(typeof(UpdateIssueTool), RequiredGrant: Grant.IssuesWrite),
        new(typeof(GetEditFieldsTool), RequiredGrant: Grant.IssuesWrite),
        new(typeof(TransitionIssueTool), RequiredGrant: Grant.IssuesWrite),
        new(typeof(AddCommentTool), RequiredGrant: Grant.CommentsWrite),
        new(typeof(AddWorklogTool), RequiredGrant: Grant.WorklogsWrite),
        new(typeof(LinkIssuesTool), RequiredGrant: Grant.LinksWrite),
        new(typeof(AddRemoteLinkTool), RequiredGrant: Grant.LinksWrite),
        new(typeof(AddAttachmentTool), RequiredGrant: Grant.AttachmentsWrite),
    ];

    /// <summary>
    /// The tools to register for a grant set and a recorded capability probe. A profile with no
    /// probe at all answers the licence question the same way an unlicensed one does: no.
    /// </summary>
    public static IReadOnlyList<Type> ToolsToRegister(GrantSet grants, JiraCapabilities? capabilities) =>
        [.. _entries
            .Where(entry => entry.IsSatisfiedBy(grants, capabilities))
            .Select(entry => entry.ToolType)];

    /// <summary>
    /// A missing or stale capability probe is not an error — the tools registered are the ones the
    /// profile knows about — but the operator is told, because a Jira that has since been licensed
    /// for Jira Software will otherwise look as though this server cannot see its boards.
    /// </summary>
    public static async Task WarnAboutTheProbeAsync(string profileName, Profile profile)
    {
        var refresh = $"Run 'jira-server-mcp profile refresh {profileName}'.";

        if (profile.Capabilities is not { } capabilities)
        {
            await Console.Error.WriteLineAsync(
                $"Profile '{profileName}' has no capability probe, so the Jira Software tools are "
                + $"not registered. {refresh}");

            return;
        }

        if (capabilities.IsStale(DateTimeOffset.UtcNow))
        {
            await Console.Error.WriteLineAsync(
                $"The capability probe for profile '{profileName}' was taken on "
                + $"{capabilities.ProbedAt:yyyy-MM-dd} and has expired. The tools registered are "
                + $"the ones it recorded. {refresh}");
        }
    }
}

/// <summary>
/// One row of the tool surface table: a tool type paired with what it requires to be registered.
/// </summary>
internal sealed record ToolSurfaceEntry(
    Type ToolType,
    Grant? RequiredGrant = null,
    bool RequiresSoftwareLicence = false)
{
    public bool IsSatisfiedBy(GrantSet grants, JiraCapabilities? capabilities) =>
        RequiredGrant is { } grant ? grants.Allows(grant)
        : RequiresSoftwareLicence ? capabilities is { SoftwareLicensed: true }
        : true;
}
