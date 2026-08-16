using System.Text;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// One issue as text: the key, the field projection, and a section for each expansion that was
/// asked for. Which sections appear is decided by what the caller asked for rather than by what
/// came back, so an issue with no links renders an empty links section instead of looking as
/// though links were never requested.
/// </summary>
internal static class IssueDetail
{
    /// <summary>
    /// The most entries any one section is worth. An issue that has been open for a year carries
    /// hundreds of history groups, and the recent ones are the ones being asked about; the rest
    /// would cost the agent its context to learn nothing.
    /// </summary>
    public const int SectionCap = 20;

    public static string Render(JiraIssueDetail issue, IReadOnlyList<Expansion> expansions)
    {
        var body = new StringBuilder();

        foreach (var field in issue.Fields)
        {
            if (FieldValue.Read(field.Value) is { Length: > 0 } value)
            {
                // Not truncated: a search result's marker sends a caller here for the full text,
                // and cutting it here would make that promise false with nowhere else to go.
                body.Append(field.Key).Append(": ").AppendLine(value);
            }
        }

        if (expansions.Contains(Expansion.Transitions))
        {
            Transitions(body, issue.Transitions);
        }

        if (expansions.Contains(Expansion.Comments))
        {
            Comments(body, issue.Comments);
        }

        if (expansions.Contains(Expansion.Changelog))
        {
            History(body, issue.Changelog);
        }

        if (expansions.Contains(Expansion.Links))
        {
            Links(body, issue.Links);
        }

        if (expansions.Contains(Expansion.Worklogs))
        {
            Worklogs(body, issue.Worklogs);
        }

        return $"""
            {issue.Key}
            {UntrustedContent.Preamble}
            {UntrustedContent.Delimit(body.ToString().TrimEnd())}
            """;
    }

    private static void Transitions(StringBuilder body, IReadOnlyList<JiraTransition> transitions)
    {
        var shown = transitions.Take(SectionCap).ToArray();

        body.AppendLine().Append("transitions ")
            .Append(Heading(shown.Length, transitions.Count)).AppendLine(":");

        foreach (var transition in shown)
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
        var (shown, heading) = Ordered(
            comments?.Comments ?? [],
            comments?.Total ?? 0);

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
        var (shown, heading) = Ordered(
            changelog?.Histories ?? [],
            changelog?.Total ?? 0);

        body.AppendLine().Append("history ").Append(heading).AppendLine(":");

        foreach (var group in shown)
        {
            body.Append("  ").Append(group.Author ?? "(unknown)")
                .Append(", ").AppendLine(group.Created ?? "(no date)");

            foreach (var item in group.Items)
            {
                // A description or environment edit carries the whole of both versions here, so
                // these are prose like any other and are cut like it.
                body.Append("    ").Append(item.Field).Append(": ")
                    .Append(Value(item.From)).Append(" to ").AppendLine(Value(item.To));
            }
        }
    }

    private static void Links(StringBuilder body, IReadOnlyList<JiraIssueLink> links)
    {
        var shown = links.Take(SectionCap).ToArray();

        body.AppendLine().Append("links ")
            .Append(Heading(shown.Length, links.Count)).AppendLine(":");

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
        var entries = worklogs?.Worklogs ?? [];

        var shown = entries.Take(SectionCap).ToArray();

        body.AppendLine().Append("worklogs ")
            .Append(Heading(shown.Length, worklogs?.Total ?? 0)).AppendLine(":");

        foreach (var entry in shown)
        {
            body.Append("  ").Append(entry.Author ?? "(unknown)")
                .Append(", ").Append(entry.TimeSpent ?? "(no duration)")
                .Append(", started ").AppendLine(entry.Started ?? "(no start time)");
        }
    }

    /// <summary>
    /// A section Jira orders oldest first, capped, and labelled with the order it ended up in.
    /// </summary>
    /// <remarks>
    /// Jira caps some collections at its own end and sends the first page, which is the oldest
    /// entries. Reversing that page would hand back the oldest activity under a "newest first"
    /// label, which is worse than saying plainly that the recent entries were not returned. So the
    /// order is only claimed to be newest-first when everything Jira counted is actually in hand.
    /// </remarks>
    private static (T[] Shown, string Heading) Ordered<T>(IReadOnlyList<T> entries, int total)
    {
        if (entries.Count < total)
        {
            var oldest = entries.Take(SectionCap).ToArray();

            return (oldest, $"(showing {oldest.Length} of {total}, oldest first — Jira returned "
                            + "only the first page, so the most recent are not here)");
        }

        var newest = entries.Reverse().Take(SectionCap).ToArray();

        return (newest, newest.Length switch
        {
            0 => "(none)",
            1 => "(1)",
            _ when newest.Length < total => $"(showing {newest.Length} of {total}, newest first)",
            _ => $"({total}, newest first)",
        });
    }

    private static string Heading(int shown, int total) =>
        shown is 0
            ? "(none)"
            : shown < total
                ? $"(showing {shown} of {total})"
                : $"({total})";

    private static string Value(string? value) =>
        value is { Length: > 0 } text ? Truncation.Body(text) : "(none)";
}
