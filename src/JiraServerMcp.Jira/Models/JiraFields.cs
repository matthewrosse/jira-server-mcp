using System.Text.Json;

namespace JiraServerMcp.Jira.Models;

/// <summary>
/// The few Jira field ids this client reads by name, so that a caller can ask an issue for its
/// status or its type without learning Jira's vocabulary. Everything else stays in the open field
/// projection, where the caller named it and can read it back the same way.
/// </summary>
/// <remarks>
/// These are the fields the structured half of a tool result is built from (ADR-0009): a status
/// id, a status name, an issue type name, an assignee's username. A rendering module reaching into
/// the projection for <c>issuetype</c> would put Jira's vocabulary in the wrong project.
/// </remarks>
internal static class JiraFields
{
    public static string? StatusId(IReadOnlyDictionary<string, JsonElement> fields) =>
        Nested(fields, "status", "id");

    public static string? StatusName(IReadOnlyDictionary<string, JsonElement> fields) =>
        Nested(fields, "status", "name");

    public static string? TypeName(IReadOnlyDictionary<string, JsonElement> fields) =>
        Nested(fields, "issuetype", "name");

    /// <summary>
    /// The assignee's username — Jira Server's <c>name</c> — rather than the display name, which
    /// is prose and is what a follow-up JQL cannot use.
    /// </summary>
    public static string? Assignee(IReadOnlyDictionary<string, JsonElement> fields) =>
        Nested(fields, "assignee", "name");

    /// <summary>
    /// When Jira last touched the issue, as Jira wrote it — an offset timestamp in the instance's
    /// own zone. Left as the string Jira sent, because what reads it is a watermark that has to
    /// hand the same moment back, and a round trip through a parsed value is where an offset gets
    /// quietly restated in someone else's zone.
    /// </summary>
    public static string? Updated(IReadOnlyDictionary<string, JsonElement> fields) =>
        fields.TryGetValue("updated", out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? Nested(
        IReadOnlyDictionary<string, JsonElement> fields,
        string field,
        string property) =>
        fields.TryGetValue(field, out var value)
        && value.ValueKind is JsonValueKind.Object
        && value.TryGetProperty(property, out var nested)
        && nested.ValueKind is JsonValueKind.String
            ? nested.GetString()
            : null;
}
