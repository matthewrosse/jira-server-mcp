using System.Text.Json.Serialization;

namespace JiraServerMcp.Jira.Models;

/// <summary>
/// What this Jira will accept in a query: the fields it publishes as queryable and the functions
/// it offers. The counterpart of a screen, which says what a write will accept.
/// </summary>
public sealed record JiraJqlCatalogue(
    IReadOnlyList<JiraJqlField> Fields,
    IReadOnlyList<JiraJqlFunction> Functions);

/// <summary>
/// One queryable field. <paramref name="Name"/> is Jira's own <c>value</c> verbatim, quotes
/// included where Jira sent them — it is what goes in the clause, and dequoting it would publish
/// something that does not parse.
/// </summary>
/// <remarks>
/// <paramref name="CustomFieldId"/> is Jira's <c>cfid</c>, the bracket form <c>cf[10107]</c>, kept
/// as sent and null for a system field. It is not the <c>customfield_10107</c> identifier the
/// write tools hand out: that identifier is not a JQL name and a clause built from it is rejected.
///
/// The types are Java class names as Jira sends them. The operators are what this field takes,
/// which differs per field: <c>summary</c> takes <c>~</c> and not <c>=</c>.
/// </remarks>
public sealed record JiraJqlField(
    string Name,
    string? CustomFieldId,
    IReadOnlyList<string> Types,
    IReadOnlyList<string> Operators,
    bool Orderable,
    bool Searchable);

/// <summary>
/// One function this instance publishes, named as it is written in a clause —
/// <c>currentUser()</c>, <c>openSprints()</c>. The part of JQL an agent most reliably invents.
/// </summary>
public sealed record JiraJqlFunction(string Name, IReadOnlyList<string> Types);

/// <summary>
/// The values one field enumerates. An empty list is not an error: Jira answers 200 both for a
/// field it does not know and for one that enumerates nothing, so the two readings are told apart
/// by the caller rather than by the payload.
/// </summary>
public sealed record JiraJqlSuggestions(string Field, IReadOnlyList<string> Values);

/// <summary>
/// A saved filter as its owner wrote it in Jira: a name, the JQL it runs, and who owns it. The
/// counterpart of the catalogue above — that one says what this Jira can be asked, this one says
/// what somebody already asked and kept.
/// </summary>
/// <remarks>
/// Everything else Jira sends for a filter is dropped by not being declared: the avatar URLs, the
/// share permissions, the shared users and the subscriptions are most of the payload's bytes and
/// none of its meaning here. <paramref name="Jql"/> arrives without an <c>expand</c>, so the whole
/// row costs one call.
///
/// <paramref name="Name"/> and <paramref name="Description"/> are free text a human typed in Jira
/// and are untrusted content.
/// </remarks>
public sealed record JiraSavedFilter(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("jql")] string Jql,
    [property: JsonPropertyName("owner")] JiraSavedFilterOwner? Owner);

/// <summary>
/// Who owns a saved filter, carried as the username alone. A filter this account has favourited
/// can be owned by somebody else, which is the whole reason a favourite stays current: whoever
/// owns it keeps maintaining it.
/// </summary>
public sealed record JiraSavedFilterOwner(
    [property: JsonPropertyName("name")] string? Name);
