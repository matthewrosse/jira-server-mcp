namespace JiraServerMcp.Jira.Models;

/// <summary>
/// What Jira will accept when an issue of one type is created in one project. Without it a create
/// call fails on a required custom field named only by an opaque identifier.
/// </summary>
public sealed record JiraCreateFields(
    string ProjectKey,
    string IssueTypeName,
    IReadOnlyList<JiraScreenField> Fields);

/// <summary>
/// What Jira will accept when one issue is updated. The edit screen is not the create screen: it
/// is chosen by the issue's type, and a field on it may still not be settable.
/// </summary>
/// <remarks>
/// <paramref name="Key"/> is the caller's own key. Jira's edit metadata carries no key, no project
/// and no issue type, and reading them would cost a round trip for a header line.
/// </remarks>
public sealed record JiraEditFields(
    string Key,
    IReadOnlyList<JiraScreenField> Fields);

/// <summary>
/// One field on a screen. <paramref name="Id"/> is what a write must send — for a custom field
/// that is a <c>customfield_10xxx</c> identifier and nothing else will do.
/// </summary>
/// <remarks>
/// The type is Jira's own schema type — <c>string</c>, <c>option</c>, <c>array</c>. The allowed
/// values are what Jira will accept where the field is a list, and are empty for a field that
/// takes free text or a value Jira does not enumerate. The operations are what Jira says may be
/// done to the field — <c>set</c>, <c>add</c>, <c>remove</c>. An empty list means the field is on
/// the screen and still not writable; null means Jira said nothing, which is not the same claim.
/// </remarks>
public sealed record JiraScreenField(
    string Id,
    string Name,
    string? Type,
    bool Required,
    IReadOnlyList<string> AllowedValues,
    IReadOnlyList<string>? Operations);
