using System.Text;
using System.Text.Json.Serialization;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Profiles;

namespace JiraServerMcp.Rendering;

/// <summary>
/// The section-writing the create screen and the edit screen share. A screen is one subject with
/// two operations behind it, so what a field looks like on the page is decided once.
/// </summary>
internal static class ScreenFields
{
    /// <summary>
    /// The most allowed values one field is worth. A cascading select or a long component list
    /// runs to hundreds, and the rest are one more call away.
    /// </summary>
    public const int ValueCap = 20;

    /// <summary>
    /// The most optional fields one response is worth. An enterprise screen carries well over a
    /// hundred, and an agent preparing a write touches a handful. The required ones are never cut.
    /// </summary>
    public const int OptionalCap = 40;

    /// <summary>
    /// A screen split the way both renderers show it: required fields, then as many optional ones
    /// as the budget allows, with what the cut left out still counted.
    /// </summary>
    public static ScreenSections Cut(IReadOnlyList<JiraScreenField> fields)
    {
        var optional = fields.Where(field => !field.Required).ToArray();

        return new ScreenSections(
            Required: [.. fields.Where(field => field.Required)],
            Optional: [.. optional.Take(OptionalCap)],
            TotalOptional: optional.Length);
    }

    /// <summary>
    /// One section of a screen — its fields, each led by the identifier a write must send, with
    /// the alias beside it where the profile declares one.
    /// </summary>
    public static void Section(
        StringBuilder body,
        string name,
        IReadOnlyList<JiraScreenField> fields,
        int total,
        FieldAliases aliases)
    {
        body.AppendLine().Append(name).Append(fields.Count < total
            ? $" (showing the first {fields.Count} of {total})"
            : $" ({total})").AppendLine(":");

        foreach (var field in fields)
        {
            body.Append("  ").Append(aliases.Label(field.Id))
                .Append(" (").Append(field.Name).Append(')');

            if (field.Type is { Length: > 0 } type)
            {
                body.Append(" — ").Append(type);
            }

            if (field.AllowedValues.Count > 0)
            {
                body.Append("; allowed: ").Append(AllowedValues(field.AllowedValues));
            }

            if (Operations(field) is { } operations)
            {
                body.Append(operations);
            }

            body.AppendLine();
        }
    }

    /// <summary>
    /// What may be done to the field, and where a field the write tools cannot touch is served
    /// instead. A field is on the screen whether or not any tool here writes it, so the row that
    /// says "not writable" is exactly the row an agent needs pointing somewhere.
    /// </summary>
    private static string? Operations(JiraScreenField field)
    {
        var operations = Operations(field.Operations);
        var served = Served(field.Id);

        return (operations, served) switch
        {
            (null, null) => null,
            (null, _) => $"; {served}",
            (_, null) => operations,
            _ => $"{operations} — {served}",
        };
    }

    /// <summary>
    /// The tool that writes a field neither screen's write tool can. Prose rather than a key of
    /// its own: what a caller does with it is call another tool, not branch on it.
    /// </summary>
    private static string? Served(string field) => field.ToLowerInvariant() switch
    {
        // issuetype is deliberately absent: nothing here makes it writable, and its bare "not
        // writable" is the whole truth.
        "issuelinks" => "links are made with jira_link_issues",
        "attachment" => "files are attached with jira_add_attachment",
        "comment" => "comments are added with jira_add_comment",
        _ => null,
    };

    /// <summary>
    /// What may be done to the field, or null where saying so is worth nothing. Almost every field
    /// on either screen accepts <c>set</c> and only <c>set</c>, and printing that on every line
    /// spends response budget saying what the caller already assumes. A field Jira said nothing
    /// about is also silent here: absence is not a claim that the field cannot be written.
    /// </summary>
    private static string? Operations(IReadOnlyList<string>? operations)
    {
        if (operations is null)
        {
            return null;
        }

        if (operations.Count is 0)
        {
            return "; not writable";
        }

        if (operations is ["set"])
        {
            return null;
        }

        return operations.Contains("set", StringComparer.Ordinal)
            ? $"; operations: {string.Join(", ", operations)}"
            : $"; {string.Join("/", operations)} only";
    }

