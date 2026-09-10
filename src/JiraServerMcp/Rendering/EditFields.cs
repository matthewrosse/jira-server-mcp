using System.Text;
using System.Text.Json.Serialization;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Profiles;

namespace JiraServerMcp.Rendering;

/// <summary>
/// The edit screen as text. Every field on it is shown, including the ones Jira will not let a
/// write touch: that a field is on the screen and still not settable is exactly what an agent
/// cannot learn any other way than by being refused.
/// </summary>
internal static class EditFields
{
    public static Rendered Render(JiraEditFields fields, FieldAliases aliases)
    {
        var sections = ScreenFields.Cut(fields.Fields);

        var body = new StringBuilder();

        // "Required" means something else here than on the create screen: an update need not send
        // the field at all, but may not empty it. jira_update_issue documents null as "clears the
        // field", so an agent is otherwise invited at exactly the fields that refuse it.
        ScreenFields.Section(
            body,
            "required (may not be cleared)",
            sections.Required,
            sections.Required.Count,
            aliases);

        ScreenFields.Section(body, "optional", sections.Optional, sections.TotalOptional, aliases);

        return new Rendered(
            $"""
             {fields.Key} — {fields.Fields.Count} fields on the edit screen
             {UntrustedContent.Preamble}
             {UntrustedContent.Delimit(body.ToString().TrimEnd())}
             """,
            ToolOutputs.Node(new EditFieldsOutput
            {
                Outcome = Outcomes.Ok,
                Key = fields.Key,
                Fields = sections.Rows,
                TotalFields = fields.Fields.Count,
                FieldsTruncated = sections.OptionalWasCut,
            }));
    }
}

/// <summary>
/// The edit screen: what an update of one issue may change, and how. Every field on the screen is
/// carried, including the ones Jira will not let a write touch — <c>operations</c> is the half an
/// agent cannot learn any other way than by being refused.
/// </summary>
internal sealed record EditFieldsOutput : ToolOutput
{
    /// <summary>The issue asked for. Jira's edit metadata names no issue, so this is the caller's own key.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    /// <summary>
    /// The fields the prose shows, in the order it shows them: required first, then as many
    /// optional ones as the response budget allows. Both halves are cut together.
    /// </summary>
    [JsonPropertyName("fields")]
    public IReadOnlyList<ScreenFieldOutput>? Fields { get; init; }

    /// <summary>Every field on the edit screen, including the optional ones that were cut.</summary>
    [JsonPropertyName("totalFields")]
    public int? TotalFields { get; init; }

    /// <summary>Whether optional fields were left out. Required fields are never cut.</summary>
    [JsonPropertyName("fieldsTruncated")]
    public bool? FieldsTruncated { get; init; }
}
