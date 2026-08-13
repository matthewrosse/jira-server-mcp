using System.ComponentModel;
using JiraServerMcp.Jira;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tools;

[McpServerToolType]
internal sealed class WhoamiTool(JiraClient jira)
{
    [McpServerTool(Name = "jira_whoami", ReadOnly = true)]
    [Description("The Jira Server account this server is authenticated as.")]
    public async Task<string> WhoamiAsync(CancellationToken cancellationToken)
    {
        var user = await jira.GetMyselfAsync(cancellationToken);

        return $"""
            display name: {user.DisplayName}
            username: {user.Name}
            email: {user.EmailAddress ?? "(none)"}
            active: {user.Active}
            """;
    }
}
