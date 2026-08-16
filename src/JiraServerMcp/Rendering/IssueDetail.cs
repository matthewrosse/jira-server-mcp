using System.Text;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// One issue as text: the key, the field projection, and a section for each expansion that was
/// asked for. A section that was not asked for is absent rather than empty, so an agent reading
/// the response cannot mistake "not requested" for "there are none".
/// </summary>
internal static class IssueDetail
{
    /// <summary>
    /// The most entries any one section is worth. An issue that has been open for a year carries
    /// hundreds of history groups, and the recent ones are the ones being asked about; the rest
    /// would cost the agent its context to learn nothing.
    /// </summary>
    public const int SectionCap = 20;

    public static string Render(JiraIssueDetail issue)
    {
        var body = new StringBuilder();

        foreach (var field in issue.Fields)
        {
            if (FieldValue.Read(field.Value) is { Length: > 0 } value)
            {
                body.Append(field.Key).Append(": ").AppendLine(Truncation.Body(value));
            }
        }

        Transitions(body, issue.Transitions);
        Comments(body, issue.Comments);
        History(body, issue.Changelog);
        Links(body, issue.Links);
        Worklogs(body, issue.Worklogs);

        return $"""
            {issue.Key}
            {UntrustedContent.Preamble}
            {UntrustedContent.Delimit(body.ToString().TrimEnd())}
            """;
    }

    private static void Transitions(StringBuilder body, IReadOnlyList<JiraTransition> transitions)
    {
        if (transitions.Count is 0)
        {
            // Absent rather than "none": a transition list is only ever present when it was asked
            // for, and an issue always has at least one transition available to someone.
            return;
        }

        body.AppendLine().AppendLine("transitions:");

        foreach (var transition in transitions)
        {
            body.Append("  ").Append(transition.Name)
                .Append(" (id ").Append(transition.Id).Append(')');

            if (transition.ToStatus is { Length: > 0 } status)
            {
                body.Append(" to ").Append(status);
            }

            // Only the required ones: an agent that names this transition must supply them, and
            // the optional ones are noise until it decides to set one.
            var required = transition.Fields.Where(field => field.Required).ToArray();

            if (required.Length > 0)
            {
                body.Append(" — requires: ").Append(string.Join(
                    ", ",
                    required.Select(field => $"{field.Id} ({field.Name})")));
            }

            body.AppendLine();
        }
    }

    private static void Comments(StringBuilder body, JiraComments? comments)
    {
        if (comments is null)
        {
            return;
        }

        var shown = Newest(comments.Comments, comments.Total, out var heading);

        body.AppendLine().Append("comments ").Append(heading).AppendLine(":");

        foreach (var comment in shown)
        {
            body.Append("  ").Append(comment.Author ?? "(unknown)")
                .Append(", ").AppendLine(comment.Created ?? "(no date)");

            if (comment.Body is { Length: > 0 } text)
            {
                body.Append("  ").AppendLine(Truncation.Body(text));
            }
        }
    }

    private static void History(StringBuilder body, JiraChangelog? changelog)
    {
        if (changelog is null)
        {
            return;
        }

        var shown = Newest(changelog.Histories, changelog.Total, out var heading);

        body.AppendLine().Append("history ").Append(heading).AppendLine(":");

        foreach (var group in shown)
        {
            body.Append("  ").Append(group.Author ?? "(unknown)")
                .Append(", ").AppendLine(group.Created ?? "(no date)");

            foreach (var item in group.Items)
            {
                body.Append("    ").Append(item.Field).Append(": ")
                    .Append(Value(item.From)).Append(" to ").AppendLine(Value(item.To));
            }
        }
    }

    private static void Links(StringBuilder body, IReadOnlyList<JiraIssueLink> links)
    {
        if (links.Count is 0)
        {
            return;
        }

        var shown = links.Take(SectionCap).ToArray();

        body.AppendLine().Append("links ").Append(Heading(shown.Length, links.Count)).AppendLine(":");

        foreach (var link in shown)
        {
            body.Append("  ").Append(link.Relation).Append(' ').Append(link.Key);

            if (link.Summary is { Length: > 0 } summary)
            {
                body.Append(" — ").Append(Truncation.Body(summary));
            }

            body.AppendLine();
        }
    }

    private static void Worklogs(StringBuilder body, JiraWorklogs? worklogs)
    {
        if (worklogs is null)
        {
            return;
        }

        var shown = worklogs.Worklogs.Take(SectionCap).ToArray();

        body.AppendLine().Append("worklogs ")
            .Append(Heading(shown.Length, worklogs.Total)).AppendLine(":");

        foreach (var entry in shown)
        {
            body.Append("  ").Append(entry.Author ?? "(unknown)")
                .Append(", ").Append(entry.TimeSpent ?? "(no duration)")
                .Append(", started ").AppendLine(entry.Started ?? "(no start time)");
        }
    }

    /// <summary>
    /// The most recent entries of a section Jira ordered oldest first, capped. Reversing before
    /// capping is what makes the cap keep the newest rather than the first ones Jira happened to
    /// send.
    /// </summary>
    private static T[] Newest<T>(IReadOnlyList<T> entries, int total, out string heading)
    {
        var shown = entries.Reverse().Take(SectionCap).ToArray();

        heading = Heading(shown.Length, total) + (shown.Length > 1 ? ", newest first" : "");

        return shown;
    }

    private static string Heading(int shown, int total) =>
        shown is 0
            ? "(none)"
            : shown < total
                ? $"(showing {shown} of {total})"
                : $"({total})";

    private static string Value(string? value) => value is { Length: > 0 } text ? text : "(none)";
}
