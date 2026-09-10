using System.Text;
using System.Text.Json.Serialization;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Profiles;

namespace JiraServerMcp.Rendering;

/// <summary>
/// The create screen as text, required fields first. A create call fails on a required custom
/// field named only by its identifier, so the identifier is what each line leads with.
/// </summary>
internal static class CreateFields
{
    public static Rendered Render(JiraCreateFields fields, FieldAliases aliases)
    {
        var sections = ScreenFields.Cut(fields.Fields);

        var body = new StringBuilder();

        body.Append("issue type: ").AppendLine(fields.IssueTypeName);

        ScreenFields.Section(
            body,
            "required",
            sections.Required,
            sections.Required.Count,
            aliases);

        ScreenFields.Section(body, "optional", sections.Optional, sections.TotalOptional, aliases);

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
                Fields = sections.Rows,
                TotalFields = fields.Fields.Count,
                FieldsTruncated = sections.OptionalWasCut,
            }));
    }
}

/// <summary>
/// The create screen: what a create call must send, and what each field will accept. The most
/// machine-shaped answer this server gives — an agent reads it to build its next call, and every
/// value in it is one that call must send verbatim.
/// </summary>
internal sealed record CreateFieldsOutput : ToolOutput
{
    [JsonPropertyName("projectKey")]
    public string? ProjectKey { get; init; }

    [JsonPropertyName("issueTypeName")]
    public string? IssueTypeName { get; init; }

    /// <summary>
    /// The fields the prose shows, in the order it shows them: required first, then as many
    /// optional ones as the response budget allows. Both halves are cut together.
    /// </summary>
    [JsonPropertyName("fields")]
    public IReadOnlyList<ScreenFieldOutput>? Fields { get; init; }

    /// <summary>Every field on the create screen, including the optional ones that were cut.</summary>
    [JsonPropertyName("totalFields")]
    public int? TotalFields { get; init; }

    /// <summary>
    /// Whether optional fields were left out. Without it, a field's absence from
    /// <see cref="Fields"/> could mean "not on this screen" or "cut", which is the confusion
    /// <c>hasAllowedValues</c> exists to prevent one level down. Required fields are never cut —
    /// a create fails without every one of them.
    /// </summary>
    [JsonPropertyName("fieldsTruncated")]
    public bool? FieldsTruncated { get; init; }
}
