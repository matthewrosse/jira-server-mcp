using System.Text.Json;

namespace JiraServerMcp.Jira.Models;

/// <summary>
/// Reads Jira's create metadata into <see cref="JiraCreateFields"/>. Hand-written because the
/// fields arrive keyed by field identifier — the thing a caller most needs is the property name,
/// which no serializer can map on its own — and because an allowed value's shape differs per field
/// type.
/// </summary>
internal static class CreateFieldsReader
{
    /// <summary>
    /// The one project and issue type that were asked for, or null when Jira knows neither. Jira
    /// answers an unknown project or type with an empty project list and a 200, so "not found"
    /// arrives here rather than as an error.
    /// </summary>
    public static JiraCreateFields? Read(JsonElement root)
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

    private static IReadOnlyList<JiraCreateField> Fields(JsonElement issueType)
    {
        if (!issueType.TryGetProperty("fields", out var fields)
            || fields.ValueKind is not JsonValueKind.Object)
        {
            return [];
        }

        return
        [
            .. fields.EnumerateObject().Select(field => new JiraCreateField(
                Id: field.Name,
                Name: String(field.Value, "name") ?? field.Name,
                Type: field.Value.TryGetProperty("schema", out var schema)
                    ? String(schema, "type")
                    : null,
                Required: field.Value.TryGetProperty("required", out var required)
                          && required.ValueKind is JsonValueKind.True,
                AllowedValues: AllowedValues(field.Value))),
        ];
    }

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
