using System.Net.Http.Headers;
using System.Text;

namespace JiraServerMcp.JiraIntegration.Tests.Harness;

/// <summary>
/// The administrator the setup wizard creates, and which the harness then seeds through.
/// </summary>
/// <remarks>
/// Basic authentication is used for seeding and for minting the personal access token, and it is
/// acceptable here for one reason: the harness is not the product. Nothing under <c>src/</c> may
/// authenticate this way — ADR-0001 makes personal access tokens the only credential, and the
/// suite itself holds a real one so it authenticates exactly as a user does.
/// </remarks>
internal sealed record JiraAdministrator(
    string Username = "admin",
    string Password = "admin123",
    string Email = "admin@example.invalid",
    string FullName = "Harness Administrator")
{
    public AuthenticationHeaderValue AuthenticationHeader => new(
        "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}")));
}
