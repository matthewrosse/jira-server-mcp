using System.ComponentModel;
using JiraServerMcp.Jira;
using JiraServerMcp.Profiles;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tools;

[McpServerToolType]
internal sealed class WhoamiTool(JiraClient jira, ServedProfile profile)
{
    private const string Name = "jira_whoami";

    [McpServerTool(Name = Name, ReadOnly = true, Destructive = false)]
    [Description("The Jira Server account this server is authenticated as.")]
    public Task<CallToolResult> WhoamiAsync(CancellationToken cancellationToken) =>
        ToolCall.RunAsync(
            profile,
            Name,
            whenUnreachable: string.Empty,
            whenTimedOut:
                ", and the request was given up. It may be under load or behind a proxy holding "
                + "the request; try again, and check the profile's base URL if it keeps happening.",
            async () =>
            {
                var user = await jira.GetMyselfAsync(cancellationToken);

                return $"""
                    display name: {user.DisplayName}
                    username: {user.Name}
                    email: {user.EmailAddress ?? "(none)"}
                    active: {user.Active}
                    """;
            },
            cancellationToken);
}
