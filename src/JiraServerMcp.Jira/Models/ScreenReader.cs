using System.Text.Json;

namespace JiraServerMcp.Jira.Models;

/// <summary>
/// Reads Jira's screen metadata — the create screen and the edit screen — into
/// <see cref="JiraCreateFields"/> and <see cref="JiraEditFields"/>. Hand-written because the
/// fields arrive keyed by field identifier — the thing a caller most needs is the property name,
/// which no serializer can map on its own — and because an allowed value's shape differs per field
/// type.
/// </summary>
/// <remarks>
/// The two payloads carry the same per-field shape and differ only in where the fields object sits:
/// <c>projects[0].issuetypes[0].fields</c> against a bare <c>fields</c>. So each screen locates its
/// own fields object and the traversal below is shared.
/// </remarks>
internal static class ScreenReader
{
    /// <summary>
    /// The one project and issue type that were asked for, or null when Jira knows neither. Jira
    /// answers an unknown project or type with an empty project list and a 200, so "not found"
    /// arrives here rather than as an error.
    /// </summary>
    public static JiraCreateFields? ReadCreateScreen(JsonElement root)
    {
        if (!TryArray(root, "projects", out var projects)
            || projects.FirstOrDefault() is not { ValueKind: JsonValueKind.Object } project
            || !TryArray(project, "issuetypes", out var issueTypes)
            || issueTypes.FirstOrDefault() is not { ValueKind: JsonValueKind.Object } issueType)
        {
            return null;
        }

        return new JiraCreateFields(
            ProjectKey: String(project, "key") ?? "",
            IssueTypeName: String(issueType, "name") ?? "",
            Fields: Fields(issueType));
    }

    /// <summary>
    /// The edit screen of the issue that was asked for. There is no null here: Jira answers an
    /// unknown key with a 404, which never reaches this reader.
    /// </summary>
    public static JiraEditFields ReadEditScreen(string key, JsonElement root) =>
        new(key, Fields(root));

    private static IReadOnlyList<JiraScreenField> Fields(JsonElement owner)
    {
        if (!owner.TryGetProperty("fields", out var fields)
            || fields.ValueKind is not JsonValueKind.Object)
        {
            return [];
        }

        return
        [
            .. fields.EnumerateObject().Select(field => new JiraScreenField(
                Id: field.Name,
                Name: String(field.Value, "name") ?? field.Name,
                Type: field.Value.TryGetProperty("schema", out var schema)
                    ? String(schema, "type")
                    : null,
                Required: field.Value.TryGetProperty("required", out var required)
                          && required.ValueKind is JsonValueKind.True,
                AllowedValues: AllowedValues(field.Value),
                Operations: Operations(field.Value))),
        ];
    }

    /// <summary>
    /// What Jira says may be done to the field, or null where Jira said nothing. An empty list is
    /// a real answer — the field is on the screen and cannot be written — and absence is not that
    /// claim, so the two are not collapsed into one value a caller could not tell apart.
    /// </summary>
    private static IReadOnlyList<string>? Operations(JsonElement field) =>
        TryArray(field, "operations", out var operations)
            ? [.. operations.Select(operation => operation.GetString()).OfType<string>()]
            : null;

    /// <summary>
    /// An allowed value as a caller would name it. Which property carries that name depends on the
    /// field type — an option has a <c>value</c>, a version or a component has a <c>name</c> — so
    /// each is tried before falling back to the identifier, which Jira always sends.
    /// </summary>
    private static IReadOnlyList<string> AllowedValues(JsonElement field)
    {
        if (!TryArray(field, "allowedValues", out var values))
        {
            return [];
        }

        return
        [
            .. values.Select(value => value.ValueKind is JsonValueKind.String
                    ? value.GetString()
                    : String(value, "value") ?? String(value, "name") ?? String(value, "id"))
                .OfType<string>(),
        ];
    }

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
