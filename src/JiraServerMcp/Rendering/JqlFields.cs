using System.Text;
using System.Text.Json.Serialization;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Profiles;

namespace JiraServerMcp.Rendering;

/// <summary>
/// The query catalogue as text: what this Jira is queryable by, under which name, and with which
/// operators. Published and never matched against anything — a caller's own word is not resolved
/// here, so this is not a published vocabulary.
/// </summary>
internal static class JqlFields
{
    /// <summary>
    /// Every queryable field the cap admits, then the functions this instance offers. A custom
    /// field carries the alias beside its JQL name where the profile declares one: the alias
    /// resolves to an identifier that is not a JQL name, so the two are shown together or the
    /// operator who declared it has no way to connect them.
    /// </summary>
    public static Rendered Catalogue(
        JiraJqlCatalogue catalogue,
        string? startsWith,
        FieldAliases aliases)
    {
        var matched = Matching(catalogue.Fields, startsWith);
        var shown = matched.Take(ResponseBudget.JqlFieldCap).ToArray();

        var body = new StringBuilder();

        body.Append(FieldsHeading(shown.Length, matched.Count, catalogue.Fields.Count, startsWith))
            .AppendLine(":");

        foreach (var field in shown)
        {
            Row(body, field, aliases);
        }

        body.AppendLine().Append("functions (").Append(catalogue.Functions.Count).AppendLine("):");

        foreach (var function in catalogue.Functions)
        {
            body.Append("  ").Append(function.Name)
                .Append("  ").AppendLine(Types(function.Types));
        }

        return new Rendered(
            UntrustedContent.Envelope(
                "What this Jira accepts in a JQL query. A custom field is queryable as cf[10107] "
                + "or by the quoted name shown here, never as the customfield_10107 identifier "
                + "the write tools take. The operators shown are the ones that field accepts.",
                body.ToString().TrimEnd()),
            ToolOutputs.Node(new JqlFieldsOutput
            {
                Outcome = Outcomes.Ok,
                Fields = [.. shown.Select(Field)],
                TotalFields = matched.Count,
                FieldsTruncated = shown.Length < matched.Count,
                Functions =
                [
                    .. catalogue.Functions.Select(function => new JqlFunctionOutput
                    {
                        Name = function.Name,
                        Types = function.Types,
                    }),
                ],
            }));
    }

    /// <summary>
    /// What one field accepts, each value written as a clause would write it — Jira quotes what
    /// needs quoting, and the quotes are part of the clause rather than presentation.
    /// </summary>
    public static Rendered Values(JiraJqlSuggestions suggestions)
    {
        var body = new StringBuilder();

        foreach (var value in suggestions.Values)
        {
            body.Append("  ").AppendLine(value);
        }

        return new Rendered(
            UntrustedContent.Envelope(
                $"{suggestions.Field}: {suggestions.Values.Count} values this Jira accepts, each "
                + "as it is written in a clause.",
                body.ToString().TrimEnd()),
            ToolOutputs.Node(new JqlFieldsOutput
            {
                Outcome = Outcomes.Ok,
                Field = suggestions.Field,
                Values = suggestions.Values,
            }));
    }

    /// <summary>
    /// A field Jira published nothing for. Jira answers 200 with an empty list whether the field
    /// is unknown or simply enumerates nothing, so both readings are named here: neither is
    /// something a caller can find out by asking again.
    /// </summary>
    public static Rendered NoValues(JiraJqlSuggestions suggestions) =>
        new(
            $"This Jira publishes no values for '{suggestions.Field}'. It may not be queryable "
            + "under that name, or it may be queryable and enumerate nothing — Jira answers the "
            + "same way for both. Call jira_get_jql_fields with no field for the names this Jira "
            + "is queryable under; a custom field is queryable as cf[NNNNN] or by its quoted "
            + "display name, never as customfield_NNNNN.",
            ToolOutputs.Node(new JqlFieldsOutput
            {
                Outcome = Outcomes.Refused,
                Field = suggestions.Field,
                Values = suggestions.Values,
            }));

    /// <summary>
    /// The fields a substring admits, matched against the JQL name and the custom field's bracket
    /// form alike: a caller narrowing by "10107" is asking about the same field as one narrowing
    /// by "story".
    /// </summary>
    private static IReadOnlyList<JiraJqlField> Matching(
        IReadOnlyList<JiraJqlField> fields,
        string? startsWith) =>
        startsWith is { Length: > 0 } substring
            ? [.. fields.Where(field =>
                field.Name.Contains(substring, StringComparison.OrdinalIgnoreCase)
                || (field.CustomFieldId?.Contains(substring, StringComparison.OrdinalIgnoreCase)
                    ?? false))]
            : fields;

