namespace JiraServerMcp.Jira.Models;

/// <summary>
/// What Jira will accept when an issue of one type is created in one project. Without it a create
/// call fails on a required custom field named only by an opaque identifier.
/// </summary>
public sealed record JiraCreateFields(
    string ProjectKey,
    string IssueTypeName,
    IReadOnlyList<JiraCreateField> Fields);

/// <summary>
/// One field on the create screen. <paramref name="Id"/> is what a create call must send — for a
/// custom field that is a <c>customfield_10xxx</c> identifier and nothing else will do.
/// </summary>
/// <remarks>
/// The type is Jira's own schema type — <c>string</c>, <c>option</c>, <c>array</c>. The allowed
/// values are what Jira will accept where the field is a list, and are empty for a field that
/// takes free text or a value Jira does not enumerate.
/// </remarks>
public sealed record JiraCreateField(
    string Id,
    string Name,
    string? Type,
    bool Required,
    IReadOnlyList<string> AllowedValues);
