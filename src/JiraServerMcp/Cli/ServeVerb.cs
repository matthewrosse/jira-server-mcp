using System.Reflection;
using JiraServerMcp.Credentials;
using JiraServerMcp.Grants;
using JiraServerMcp.Profiles;
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

        var connected = ConnectedProfile.OptionsFor(profile, held.Value);

        builder.Services.AddSingleton(Options.Create(connected));

        builder.Services.AddSingleton(new ServedProfile(profileName));
        builder.Services.AddJiraClient();

        var server = builder.Services
            .AddMcpServer(options => options.ServerInfo = ServerInfo())
            .WithStdioServerTransport()
            .WithTools<WhoamiTool>()
            .WithTools<SearchTool>()
            .WithTools<GetIssueTool>()
            .WithTools<ListProjectsTool>()
            .WithTools<GetProjectTool>()
            .WithTools<GetCreateFieldsTool>()
            .WithTools<SearchUsersTool>();

        // The capability probe decides this, and it is read from the profile: startup does no
        // network input or output, so a client that times out a slow start does not drop the
        // server, and a laptop off the VPN fails one call rather than failing to start.
        if (profile.Capabilities is { SoftwareLicensed: true })
        {
            server
                .WithTools<ListBoardsTool>()
                .WithTools<ListSprintsTool>()
                .WithTools<GetSprintIssuesTool>()
                .WithTools<GetBacklogTool>();
        }

        await SayWhatTheProbeLeavesUnansweredAsync(profileName, profile);

        // Without its grant a write tool is not registered, so the model never discovers it,
        // attempts it, and burns context learning that it is forbidden.
        if (grants.Allows(Grant.IssuesWrite))
        {
            server
                .WithTools<CreateIssueTool>()
                .WithTools<UpdateIssueTool>()
                .WithTools<TransitionIssueTool>();
        }

        // Each grant stands on its own: an agent allowed to comment gets neither of the others.
        if (grants.Allows(Grant.CommentsWrite))
        {
            server.WithTools<AddCommentTool>();
        }

        if (grants.Allows(Grant.WorklogsWrite))
        {
            server.WithTools<AddWorklogTool>();
        }

        await builder.Build().RunAsync(cancellationToken);

        return 0;
    }

    /// <summary>
    /// A missing or stale capability probe is not an error — the tools registered are the ones the
    /// profile knows about — but the operator is told, because a Jira that has since been licensed
    /// for Jira Software will otherwise look as though this server cannot see its boards.
    /// </summary>
    private static async Task SayWhatTheProbeLeavesUnansweredAsync(
        string profileName,
        Profile profile)
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
