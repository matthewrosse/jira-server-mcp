using System.Reflection;
using JiraServerMcp.Grants;
using JiraServerMcp.Jira.Capabilities;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tools;

/// <summary>
/// The tool catalogue: every built-in tool this repository ships, each paired with what it
/// requires — nothing, a named grant, or a Jira Software licence — named once here as a value
/// rather than as control flow scattered through the serve verb. A tool that satisfies nobody is
/// absent from registration, so an agent never discovers it, attempts it, and burns context
/// learning it is forbidden.
/// </summary>
/// <remarks>
/// This is not what one run registers: that is <see cref="ToolSurface"/>, which adds a profile's
/// operator-defined queries to what survives this gate. The README, the claimed absences and the
/// prompt gate read the catalogue, because none of them can speak for a deployment's own queries.
/// </remarks>
internal static class ToolCatalogue
{
    /// <summary>
    /// Exposed for the README test, which holds the catalogue to this table rather than to a
    /// second copy of the mapping.
    /// </summary>
    internal static IReadOnlyList<ToolCatalogueEntry> Entries => _entries;

    private static readonly IReadOnlyList<ToolCatalogueEntry> _entries =
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
    /// The name an agent sees for a built-in tool, read from the attribute the MCP SDK itself
    /// reads rather than written a second time beside the type, where it would be free to drift.
    /// A type declaring no tool, or several, has no one name to give a row, and says so here
    /// rather than registering something nobody can name.
    /// </summary>
    public static string NameOf(Type toolType)
    {
        string[] names =
        [
            .. toolType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
                .OfType<string>(),
        ];

        return names.Length == 1
            ? names[0]
            : throw new InvalidOperationException(
                $"{toolType.Name} declares {names.Length} methods carrying an [McpServerTool] "
                + "name. A tool in the catalogue must declare exactly one.");
    }
}

/// <summary>
/// One row of the tool catalogue: a tool type paired with what it requires to be registered.
/// </summary>
internal sealed record ToolCatalogueEntry(
    Type ToolType,
    Grant? RequiredGrant = null,
    bool RequiresSoftwareLicence = false)
{
    public bool IsSatisfiedBy(GrantSet grants, JiraCapabilities? capabilities) =>
        RequiredGrant is { } grant ? grants.Allows(grant)
        : RequiresSoftwareLicence ? capabilities is { SoftwareLicensed: true }
        : true;
}
