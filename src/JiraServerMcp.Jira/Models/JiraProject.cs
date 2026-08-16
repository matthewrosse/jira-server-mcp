using System.Text.Json.Serialization;

namespace JiraServerMcp.Jira.Models;

/// <summary>
/// A project as an orientation call returns it: what an agent needs to pick one out of a list and
/// name it in a follow-up call, and nothing else.
/// </summary>
/// <remarks>
/// The project type is Jira's own word for the kind of project — <c>software</c>,
/// <c>business</c>, <c>service_desk</c> — and is absent on instances old enough not to have typed
/// their projects.
/// </remarks>
public sealed record JiraProject(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("projectTypeKey")] string? ProjectTypeKey);

/// <summary>
/// One project, merged from the four calls Jira makes a reader do: the project itself, the
/// statuses each issue type can be in, the components, and the versions. An agent preparing to
/// file a ticket needs all of it before it can send anything Jira will accept.
/// </summary>
public sealed record JiraProjectDetail(
    JiraProject Project,
    string? Description,
    string? Lead,
    IReadOnlyList<JiraIssueTypeStatuses> IssueTypes,
    IReadOnlyList<JiraProjectComponent> Components,
    IReadOnlyList<JiraProjectVersion> Versions);

/// <summary>
/// An issue type in this project and the statuses its workflow allows. The pair is what makes the
/// statuses meaningful: two issue types in one project routinely run different workflows.
/// </summary>
public sealed record JiraIssueTypeStatuses(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("subtask")] bool Subtask,
    [property: JsonPropertyName("statuses")] IReadOnlyList<JiraStatus> Statuses);

/// <summary>One status in a workflow.</summary>
public sealed record JiraStatus(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name);

/// <summary>One of the project's components. The description is Jira-authored free text.</summary>
public sealed record JiraProjectComponent(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description);

/// <summary>
/// One of the project's versions. Released and archived are separate flags in Jira, and a version
/// can be both.
/// </summary>
public sealed record JiraProjectVersion(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("released")] bool Released,
    [property: JsonPropertyName("archived")] bool Archived,
    [property: JsonPropertyName("releaseDate")] string? ReleaseDate);

/// <summary>
/// The project response as Jira sends it. It carries more than <see cref="JiraProject"/> keeps —
/// the description and the lead, which belong to a project read rather than to a listing.
/// </summary>
internal sealed record JiraProjectResponse(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("projectTypeKey")] string? ProjectTypeKey,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("lead")] JiraUser? Lead);
