using System.Net.Http.Json;
using System.Text.Json;

namespace JiraServerMcp.JiraIntegration.Tests.Harness;

/// <summary>
/// Mints a real personal access token, so the suite authenticates exactly as a user does rather
/// than over the basic authentication the harness seeds with.
/// </summary>
/// <remarks>
/// Atlassian documents only creating these through the user interface, but the Phase 0 spike
/// established that <c>POST /rest/pat/latest/tokens</c> answers 201 on 8.20.7. Neither fallback
/// the design allowed for — a form post, or a pre-provisioned database dump — is needed.
/// </remarks>
internal static class PersonalAccessTokenMinter
{
    public static async Task<string> MintAsync(
        HttpClient client, JiraAdministrator administrator, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/rest/pat/latest/tokens")
        {
            // expirationDuration is in days, and one is far longer than the licence lives.
            Content = JsonContent.Create(new { name = "jira-server-mcp-harness", expirationDuration = 1 }),
        };

        request.Headers.Authorization = administrator.AuthenticationHeader;
        request.Headers.Add("X-Atlassian-Token", "no-check");

        using var response = await client.SendAsync(request, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if ((int)response.StatusCode is not 201)
        {
            throw new InvalidOperationException(
                $"Minting a personal access token answered {(int)response.StatusCode}, not 201.");
        }

        // Returned once and never again, exactly as in the user interface.
        return JsonDocument.Parse(body).RootElement.TryGetProperty("rawToken", out var token)
            ? token.GetString()!
            : throw new InvalidOperationException("The token response carried no rawToken.");
    }
}
