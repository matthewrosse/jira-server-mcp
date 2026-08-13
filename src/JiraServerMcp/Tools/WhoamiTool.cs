using System.ComponentModel;
using System.Net;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Errors;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace JiraServerMcp.Tools;

[McpServerToolType]
internal sealed class WhoamiTool(JiraClient jira)
{
    [McpServerTool(Name = "jira_whoami", ReadOnly = true)]
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
            return Error(Explain(exception));
        }
        catch (HttpRequestException exception)
        {
            return Error($"Could not reach Jira: {exception.Message}");
        }
    }

    private static string Explain(JiraApiException exception) => exception.StatusCode switch
    {
        HttpStatusCode.Unauthorized =>
            $"Jira rejected the personal access token (401). It may be expired or revoked. {exception.Message}",

        HttpStatusCode.Forbidden =>
            $"Jira refused the request (403). The personal access token is valid but not permitted here. {exception.Message}",

        HttpStatusCode.NotFound =>
            "Jira has no /rest/api/2/myself at the configured base URL (404). Check the base URL, "
            + "including any context path such as /jira.",

        _ => exception.Message,
    };

    private static CallToolResult Text(string text) =>
        new() { Content = [new TextContentBlock { Text = text }] };

    private static CallToolResult Error(string text) =>
        new() { Content = [new TextContentBlock { Text = text }], IsError = true };
}
