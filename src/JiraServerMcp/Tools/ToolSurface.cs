using JiraServerMcp.Grants;
using JiraServerMcp.Profiles;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tools;

/// <summary>
/// The tool surface: exactly the tools one run registers — the <see cref="ToolCatalogue"/> after
/// its grant-and-licence gate, then this profile's operator-defined queries. One ordered list, so
/// startup registers what this value says and a test can hold <c>tools/list</c> to it.
/// </summary>
/// <remarks>
/// A pure value. How a row reaches the MCP SDK is the serve verb's registration concern, not this
/// module's, and nothing here names a server builder. The profile is taken whole because it
/// carries both halves: the capability probe the catalogue's gate reads, and the declared queries.
/// </remarks>
internal sealed class ToolSurface
{
    private ToolSurface(IReadOnlyList<ToolSurfaceRow> rows) => Rows = rows;

    /// <summary>Every tool this run registers, in registration order.</summary>
    public IReadOnlyList<ToolSurfaceRow> Rows { get; }

    /// <summary>The name an agent sees for each row, in the same order.</summary>
    public IReadOnlyList<string> Names => [.. Rows.Select(row => row.Name)];

    public static ToolSurface For(GrantSet grants, Profile profile) =>
        new(
        [
            .. ToolCatalogue.ToolsToRegister(grants, profile.Capabilities)
                .Select(toolType => new ToolSurfaceRow.BuiltInRow(toolType)),
            .. ProfileQuerySurface.RowsFor(profile),
        ]);
}

/// <summary>
/// One tool on the surface. The two kinds reach the SDK differently — a built-in tool is a type
/// the SDK builds, an operator-defined query is a tool built from a delegate — and this is where
/// that difference is stated, rather than at every place that registers or names a tool.
/// </summary>
internal abstract record ToolSurfaceRow
{
    /// <summary>Closes the set: a tool is one of the two kinds below.</summary>
    private ToolSurfaceRow(string name) => Name = name;

    public string Name { get; }

    /// <summary>A tool this repository ships, named from the attribute its type declares.</summary>
    internal sealed record BuiltInRow(Type ToolType)
        : ToolSurfaceRow(ToolCatalogue.NameOf(ToolType));

    /// <summary>
    /// A profile's operator-defined query. Its tool is built from the host's container rather than
    /// ahead of it, because the client it runs against only exists once there is a container.
    /// </summary>
    internal sealed record QueryRow(string Name, Func<IServiceProvider, McpServerTool> Build)
        : ToolSurfaceRow(Name);
}
