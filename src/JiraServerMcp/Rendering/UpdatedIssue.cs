using System.Text.Json;
using System.Text.Json.Serialization;
using JiraServerMcp.Profiles;

namespace JiraServerMcp.Rendering;

/// <summary>
/// What was changed, named as the caller would recognise it. The prose labels an aliased field
/// with both names, so an agent that wrote "story_points" can match the answer to its own
/// request, and says which of a field's operations carried the change; the structured half
/// carries the identifiers alone, which is what rule 2 of ADR-0009 admits and what a follow-up
/// call must send. The operation is left out of it deliberately: it was the caller's own
/// argument, which the caller already holds.
/// </summary>
internal static class UpdatedIssue
{
    public static Rendered Render(
        string key,
        IReadOnlyDictionary<string, JsonElement>? fields,
        IReadOnlyDictionary<string, JsonElement> added,
        IReadOnlyDictionary<string, JsonElement> removed,
        string? assignee,
        FieldAliases aliases)
    {
        var changed = new List<string>();
        var named = new List<string>();

        void Note(IEnumerable<string> operated, string? how)
        {
            foreach (var field in operated)
            {
                if (!changed.Contains(field, StringComparer.Ordinal))
                {
                    changed.Add(field);
                }

                named.Add(how is null ? aliases.Label(field) : $"{aliases.Label(field)} ({how})");
            }
        }

        Note(fields?.Keys ?? [], null);
        Note(added.Keys, "added");
        Note(removed.Keys, "removed");

        if (assignee is not null)
        {
            var how = assignee.Length is 0 ? "assignee (cleared)" : "assignee";

            changed.Add(how);
            named.Add(how);
        }

        return new Rendered(
            $"Updated {key}: {string.Join(", ", named)}.",
            ToolOutputs.Node(new UpdatedIssueOutput
            {
                Outcome = Outcomes.Ok,
                Key = key,
                Changed = changed,
            }));
    }
}

/// <summary><see cref="Changed"/> is the field ids the server sent, as the prose names them.</summary>
internal sealed record UpdatedIssueOutput : ToolOutput
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("changed")]
    public IReadOnlyList<string>? Changed { get; init; }
}