    /// <summary>
    /// One field's structured half, with its allowed values capped as the prose caps them. The
    /// values are what a write must send verbatim, so they are carried rather than described.
    /// </summary>
    public static ScreenFieldOutput Field(JiraScreenField field)
    {
        var constrained = field.AllowedValues.Count > 0;

        return new ScreenFieldOutput
        {
            Id = field.Id,
            Name = field.Name,
            Required = field.Required,
            Type = field.Type,
            Operations = field.Operations,
            HasAllowedValues = constrained,
            AllowedValues = constrained ? [.. field.AllowedValues.Take(ValueCap)] : null,
            AllowedValuesTruncated = constrained
                ? field.AllowedValues.Count > ValueCap
                : null,
        };
    }

    private static string AllowedValues(IReadOnlyList<string> values)
    {
        var shown = string.Join(", ", values.Take(ValueCap));

        return values.Count > ValueCap
            ? $"{shown} …[{values.Count - ValueCap} more of {values.Count} not shown]"
            : shown;
    }
}

/// <summary>
/// One screen, cut to what a response carries: every required field, and the optional ones the
/// budget left room for.
/// </summary>
internal sealed record ScreenSections(
    IReadOnlyList<JiraScreenField> Required,
    IReadOnlyList<JiraScreenField> Optional,
    int TotalOptional)
{
    /// <summary>Whether optional fields were left out. Required fields are never cut.</summary>
    public bool OptionalWasCut => Optional.Count < TotalOptional;

    /// <summary>
    /// The same fields, in the same order, off the same traversal: an agent reading the structure
    /// to build a write must see exactly what the prose showed it.
    /// </summary>
    public IReadOnlyList<ScreenFieldOutput> Rows => [.. Required.Concat(Optional).Select(ScreenFields.Field)];
}

/// <summary>
/// One field on a screen. The name is a selection label under ADR-0009's amended rule 2:
/// <c>customfield_10010</c> tells an agent nothing, and the name is what makes the identifier it
/// must send actionable.
/// </summary>
internal sealed record ScreenFieldOutput
{
    /// <summary>What a write must send. For a custom field, nothing else will do.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("required")]
    public required bool Required { get; init; }

    /// <summary>
    /// Jira's own <c>schema.type</c>, passed through unchanged and absent where Jira sent none.
    /// Normalising it into a vocabulary this server owns would mean maintaining a mapping across
    /// every Jira Server version, and a mistranslation is worse than an unfamiliar string: an
    /// agent can match an unfamiliar string against the prose, but cannot detect a wrong one.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// Whether Jira constrains this field to a list. Kept beside <see cref="AllowedValues"/> so
    /// that "constrained, but the list was cut" stays distinguishable from "unconstrained".
    /// </summary>
    [JsonPropertyName("hasAllowedValues")]
    public required bool HasAllowedValues { get; init; }

    [JsonPropertyName("allowedValues")]
    public IReadOnlyList<string>? AllowedValues { get; init; }

    [JsonPropertyName("allowedValuesTruncated")]
    public bool? AllowedValuesTruncated { get; init; }

    /// <summary>
    /// What Jira says may be done to this field — <c>set</c>, <c>add</c>, <c>remove</c> — carried
    /// verbatim under rule 2 of ADR-0009, which admits a value Jira itself enumerates. An empty
    /// list is a real answer: the field is on the screen and cannot be written at all. Absent
    /// where Jira said nothing, which is the different claim ADR-0009 forbids collapsing into it.
    /// </summary>
    [JsonPropertyName("operations")]
    public IReadOnlyList<string>? Operations { get; init; }
}
