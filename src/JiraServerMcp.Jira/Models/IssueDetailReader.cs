using System.Text.Json;

namespace JiraServerMcp.Jira.Models;

/// <summary>
/// Reads Jira's single-issue response into <see cref="JiraIssueDetail"/>. It is hand-written
/// rather than attribute-driven because the response mixes an open field projection with sections
/// of a fixed shape, and because two of those sections — links and transition screens — are keyed
/// in a way no serializer can map on its own.
/// </summary>
internal static class IssueDetailReader
{
    /// <summary>
    /// The projected fields Jira answers with as a collection rather than a value. Each becomes a
    /// section, and none is left in the projection, where it would render as a JSON blob.
    /// </summary>
    private static readonly string[] _collectionFields = ["comment", "issuelinks", "worklog"];

    public static JiraIssueDetail Read(JsonElement root)
    {
        var fields = root.TryGetProperty("fields", out var element)
                     && element.ValueKind is JsonValueKind.Object
            ? element
            : default;

        return new JiraIssueDetail(
            Key: root.TryGetProperty("key", out var key) ? key.GetString() ?? "" : "",
            Fields: Projection(fields),
            Transitions: Transitions(root),
            Changelog: Changelog(root),
            Comments: Comments(fields),
            Links: Links(fields),
            Worklogs: Worklogs(fields));
    }

    private static IReadOnlyDictionary<string, JsonElement> Projection(JsonElement fields)
    {
        var projection = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        if (fields.ValueKind is not JsonValueKind.Object)
        {
            return projection;
        }

        foreach (var field in fields.EnumerateObject())
        {
            if (!_collectionFields.Contains(field.Name, StringComparer.Ordinal))
            {
                // The document this came from is disposed before the caller reads it.
                projection[field.Name] = field.Value.Clone();
            }
        }

        return projection;
    }

    /// <summary>
    /// The transitions section, which the dedicated transitions endpoint answers with in exactly
    /// the shape it takes inside an issue read.
    /// </summary>
    public static IReadOnlyList<JiraTransition> ReadTransitions(JsonElement root) =>
        Transitions(root);

    private static IReadOnlyList<JiraTransition> Transitions(JsonElement root)
    {
        if (!TryArray(root, "transitions", out var transitions))
        {
            return [];
        }

        return
        [
            .. transitions.Select(transition => new JiraTransition(
                Id: String(transition, "id") ?? "",
                Name: String(transition, "name") ?? "",
                ToStatus: transition.TryGetProperty("to", out var to) ? String(to, "name") : null,
                Fields: TransitionFields(transition))),
        ];
    }

    /// <summary>
    /// A transition screen's fields arrive keyed by field id, so the id is the property name and
    /// everything else is inside it.
    /// </summary>
    private static IReadOnlyList<JiraTransitionField> TransitionFields(JsonElement transition)
    {
        if (!transition.TryGetProperty("fields", out var fields)
            || fields.ValueKind is not JsonValueKind.Object)
        {
            return [];
        }

        return
        [
            .. fields.EnumerateObject().Select(field => new JiraTransitionField(
                Id: field.Name,
                Name: String(field.Value, "name") ?? field.Name,
                Required: field.Value.TryGetProperty("required", out var required)
                          && required.ValueKind is JsonValueKind.True)),
        ];
    }

    private static JiraChangelog? Changelog(JsonElement root)
    {
        if (!root.TryGetProperty("changelog", out var changelog)
            || changelog.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        var histories = TryArray(changelog, "histories", out var groups)
            ? groups.Select(group => new JiraChangeGroup(
                Author: Author(group),
                Created: String(group, "created"),
                Items: ChangeItems(group))).ToArray()
            : [];

        return new JiraChangelog(Total(changelog, histories.Length), histories);
    }

    private static IReadOnlyList<JiraChangeItem> ChangeItems(JsonElement group)
    {
        if (!TryArray(group, "items", out var items))
        {
            return [];
        }

        return
        [
            .. items.Select(item => new JiraChangeItem(
                Field: String(item, "field") ?? "",
                From: String(item, "fromString"),
                To: String(item, "toString"))),
        ];
    }

    private static JiraComments? Comments(JsonElement fields)
    {
        if (fields.ValueKind is not JsonValueKind.Object
            || !fields.TryGetProperty("comment", out var comment)
            || comment.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        var comments = TryArray(comment, "comments", out var entries)
            ? entries.Select(entry => new JiraComment(
                Author: Author(entry),
                Created: String(entry, "created"),
                Body: String(entry, "body"))).ToArray()
            : [];

        return new JiraComments(Total(comment, comments.Length), comments);
    }

    private static IReadOnlyList<JiraIssueLink> Links(JsonElement fields)
    {
        if (fields.ValueKind is not JsonValueKind.Object
            || !TryArray(fields, "issuelinks", out var links))
        {
            return [];
        }

        return [.. links.Select(Link).OfType<JiraIssueLink>()];
    }

    /// <summary>
    /// Which end of the link this issue is on is told only by which of the two issue properties
    /// Jira filled in, and that end picks the wording of the relation.
    /// </summary>
    private static JiraIssueLink? Link(JsonElement link)
    {
        link.TryGetProperty("type", out var type);

        var (other, wording) = link.TryGetProperty("outwardIssue", out var outward)
            ? (outward, "outward")
            : link.TryGetProperty("inwardIssue", out var inward)
                ? (inward, "inward")
                : (default, "");

        if (other.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        var relation = String(type, wording) ?? String(type, "name") ?? "relates to";

        return new JiraIssueLink(
            Relation: relation,
            Key: String(other, "key") ?? "",
            Summary: other.TryGetProperty("fields", out var otherFields)
                ? String(otherFields, "summary")
                : null);
    }

    private static JiraWorklogs? Worklogs(JsonElement fields)
    {
        if (fields.ValueKind is not JsonValueKind.Object
            || !fields.TryGetProperty("worklog", out var worklog)
            || worklog.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        var worklogs = TryArray(worklog, "worklogs", out var entries)
            ? entries.Select(entry => new JiraWorklog(
                Author: Author(entry),
                TimeSpent: String(entry, "timeSpent"),
                Started: String(entry, "started"))).ToArray()
            : [];

        return new JiraWorklogs(Total(worklog, worklogs.Length), worklogs);
    }

    /// <summary>
    /// A person, by the name a reader recognises rather than the one Jira keys them by. Jira omits
    /// the author entirely on anonymous or deleted accounts.
    /// </summary>
    private static string? Author(JsonElement element) =>
        element.TryGetProperty("author", out var author)
        && author.ValueKind is JsonValueKind.Object
            ? String(author, "displayName") ?? String(author, "name")
            : null;

    /// <summary>
    /// Jira's own count for a section, which exceeds what it sent whenever the section was capped
    /// at its end. A section that reports no total has sent everything it has.
    /// </summary>
    private static int Total(JsonElement section, int sent) =>
        section.TryGetProperty("total", out var total) && total.ValueKind is JsonValueKind.Number
            ? total.GetInt32()
            : sent;

    private static bool TryArray(JsonElement element, string name, out JsonElement[] items)
    {
        if (element.ValueKind is JsonValueKind.Object
            && element.TryGetProperty(name, out var array)
            && array.ValueKind is JsonValueKind.Array)
        {
            items = [.. array.EnumerateArray()];

            return true;
        }

        items = [];

        return false;
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind is JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}
