using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// The single account a profile is authenticated as. Not truncated: the response budget exists
/// to stop a page of rows flooding an agent's context, and one record of fields Jira Server caps
/// at 255 characters cannot.
/// </summary>
internal static class AccountDetail
{
    public static Rendered Render(JiraUser user, string profileName) =>
        new(
            UntrustedContent.Envelope(
                $"account on profile '{profileName}'",
                $"""
                display name: {user.DisplayName}
                username: {user.Name}
                email: {user.EmailAddress ?? "(no email)"}
                status: {(user.Active ? "active" : "inactive")}
                """),
            // The display name and the email stay in the delimited region: the username is what a
            // write sends, and it is the only thing here a workflow branches on.
            ToolOutputs.Node(new AccountOutput
            {
                Outcome = Outcomes.Ok,
                Username = user.Name,
                Active = user.Active,
            }));
}
