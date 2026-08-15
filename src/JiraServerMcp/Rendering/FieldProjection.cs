namespace JiraServerMcp.Rendering;

/// <summary>
/// The set of issue fields a response carries. A raw Jira 8 issue is tens of kilobytes of nulls,
/// avatar URL sets, and expand scaffolding around a few hundred useful tokens, so the projection
/// is named on the request and Jira never sends the rest.
/// </summary>
internal static class FieldProjection
{
    /// <summary>
    /// What an agent needs to decide whether an issue is the one it is looking for, and nothing
    /// beyond that. Everything else is a widening the caller asks for explicitly.
    /// </summary>
    public static IReadOnlyList<string> Default { get; } =
    [
        "summary",
        "status",
        "issuetype",
        "priority",
        "assignee",
        "reporter",
        "created",
        "updated",
        "parent",
        "labels",
    ];

    /// <summary>
    /// The default projection plus whatever the caller asked for. Widening adds; it never
    /// replaces, so a caller reaching for one custom field does not lose the status by accident.
    /// </summary>
    public static IReadOnlyList<string> Widen(IReadOnlyList<string>? extra)
    {
        if (extra is null or { Count: 0 })
        {
            return Default;
        }

        var widened = new List<string>(Default);

        foreach (var field in extra)
        {
            var trimmed = field.Trim();

            if (trimmed.Length > 0 && !widened.Contains(trimmed, StringComparer.Ordinal))
            {
                widened.Add(trimmed);
            }
        }

        return widened;
    }
}
