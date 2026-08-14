using System.ComponentModel;
using JiraServerMcp.Errors;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Profiles;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tools;

[McpServerToolType]
internal sealed class WhoamiTool(JiraClient jira, ServedProfile profile)
{
    private const string Name = "jira_whoami";

    [McpServerTool(Name = Name, ReadOnly = true)]
    [Description("The Jira Server account this server is authenticated as.")]
    public async Task<CallToolResult> WhoamiAsync(CancellationToken cancellationToken)
    {
        // The agent cannot read this server's log, so a failure has to say which failure it was
        // in the result itself, or an expired token looks the same as a wrong base URL.
        try
        {
            var user = await jira.GetMyselfAsync(cancellationToken);

            return Text($"""
                display name: {user.DisplayName}
                username: {user.Name}
                email: {user.EmailAddress ?? "(none)"}
                active: {user.Active}
                """);
        }
        catch (JiraApiException exception)
        {
            return Error(JiraToolError.Describe(exception, profile.Name, Name));
        }
        catch (HttpRequestException exception)
        {
            return Error($"Could not reach Jira: {exception.Message}");
        }
    }

    private static CallToolResult Text(string text) =>
        new() { Content = [new TextContentBlock { Text = text }] };

    private static CallToolResult Error(string text) =>
        new() { Content = [new TextContentBlock { Text = text }], IsError = true };
}
