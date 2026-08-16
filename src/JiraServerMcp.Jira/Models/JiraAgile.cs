using System.Text.Json.Serialization;

namespace JiraServerMcp.Jira.Models;

/// <summary>
/// One board. Its type — <c>scrum</c> or <c>kanban</c> — decides whether asking for its sprints is
/// worth a call at all.
/// </summary>
public sealed record JiraBoard(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string? Type);

/// <summary>
/// One sprint of a board. A future sprint carries no dates yet, which is ordinary rather than
/// missing data.
/// </summary>
public sealed record JiraSprint(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("startDate")] string? StartDate,
    [property: JsonPropertyName("endDate")] string? EndDate);
