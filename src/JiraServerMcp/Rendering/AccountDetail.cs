using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// The single account a profile is authenticated as. Not truncated: the response budget exists
/// to stop a page of rows flooding an agent's context, and one record of fields Jira Server caps
/// at 255 characters cannot.
/// </summary>
internal static class AccountDetail
{
    public static string Render(JiraUser user, string profileName) =>
        UntrustedContent.Envelope(
            $"account on profile '{profileName}'",
            $"""
            display name: {user.DisplayName}
            username: {user.Name}
            email: {user.EmailAddress ?? "(no email)"}
            status: {(user.Active ? "active" : "inactive")}
            """);
}
