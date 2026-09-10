using System.Text.Json.Serialization;
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

/// <summary>
/// The account a profile is authenticated as. Small on purpose: rule 3 puts an outcome on this
/// result whatever else it carries, and the username is the value most likely to be fed straight
/// into an assignee field.
/// </summary>
internal sealed record AccountOutput : ToolOutput
{
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    [JsonPropertyName("active")]
    public bool? Active { get; init; }
}
