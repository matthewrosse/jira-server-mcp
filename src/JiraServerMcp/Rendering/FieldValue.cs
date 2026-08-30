using System.Text.Json;

namespace JiraServerMcp.Rendering;

/// <summary>
/// One projected field's value as text. Jira answers with a different shape per field type — a
/// bare string, an object naming something, or a list of either — and every read tool meets all
/// of them, because the projection is open to any custom field.
/// </summary>
internal static class FieldValue
{
    /// <summary>
    /// The value as a reader would say it, or null for a field Jira left empty. An empty field is
    /// left out rather than rendered as a slot the agent has to read past.
    /// </summary>
    public static string? Read(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
        JsonValueKind.Array => string.Join(
            ", ",
            element.EnumerateArray().Select(Read).OfType<string>()),
        JsonValueKind.Object => Named(element),
        _ => null,
    };

    private static string? Named(JsonElement element)
    {
        // An issue Jira names inside another issue's field — the parent above all — carries no
        // name of its own, so the loop below would fall through to the bare key. The status is
        // what a reader wants of it and the one field the embedded projection carries; the
        // summary is not, because this renders on every search line as well as every issue read.
        if (Issue(element) is { Length: > 0 } issue)
        {
            return issue;
        }

        foreach (var property in (string[])["name", "displayName", "value", "key"])
        {
            if (element.TryGetProperty(property, out var named)
                && named.ValueKind is JsonValueKind.String)
            {
                return named.GetString();
            }
        }

        // A widened projection can name a custom field whose value is some shape of Jira's own.
        // Its JSON is worth more to an agent than nothing at all.
        return element.GetRawText();
    }

    /// <summary>
    /// An embedded issue as "key (status)", or null for an object that is not one. Both halves
    /// are required: an object carrying a key and no status is some other shape of Jira's, and
    /// the key alone is what the fallback below already renders.
    /// </summary>
    private static string? Issue(JsonElement element) =>
        element.TryGetProperty("key", out var key)
        && key.ValueKind is JsonValueKind.String
        && element.TryGetProperty("fields", out var fields)
        && fields.ValueKind is JsonValueKind.Object
        && fields.TryGetProperty("status", out var status)
        && status.ValueKind is JsonValueKind.Object
        && status.TryGetProperty("name", out var name)
        && name.ValueKind is JsonValueKind.String
            ? $"{key.GetString()} ({name.GetString()})"
            : null;
}
