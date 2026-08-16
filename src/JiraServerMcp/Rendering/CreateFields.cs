using System.Text;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// The create screen as text, required fields first. A create call fails on a required custom
/// field named only by its identifier, so the identifier is what each line leads with.
/// </summary>
internal static class CreateFields
{
    /// <summary>
    /// The most allowed values one field is worth. A cascading select or a long component list
    /// runs to hundreds, and the rest are one more call away.
    /// </summary>
    public const int ValueCap = 20;

    public static string Render(JiraCreateFields fields)
    {
        var required = fields.Fields.Where(field => field.Required).ToArray();
        var optional = fields.Fields.Where(field => !field.Required).ToArray();

        var body = new StringBuilder();

        Section(body, "required", required);
        Section(body, "optional", optional);

        return $"""
            {fields.ProjectKey} / {fields.IssueTypeName} — {fields.Fields.Count} fields on the create screen
            {UntrustedContent.Preamble}
            {UntrustedContent.Delimit(body.ToString().TrimEnd())}
            """;
    }

    private static void Section(StringBuilder body, string name, IReadOnlyList<JiraCreateField> fields)
    {
        body.Append(name).Append(" (").Append(fields.Count).AppendLine("):");

        foreach (var field in fields)
        {
            body.Append("  ").Append(field.Id).Append(" (").Append(field.Name).Append(')');

            if (field.Type is { Length: > 0 } type)
            {
                body.Append(" — ").Append(type);
            }

            if (field.AllowedValues.Count > 0)
            {
                body.Append("; allowed: ").Append(AllowedValues(field.AllowedValues));
            }

            body.AppendLine();
        }

        body.AppendLine();
    }

    private static string AllowedValues(IReadOnlyList<string> values)
    {
        var shown = string.Join(", ", values.Take(ValueCap));

        return values.Count > ValueCap
            ? $"{shown} …[{values.Count - ValueCap} more of {values.Count} not shown]"
            : shown;
    }
}
