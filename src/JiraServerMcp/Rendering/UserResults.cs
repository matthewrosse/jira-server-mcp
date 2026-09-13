using System.Text;
using System.Text.Json.Serialization;
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

/// <summary>
/// A page of users. Jira's user search reports no total, so none is carried — what says there may
/// be more is a full page, which the prose spells out and the paging position here supports.
/// </summary>
internal sealed record UserSearchOutput : ToolOutput
{
    [JsonPropertyName("startAt")]
    public int? StartAt { get; init; }

    [JsonPropertyName("count")]
    public int? Count { get; init; }

    /// <summary>
    /// What was asked for, not what came back: a caller that sees only active users needs to know
    /// whether that is the instance or its own argument.
    /// </summary>
    [JsonPropertyName("includeInactive")]
    public bool? IncludeInactive { get; init; }

    /// <summary>
    /// The issue key or project key the search was narrowed to, as the caller gave it, and absent
    /// when it was not narrowed at all. A count narrowed by an assignment permission means
    /// something different from a count of the directory, and the rows carry nothing that says
    /// which of the two this is.
    /// </summary>
    [JsonPropertyName("assignableTo")]
    public string? AssignableTo { get; init; }

    [JsonPropertyName("users")]
    public IReadOnlyList<UserRowOutput>? Users { get; init; }
}

/// <summary>
/// One user. The username is the whole point — on Jira Server it is what a write must send, and
/// an agent that searched for a user is about to put it in an assignee field.
/// </summary>
/// <remarks>
/// The display name and the email address are deliberately absent. The selection-label carve-out
/// admits an admin-typed name only where the identifier is opaque and the name is the sole basis
/// for choosing; neither holds here, because the username both identifies and is what the write
/// sends. Someone disambiguating two similar people reads the prose, which is where a display name
/// belongs — and an email address is personal data this server would be promising to carry stably.
/// </remarks>
internal sealed record UserRowOutput
{
    [JsonPropertyName("username")]
    public required string Username { get; init; }

    [JsonPropertyName("active")]
    public required bool Active { get; init; }
}
