using System.Text.Json;
using System.Text.Json.Serialization;

namespace JiraServerMcp.Jira.Models;

/// <summary>
/// One issue as a search returned it: its key, and whichever fields the projection asked for.
/// The fields stay as JSON because the projection is open — a caller may widen it to any custom
/// field, and Jira's value shape differs per field type.
/// </summary>
public sealed record JiraIssue(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("fields")] IReadOnlyDictionary<string, JsonElement> Fields);
