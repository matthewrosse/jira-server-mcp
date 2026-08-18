using System.Text;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// Users as text, username first. On Jira Server the username is what a write must send — there is
/// no account identifier here, and anything shaped like one belongs to Cloud.
/// </summary>
internal static class UserResults
{
    public static Rendered Render(
        IReadOnlyList<JiraUser> users,
        int startAt,
        int maxResults,
        bool includeInactive)
    {
        var lines = new StringBuilder();

        foreach (var user in users)
        {
            lines.Append(user.Name).Append(" | ").Append(Truncation.Body(user.DisplayName))
                .Append(" | ").Append(user.EmailAddress ?? "(no email)")
                .Append(" | ").AppendLine(user.Active ? "active" : "inactive");
        }

        return new Rendered(
            UntrustedContent.Envelope(
                Header(users.Count, startAt, maxResults, includeInactive),
                lines.ToString().TrimEnd()),
            ToolOutputs.Node(new UserSearchOutput
            {
                Outcome = Outcomes.Ok,
                StartAt = startAt,
                Count = users.Count,
                IncludeInactive = includeInactive,
                Users =
                [
                    .. users.Select(user => new UserRowOutput
                    {
                        Username = user.Name,
                        Active = user.Active,
                    }),
                ],
            }));
    }

    /// <summary>
    /// Jira's user search reports no total, so what can be said honestly is how many came back and
    /// whether the page was full — a full page is the only sign there may be more.
    /// </summary>
    private static string Header(int count, int startAt, int maxResults, bool includeInactive)
    {
        var inactive = includeInactive
            ? "Inactive users were included."
            : "Inactive users were excluded; ask again with includeInactive: true to see them.";

        if (count is 0)
        {
            return $"users: none matched. {inactive}";
        }

        var page = count >= maxResults
            ? $" — a full page, so more may exist; ask for the next with startAt: {startAt + count}"
            : " — no more match";

        return $"users: {count}, usernames first{page}. {inactive}";
    }
}
