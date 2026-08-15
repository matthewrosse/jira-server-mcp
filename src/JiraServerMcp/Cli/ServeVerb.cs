using System.Reflection;
using JiraServerMcp.Credentials;
using JiraServerMcp.Jira;
using JiraServerMcp.Profiles;
using JiraServerMcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace JiraServerMcp.Cli;

internal static class ServeVerb
{
    public static async Task<int> RunAsync(
        string profileName,
        CredentialStoreChoice storeChoice,
        CancellationToken cancellationToken)
    {
        // ADR-0005: the profile is chosen here, once, and is invisible to every tool.
        var profile = ProfileStore.InConfigurationDirectory().Find(profileName);

        if (profile is null)
        {
            await Console.Error.WriteLineAsync(
                $"There is no profile named '{profileName}'. Add it with "
                + $"'jira-server-mcp profile add {profileName} --url <url>'.");

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

        var store = await CredentialStoreSelector.ForThisMachine()
            .SelectAsync(storeChoice, Console.Error, cancellationToken);

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

        builder.Services.Configure<JiraClientOptions>(options =>
        {
            options.BaseUrl = profile.BaseUrl;
            options.PersonalAccessToken = held.Value;
            options.CaBundlePath = profile.CaBundlePath;
        });

        builder.Services.AddSingleton(new ServedProfile(profileName));
        builder.Services.AddJiraClient();

        builder.Services
            .AddMcpServer(options => options.ServerInfo = ServerInfo())
            .WithStdioServerTransport()
            .WithTools<WhoamiTool>();

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
