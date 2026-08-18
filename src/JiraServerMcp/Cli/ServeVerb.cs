using System.Reflection;
using JiraServerMcp.Credentials;
using JiraServerMcp.Grants;
using JiraServerMcp.Profiles;
using JiraServerMcp.Prompts;
using JiraServerMcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace JiraServerMcp.Cli;

internal static class ServeVerb
{
    public static async Task<int> RunAsync(
        string profileName,
        string[] allowed,
        CredentialStoreChoice storeChoice,
        CancellationToken cancellationToken)
    {
        // Before anything else: a grant nobody recognises is a mistake in the launch arguments,
        // and the operator should hear about that rather than about the next problem along.
        var grants = GrantSet.Parse(allowed);

        // ADR-0005: the profile is chosen here, once, and is invisible to every tool.
        if (await ProfileResolution.FindAsync(profileName) is not { } profile)
        {
            return 2;
        }

        // The bundle was there when the profile was added; finding out mid-tool-call that it has
        // moved since is worse than refusing to start.
        if (profile.CaBundlePath is { } caBundlePath && !File.Exists(caBundlePath))
        {
            await Console.Error.WriteLineAsync(
                $"Profile '{profileName}' names a certificate authority bundle at "
                + $"'{caBundlePath}', and there is nothing there. Restore the file, or point the "
                + "profile at the bundle again with 'jira-server-mcp profile add'.");

            return 2;
        }

        var store = await ProfileResolution.SelectStoreAsync(storeChoice, cancellationToken);

        var token = await ProfileToken.ResolveAsync(profileName, store, cancellationToken);

        if (token is not { } held)
        {
            await Console.Error.WriteLineAsync(
                $"No personal access token is stored for profile '{profileName}'. Store one with "
                + $"'jira-server-mcp auth login {profileName}'.");

            return 2;
        }

        var builder = Host.CreateApplicationBuilder();

        // ADR-0002: every log line goes to standard error, whatever its level.
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        var connected = ConnectedProfile.OptionsFor(profile, held.Value);

        builder.Services.AddSingleton(Options.Create(connected));

        builder.Services.AddSingleton(new ServedProfile(profileName));

        // One record for the life of the process, which is exactly what an idempotency key
        // promises and the whole of what it promises.
        builder.Services.AddSingleton<WriteAttempts>();

        // Read from the profile at startup and never re-read: an alias is the operator's
        // declaration, not something Jira can change under the process.
        builder.Services.AddSingleton(FieldAliases.For(profile.FieldAliases));
        builder.Services.AddJiraClient();

        // ADR-0005: grants come only from launch arguments. The capability probe is read from the
        // profile: startup does no network input or output, so a client that times out a slow
        // start does not drop the server, and a laptop off the VPN fails one call rather than
        // failing to start.
        var server = builder.Services
            .AddMcpServer(options => options.ServerInfo = ServerInfo())
            .WithStdioServerTransport();

        // One call per type: the MCP SDK's WithTools(IEnumerable<Type>) mis-registers the tool
        // list when handed more than one type in a single call.
        foreach (var toolType in ToolSurface.ToolsToRegister(grants, profile.Capabilities))
        {
            server.WithTools([toolType]);
        }

        // Same one-call-per-type caution as the tools above: WithPrompts takes a batch, and the
        // tool equivalent is known to mis-register one.
        foreach (var promptType in PromptSurface.PromptsToRegister(grants, profile.Capabilities))
        {
            server.WithPrompts([promptType]);
        }

        await ToolSurface.WarnAboutTheProbeAsync(profileName, profile);

        await builder.Build().RunAsync(cancellationToken);

        return 0;
    }

    private static Implementation ServerInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();

        return new Implementation
        {
            Name = assembly.GetName().Name ?? "jira-server-mcp",
            Version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                          ?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString()
                      ?? "0.0.0",
        };
    }
}
