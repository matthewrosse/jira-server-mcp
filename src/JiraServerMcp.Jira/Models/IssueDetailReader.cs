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
    /// Reads one issue. <paramref name="collectionFields"/> names the projected fields Jira
    /// answers with as a collection rather than a value: each becomes a section, and none is left
    /// in the projection, where it would render as a JSON blob. The caller names them because the
    /// caller is what decides which sections may be asked for; a copy kept here would be the same
    /// strings on both sides of the edge, free to drift apart.
    /// </summary>
    public static JiraIssueDetail Read(JsonElement root, IReadOnlyList<string> collectionFields)
    {
        var fields = root.TryGetProperty("fields", out var element)
                     && element.ValueKind is JsonValueKind.Object
            ? element
            : default;

        return new JiraIssueDetail(
            Key: root.TryGetProperty("key", out var key) ? key.GetString() ?? "" : "",
            Fields: Projection(fields, collectionFields),
            Transitions: Transitions(root),
            Changelog: Changelog(root),
            Comments: Comments(fields),
            Links: Links(fields),
            RemoteLinks: null,
            Worklogs: Worklogs(fields),
            Attachments: Attachments(fields),
            Subtasks: Subtasks(fields));
    }

    private static IReadOnlyDictionary<string, JsonElement> Projection(
        JsonElement fields,
        IReadOnlyList<string> collectionFields)
    {
        var projection = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        if (fields.ValueKind is not JsonValueKind.Object)
        {
            return projection;
        }

        foreach (var field in fields.EnumerateObject())
        {
            if (!collectionFields.Contains(field.Name, StringComparer.Ordinal))
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

    /// <summary>
    /// The files on the issue. One with no identifier is dropped: the identifier is the whole
    /// point of listing it, since a fetch has nothing else to name it by.
    /// </summary>
    private static IReadOnlyList<JiraAttachment> Attachments(JsonElement fields)
    {
        if (fields.ValueKind is not JsonValueKind.Object
            || !TryArray(fields, "attachment", out var attachments))
        {
            return [];
        }

        return
        [
            .. attachments
                .Select(attachment => Identifier(attachment) is { Length: > 0 } id
                    ? new JiraAttachment(
                        Id: id,
                        FileName: String(attachment, "filename") ?? "(unnamed)",
                        Size: attachment.TryGetProperty("size", out var size)
                              && size.ValueKind is JsonValueKind.Number
                            ? size.GetInt64()
                            : 0,
                        MimeType: String(attachment, "mimeType"),
                        Content: String(attachment, "content"))
                    : null)
                .OfType<JiraAttachment>(),
        ];
    }

    /// <summary>
    /// An attachment's identifier, quoted or numbered. Jira's own two shapes for the same value
    /// disagree — the issue field carries it as a string and the attachment endpoint as a number —
    /// so neither is assumed, and a file is never dropped from a listing over its spelling.
    /// </summary>
    private static string? Identifier(JsonElement attachment) =>
        attachment.TryGetProperty("id", out var id)
            ? id.ValueKind switch
            {
                JsonValueKind.String => id.GetString(),
                JsonValueKind.Number => id.GetRawText(),
                _ => null,
            }
            : null;

    /// <summary>
    /// The issue's sub-tasks, in the order Jira returned them, which is the parent's own rank
    /// order. One with no key is dropped: the key is the only part a reader of it can act on.
    /// </summary>
    private static IReadOnlyList<JiraSubtask> Subtasks(JsonElement fields)
    {
        if (fields.ValueKind is not JsonValueKind.Object
            || !TryArray(fields, "subtasks", out var subtasks))
        {
            return [];
        }

        return [.. subtasks.Select(Subtask).OfType<JiraSubtask>()];
    }

    /// <summary>
    /// One sub-task. Jira embeds a projection of its own under <c>fields</c>, and an instance that
    /// answered without one leaves the sub-task as its key alone rather than dropping it.
    /// </summary>
    private static JiraSubtask? Subtask(JsonElement subtask)
    {
        if (String(subtask, "key") is not { Length: > 0 } key)
        {
            return null;
        }

        subtask.TryGetProperty("fields", out var fields);

        return new JiraSubtask(
            Key: key,
            Status: fields.ValueKind is JsonValueKind.Object
                    && fields.TryGetProperty("status", out var status)
                ? String(status, "name")
                : null,
            Summary: String(fields, "summary"));
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

    /// <summary>
    /// The attachment endpoint's answer, which carries the same properties an issue's attachment
    /// field does — except that it numbers the identifier rather than quoting it, so the one the
    /// caller asked by is the one carried back.
    /// </summary>
    public static JiraAttachment ReadAttachment(JsonElement root, string id) =>
        new(
            Id: id,
            FileName: String(root, "filename") ?? "(unnamed)",
            Size: root.TryGetProperty("size", out var size)
                  && size.ValueKind is JsonValueKind.Number
                ? size.GetInt64()
                : 0,
            MimeType: String(root, "mimeType"),
            Content: String(root, "content"));

    /// <summary>
    /// The remote-link endpoint's answer: a bare array, each entry carrying the link's target
    /// inside an <c>object</c> of its own. A link with no URL is dropped — it is the one part a
    /// reader can do nothing without.
    /// </summary>
    public static IReadOnlyList<JiraRemoteLink> ReadRemoteLinks(JsonElement root)
    {
        if (root.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. root.EnumerateArray()
                .Select(link => link.TryGetProperty("object", out var target)
                                && String(target, "url") is { Length: > 0 } url
                    ? new JiraRemoteLink(
                        Title: String(target, "title") ?? url,
                        Url: url,
                        Relationship: String(link, "relationship"))
                    : null)
                .OfType<JiraRemoteLink>(),
        ];
    }

    /// <summary>
    /// The link types this Jira publishes, each with the wording for both of its ends.
    /// </summary>
    public static IReadOnlyList<JiraIssueLinkType> ReadIssueLinkTypes(JsonElement root)
    {
        if (!TryArray(root, "issueLinkTypes", out var types))
        {
            return [];
        }

        return
        [
            .. types.Select(type => new JiraIssueLinkType(
                Name: String(type, "name") ?? "",
                Inward: String(type, "inward") ?? "",
                Outward: String(type, "outward") ?? "")),
        ];
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
