using System.Text.Json;

namespace JiraServerMcp.Jira.Models;

/// <summary>
/// Reads Jira's autocomplete data into a <see cref="JiraJqlCatalogue"/>. Hand-written for the same
/// reason the screen reader is: the payload's booleans arrive as the strings <c>"true"</c> and
/// <c>"false"</c>, and an absent one is a real answer rather than a missing property.
/// </summary>
internal static class JqlReader
{
    public static JiraJqlCatalogue ReadCatalogue(JsonElement root) =>
        new(
            Fields: [.. Items(root, "visibleFieldNames").Select(field => new JiraJqlField(
                Name: String(field, "value") ?? "",
                CustomFieldId: String(field, "cfid"),
                Types: Strings(field, "types"),
                Operators: Strings(field, "operators"),
                Orderable: Flag(field, "orderable"),
                Searchable: Flag(field, "searchable")))],
            Functions: [.. Items(root, "visibleFunctionNames").Select(function => new JiraJqlFunction(
                Name: String(function, "value") ?? "",
                Types: Strings(function, "types")))]);

    /// <summary>
    /// The values one field enumerates, in the order Jira published them. Each is carried verbatim
    /// — Jira quotes a value that needs quoting, and it is the quoted form that goes in the query.
    /// </summary>
    public static JiraJqlSuggestions ReadSuggestions(string field, JsonElement root) =>
        new(field, [.. Items(root, "results").Select(result => String(result, "value")).OfType<string>()]);

    /// <summary>
    /// A boolean Jira sends as a string. Absent means false: a field with no <c>orderable</c> is
    /// one an ORDER BY clause may not name, which is the claim the renderer prints.
    /// </summary>
    private static bool Flag(JsonElement element, string name) =>
        string.Equals(String(element, name), "true", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Strings(JsonElement element, string name) =>
        [.. Items(element, name).Select(item => item.GetString()).OfType<string>()];

    private static JsonElement[] Items(JsonElement element, string name) =>
        element.ValueKind is JsonValueKind.Object
        && element.TryGetProperty(name, out var array)
        && array.ValueKind is JsonValueKind.Array
            ? [.. array.EnumerateArray()]
            : [];

    private static string? String(JsonElement element, string name) =>
        element.ValueKind is JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;
}
