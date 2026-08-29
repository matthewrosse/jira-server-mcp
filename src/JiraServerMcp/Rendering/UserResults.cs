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
        bool includeInactive,
        string? assignableTo)
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
                Header(users.Count, startAt, maxResults, includeInactive, assignableTo),
                lines.ToString().TrimEnd()),
            ToolOutputs.Node(new UserSearchOutput
            {
                Outcome = Outcomes.Ok,
                StartAt = startAt,
                Count = users.Count,
                IncludeInactive = includeInactive,
                AssignableTo = assignableTo,
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
    /// whether the page was full — a full page is the only sign there may be more. Where the search
    /// was narrowed to an issue or a project, the count is a count of who may be assigned there and
    /// the header says so: an anchored "none matched" that reads as a claim about the directory is
    /// a claim this server would be making falsely.
    /// </summary>
    private static string Header(
        int count,
        int startAt,
        int maxResults,
        bool includeInactive,
        string? assignableTo)
    {
        var inactive = assignableTo is not null
            ? "Inactive users cannot be included when assignableTo is set — Jira never offers one "
              + "as an assignee."
            : includeInactive
                ? "Inactive users were included."
                : "Inactive users were excluded; ask again with includeInactive: true to see them.";

        var subject = assignableTo is null ? "users" : $"users assignable on {assignableTo}";

        if (count is 0)
        {
            // The moment an agent is about to conclude that a person cannot be assigned, when what
            // happened is that it searched by something this endpoint does not match on.
            var matching = assignableTo is null
                ? string.Empty
                : " — this search matches usernames and display names, not email addresses, and it "
                  + "matches from the start of a name rather than anywhere inside it";

            return $"{subject}: none matched{matching}. {inactive}";
        }

        var page = count >= maxResults
            ? $" — a full page, so more may exist; ask for the next with startAt: {startAt + count}"
            : " — no more match";

        return $"{subject}: {count}, usernames first{page}. {inactive}";
    }
}
