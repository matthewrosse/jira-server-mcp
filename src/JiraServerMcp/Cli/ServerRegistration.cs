using JiraServerMcp.Grants;
using JiraServerMcp.Profiles;
using JiraServerMcp.Prompts;
using JiraServerMcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Cli;

/// <summary>
/// What one run puts on the MCP server: the <see cref="ToolSurface"/> for this grant set and
/// profile, and the prompts whose tools all survived it. The only place that knows how a row
/// reaches the SDK, so the serve verb states no registration rule of its own.
/// </summary>
/// <remarks>
/// Here rather than on <see cref="ToolSurface"/>, because prompts are registered too and
/// <see cref="PromptSurface"/> already reads the tool catalogue: prompt registration inside
/// <c>Tools</c> would make that dependency mutual. It also keeps the surface a value with no server
/// builder in its signature.
/// </remarks>
internal static class ServerRegistration
{
    public static async Task RegisterAsync(
        IMcpServerBuilder server,
        GrantSet grants,
        Profile profile,
        string profileName)
    {
        // One registration per tool and per prompt: the MCP SDK's WithTools(IEnumerable<Type>)
        // mis-registers the tool list when handed more than one type in a single call, and
        // WithPrompts takes a batch the same way.
        foreach (var row in ToolSurface.For(grants, profile).Rows)
        {
            switch (row)
            {
                case ToolSurfaceRow.BuiltInRow builtIn:
                    server.WithTools([builtIn.ToolType]);
                    break;

                // What WithTools does with an instance, except that the factory is handed the
                // container Build() produces — which is what the query's tool resolves its client
                // from, and which does not exist yet while tools are being registered.
                case ToolSurfaceRow.QueryRow query:
                    server.Services.AddSingleton<McpServerTool>(query.Build);
                    break;

                default:
                    throw new InvalidOperationException($"No registration for {row}.");
            }
        }

        foreach (var promptType in PromptSurface.PromptsToRegister(grants, profile.Capabilities))
        {
            server.WithPrompts([promptType]);
        }

        await WarnAboutTheProbeAsync(profileName, profile);
    }

    /// <summary>
    /// A missing or stale capability probe is not an error — the tools registered are the ones the
    /// profile knows about — but the operator is told, because a Jira that has since been licensed
    /// for Jira Software will otherwise look as though this server cannot see its boards.
    /// </summary>
    private static async Task WarnAboutTheProbeAsync(string profileName, Profile profile)
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