    /// <summary>
    /// One field's line: the name a clause must use, the bracket form where Jira sent one, the
    /// alias where the profile declares one, the types, and the operators. Only a departure from
    /// the default is marked — almost every field is sortable and searchable, and saying so on
    /// each line spends the cap on what the caller already assumes.
    /// </summary>
    private static void Row(StringBuilder body, JiraJqlField field, FieldAliases aliases)
    {
        body.Append("  ").Append(field.Name);

        if (field.CustomFieldId is { Length: > 0 } cfid)
        {
            body.Append("  ").Append(cfid);

            if (Alias(cfid, aliases) is { } alias)
            {
                body.Append("  ").Append(alias);
            }
        }

        body.Append("  ").Append(Types(field.Types))
            .Append("  ").Append(string.Join(", ", field.Operators));

        if (!field.Orderable)
        {
            body.Append("; not sortable");
        }

        if (!field.Searchable)
        {
            body.Append("; not searchable");
        }

        body.AppendLine();
    }

    /// <summary>
    /// The alias label for a custom field, or null where the profile declares none. The join is on
    /// the number, because no part of this payload carries the <c>customfield_10107</c> spelling an
    /// alias resolves to — Jira publishes that number inside the bracket form instead.
    /// </summary>
    private static string? Alias(string customFieldId, FieldAliases aliases)
    {
        var digits = new string([.. customFieldId.Where(char.IsAsciiDigit)]);

        if (digits.Length is 0)
        {
            return null;
        }

        var identifier = "customfield_" + digits;
        var label = aliases.Label(identifier);

        return label == identifier ? null : label;
    }

    /// <summary>
    /// Jira's types as prose: the last dot-segment of each Java class name. The full strings ride
    /// in the structured half, which is the one ADR-0009 keeps intact.
    /// </summary>
    private static string Types(IReadOnlyList<string> types) =>
        string.Join(", ", types.Select(type => type[(type.LastIndexOf('.') + 1)..]));

    private static string FieldsHeading(int shown, int matched, int total, string? startsWith)
    {
        if (shown < matched)
        {
            return $"fields (showing {shown} of {matched}; name a narrower substring, or a field, "
                   + "for the values it takes)";
        }

        return startsWith is { Length: > 0 } substring
            ? $"fields ({matched} of {total} matching '{substring}')"
            : $"fields ({total})";
    }

    private static JqlFieldOutput Field(JiraJqlField field) =>
        new()
        {
            Name = field.Name,
            CustomFieldId = field.CustomFieldId,
            Types = field.Types,
            Operators = field.Operators,
            Orderable = field.Orderable,
            Searchable = field.Searchable,
        };
}

/// <summary>
/// A query catalogue, or the values one field of it enumerates. One shape rather than two,
/// because it is one question asked at two zoom levels: the catalogue half is absent when a field
/// was named, and the suggestion half is absent when none was.
/// </summary>
internal sealed record JqlFieldsOutput : ToolOutput
{
    /// <summary>The fields the prose shows, in the order it shows them.</summary>
    [JsonPropertyName("fields")]
    public IReadOnlyList<JqlFieldOutput>? Fields { get; init; }

    /// <summary>Every queryable field this Jira published, including the ones the cap left out.</summary>
    [JsonPropertyName("totalFields")]
    public int? TotalFields { get; init; }

    /// <summary>
    /// Whether the cap ended the list. There is no position to resume from — Jira's endpoint has
    /// no page — so this is the whole of what a caller can learn about what it did not see.
    /// </summary>
    [JsonPropertyName("fieldsTruncated")]
    public bool? FieldsTruncated { get; init; }

    [JsonPropertyName("functions")]
    public IReadOnlyList<JqlFunctionOutput>? Functions { get; init; }

    /// <summary>The field a caller named, present only where it named one.</summary>
    [JsonPropertyName("field")]
    public string? Field { get; init; }

    /// <summary>
    /// What that field accepts, each value exactly as it is written in a clause — quoted where
    /// Jira quoted it, because the quotes are part of the clause.
    /// </summary>
    [JsonPropertyName("values")]
    public IReadOnlyList<string>? Values { get; init; }
}

/// <summary>
/// One queryable field. <see cref="Name"/> is the JQL name and nothing else will do: for a custom
/// field it is the quoted display name, and <see cref="CustomFieldId"/> is the other name it
/// answers to — never the <c>customfield_10107</c> identifier a write takes.
/// </summary>
internal sealed record JqlFieldOutput
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Jira's <c>cfid</c>, the bracket form <c>cf[10107]</c>. Absent for a system field.</summary>
    [JsonPropertyName("customFieldId")]
    public string? CustomFieldId { get; init; }

    /// <summary>
    /// Jira's own type names, carried in full — the prose shows the last dot-segment, and this is
    /// the half rule 2 wants intact.
    /// </summary>
    [JsonPropertyName("types")]
    public required IReadOnlyList<string> Types { get; init; }

    /// <summary>What this field takes. It differs per field: <c>summary</c> takes <c>~</c>, not <c>=</c>.</summary>
    [JsonPropertyName("operators")]
    public required IReadOnlyList<string> Operators { get; init; }

    [JsonPropertyName("orderable")]
    public required bool Orderable { get; init; }

    [JsonPropertyName("searchable")]
    public required bool Searchable { get; init; }
}

/// <summary>One function this instance publishes, named as it is written in a clause.</summary>
internal sealed record JqlFunctionOutput
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("types")]
    public required IReadOnlyList<string> Types { get; init; }
}
