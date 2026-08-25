using System.Text;
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
