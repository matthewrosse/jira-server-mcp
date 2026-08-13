using System.Reflection;
using JiraServerMcp.Jira;
using JiraServerMcp.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace JiraServerMcp.Cli;

internal static class ServeVerb
{
    private const string BaseUrlKey = "JIRA_SERVER_MCP_URL";
    private const string TokenKey = "JIRA_SERVER_MCP_TOKEN";

    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();

        // ADR-0002: every log line goes to standard error, whatever its level.
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services
            .AddOptions<JiraClientOptions>()
            .Configure<IConfiguration>(BindEnvironment)
            .Validate(
                options => options.BaseUrl is not null,
                $"{BaseUrlKey} must hold the absolute base URL of your Jira Server.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.PersonalAccessToken),
                $"{TokenKey} must hold a personal access token.")
            .ValidateOnStart();

        builder.Services.AddJiraClient();

        builder.Services
            .AddMcpServer(options => options.ServerInfo = ServerInfo())
            .WithStdioServerTransport()
            .WithTools<WhoamiTool>();

        await builder.Build().RunAsync(cancellationToken);
    }

    private static void BindEnvironment(JiraClientOptions options, IConfiguration configuration)
    {
        options.BaseUrl = Uri.TryCreate(configuration[BaseUrlKey], UriKind.Absolute, out var baseUrl)
            ? baseUrl
            : null;

        options.PersonalAccessToken = configuration[TokenKey];
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
