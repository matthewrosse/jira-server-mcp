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
    [property: JsonPropertyName("fields")] IReadOnlyDictionary<string, JsonElement> Fields)
{
    /// <summary>The status id, which survives an admin renaming the workflow.</summary>
    [JsonIgnore]
    public string? StatusId => JiraFields.StatusId(Fields);

    /// <summary>The status name, which is the field "is this still open?" turns on.</summary>
    [JsonIgnore]
    public string? Status => JiraFields.StatusName(Fields);

    [JsonIgnore]
    public string? TypeName => JiraFields.TypeName(Fields);

    /// <summary>The assignee's username, which a follow-up JQL can use.</summary>
    [JsonIgnore]
    public string? Assignee => JiraFields.Assignee(Fields);
}
