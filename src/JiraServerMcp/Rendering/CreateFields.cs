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

    /// <summary>
    /// The most optional fields one response is worth. An enterprise create screen carries well
    /// over a hundred, and an agent filing a ticket sets a handful. The required ones are never
    /// cut: a create call fails without every one of them.
    /// </summary>
    public const int OptionalCap = 40;

    public static Rendered Render(JiraCreateFields fields)
    {
        var required = fields.Fields.Where(field => field.Required).ToArray();
        var optional = fields.Fields.Where(field => !field.Required).ToArray();
        var shownOptional = optional.Take(OptionalCap).ToArray();

        var body = new StringBuilder();

        body.Append("issue type: ").AppendLine(fields.IssueTypeName);

        Section(body, "required", required, required.Length);
        Section(body, "optional", shownOptional, optional.Length);

        // The same fields, in the same order, off the same traversal: an agent reading the
        // structure to build a create must see exactly what the prose showed it.
        return new Rendered(
            $"""
             {fields.ProjectKey} — {fields.Fields.Count} fields on the create screen
             {UntrustedContent.Preamble}
             {UntrustedContent.Delimit(body.ToString().TrimEnd())}
             """,
            ToolOutputs.Node(new CreateFieldsOutput
            {
                Outcome = Outcomes.Ok,
                ProjectKey = fields.ProjectKey,
                IssueTypeName = fields.IssueTypeName,
                Fields = [.. required.Concat(shownOptional).Select(Field)],
                TotalFields = fields.Fields.Count,
                FieldsTruncated = shownOptional.Length < optional.Length,
            }));
    }

    /// <summary>
    /// One field, with its allowed values capped as the prose caps them. The values are what a
    /// create must send verbatim, so they are carried rather than described.
    /// </summary>
    private static CreateFieldOutput Field(JiraCreateField field)
    {
        var constrained = field.AllowedValues.Count > 0;

        return new CreateFieldOutput
        {
            Id = field.Id,
            Name = field.Name,
            Required = field.Required,
            Type = field.Type,
            HasAllowedValues = constrained,
            AllowedValues = constrained ? [.. field.AllowedValues.Take(ValueCap)] : null,
            AllowedValuesTruncated = constrained ? field.AllowedValues.Count > ValueCap : null,
        };
    }

    private static void Section(
        StringBuilder body,
        string name,
        IReadOnlyList<JiraCreateField> fields,
        int total)
    {
        body.AppendLine().Append(name).Append(fields.Count < total
            ? $" (showing the first {fields.Count} of {total})"
            : $" ({total})").AppendLine(":");

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
    }

    private static string AllowedValues(IReadOnlyList<string> values)
    {
        var shown = string.Join(", ", values.Take(ValueCap));

        return values.Count > ValueCap
            ? $"{shown} …[{values.Count - ValueCap} more of {values.Count} not shown]"
            : shown;
    }
}
