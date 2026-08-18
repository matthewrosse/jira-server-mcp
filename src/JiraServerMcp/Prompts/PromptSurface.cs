using JiraServerMcp.Grants;
using JiraServerMcp.Jira.Capabilities;
using JiraServerMcp.Tools;

namespace JiraServerMcp.Prompts;

/// <summary>
/// Which workflow prompts a client gets. A prompt whose procedure calls a tool the client was not
/// granted would read as an instruction to do something impossible, so a prompt is registered only
/// where every tool it names is registered.
/// </summary>
/// <remarks>
/// The gate is derived from <see cref="ToolSurface"/> rather than declared again here. Giving a
/// prompt row its own <c>RequiredGrant</c> would duplicate the tools' gate and let the two drift:
/// a tool moved to another grant would silently leave its prompt registered against a client that
/// can no longer follow it.
/// </remarks>
internal static class PromptSurface
{
    /// <summary>
    /// Exposed for the README test, which holds the documented list to this table rather than to a
    /// second copy of it.
    /// </summary>
    internal static IReadOnlyList<PromptSurfaceEntry> Entries => _entries;

    private static readonly IReadOnlyList<PromptSurfaceEntry> _entries =
    [
        new(
            typeof(ImplementIssuePrompt),
            [typeof(GetIssuesTool), typeof(TransitionIssueTool), typeof(AddCommentTool)]),
    ];

    /// <summary>
    /// The prompts to register for a grant set and a recorded capability probe: the ones every
    /// tool of which survived the tool surface's own gate.
    /// </summary>
    public static IReadOnlyList<Type> PromptsToRegister(
        GrantSet grants,
        JiraCapabilities? capabilities)
    {
        var registered = ToolSurface.ToolsToRegister(grants, capabilities).ToHashSet();

        return
        [
            .. _entries
                .Where(entry => entry.RequiredTools.All(registered.Contains))
                .Select(entry => entry.PromptType),
        ];
    }
}

/// <summary>
/// One row of the prompt surface: a prompt type, and the tool types its procedure tells the agent
/// to call.
/// </summary>
internal sealed record PromptSurfaceEntry(Type PromptType, IReadOnlyList<Type> RequiredTools);
