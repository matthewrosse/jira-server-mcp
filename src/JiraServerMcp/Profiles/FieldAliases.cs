using System.Text.RegularExpressions;

namespace JiraServerMcp.Profiles;

/// <summary>
/// The operator's own names for this Jira's fields: <c>story_points</c> for
/// <c>customfield_10010</c>. A custom field is named only by identifier everywhere an agent
/// touches it, and the identifier differs per instance — so a workflow that sets story points
/// against a second Jira is otherwise a different workflow.
/// </summary>
/// <remarks>
/// Declared by the operator and never derived from Jira's own field names. Derivation would give
/// two instances the same alias by accident, which is a trap rather than a contract: the operator's
/// intent is the one thing nothing else can supply.
///
/// An alias is an additional name, never a rename. What it resolves to is what Jira is sent, and
/// what a read shows is the alias beside the identifier — an agent still needs the identifier for
/// everything an alias does not cover.
/// </remarks>
internal sealed partial class FieldAliases
{
    private readonly IReadOnlyDictionary<string, string> _byAlias;
    private readonly IReadOnlyDictionary<string, string> _byField;

    private FieldAliases(IReadOnlyDictionary<string, string> byAlias)
    {
        _byAlias = byAlias;

        // Two aliases for one field is allowed and harmless; a read shows whichever came first,
        // and both resolve on the way in.
        _byField = byAlias
            .GroupBy(entry => entry.Value, StringComparer.Ordinal)
            .ToDictionary(field => field.Key, field => field.First().Key, StringComparer.Ordinal);
    }

    /// <summary>A profile that declares none, which is every profile until an operator says otherwise.</summary>
    public static FieldAliases None { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    public static FieldAliases For(IReadOnlyDictionary<string, string>? declared) =>
        declared is { Count: > 0 }
            ? new FieldAliases(new Dictionary<string, string>(declared, StringComparer.OrdinalIgnoreCase))
            : None;

    /// <summary>The aliases this profile declares, in the order an operator would read them.</summary>
    public IReadOnlyList<string> Known =>
        [.. _byAlias.Select(entry => $"{entry.Key} -> {entry.Value}").Order(StringComparer.Ordinal)];

    public bool Any => _byAlias.Count > 0;

    /// <summary>
    /// What Jira is asked for. An alias resolves to its field; anything else is passed through
    /// untouched, because this server does not hold Jira's field catalogue and a name it does not
    /// recognise is far more likely to be a real identifier than a mistake.
    /// </summary>
    public string Resolve(string name) =>
        _byAlias.TryGetValue(name.Trim(), out var field) ? field : name;

    /// <summary>
    /// How a field is labelled in a read: the alias and the identifier together, never the alias
    /// alone. Replacing the identifier would hide the value an agent needs for the writes an alias
    /// does not cover.
    /// </summary>
    public string Label(string field) =>
        _byField.TryGetValue(field, out var alias) ? $"{alias} ({field})" : field;

    /// <summary>
    /// Whether a name may be declared as an alias. A name spelled like a Jira custom field
    /// identifier is refused: it would be ambiguous with the identifier itself, and an operator
    /// who wants a readable name has no reason to choose that spelling.
    /// </summary>
    public static bool IsDeclarable(string alias) =>
        alias.Trim().Length > 0 && !FieldIdentifier().IsMatch(alias.Trim());

    [GeneratedRegex(@"^customfield_\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex FieldIdentifier();
}
