using System.Text.Json.Serialization;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// The structured half carries the phrase and the type name both. They are different strings —
/// "is blocked by" is stored under <c>Blocks</c> — and each answers a question the other
/// cannot: the phrase is what a repeat call would send and what reads as English, the type name
/// is what the issue panel and Jira's own payloads say. Carrying the identifier beside the
/// enumerated name is what the issue row already does with <c>statusId</c> and <c>status</c>.
/// The two keys are the caller's, unswapped: the phrase decided the direction (ADR-0010), so
/// reporting the ends the way Jira slots them would hand back a sentence nobody wrote.
/// </summary>
internal static class LinkedIssues
{
    public static Rendered Render(
        string from,
        string to,
        string relation,
        JiraIssueLinkType type) =>
        new(
            $"Linked {from} to {to}: {from} {relation.Trim()} {to}.",
            ToolOutputs.Node(new LinkedIssuesOutput
            {
                Outcome = Outcomes.Ok,
                From = from,
                To = to,
                Relation = relation.Trim(),
                TypeName = type.Name,
            }));
}

/// <summary>
/// A link between two issues. Both ends are named as the caller named them, because the phrase is
/// what decided the direction (ADR-0010) and reversing them here would hand back a sentence the
/// caller did not write.
/// </summary>
/// <remarks>
/// <see cref="Relation"/> is the phrase, trimmed, and <see cref="TypeName"/> is the type Jira
/// stored it under. They are not the same string and neither substitutes for the other: the phrase
/// is what reads as English and what a repeat call would send, while the type name is what the
/// issue panel and Jira's own payloads say. This is the pattern the issue row already follows with
/// <c>statusId</c> beside <c>status</c> — the identifier and the enumerated name, both carried.
/// </remarks>
internal sealed record LinkedIssuesOutput : ToolOutput
{
    /// <summary>The issue the relation is about — "PROJ-1" in "PROJ-1 blocks PROJ-2".</summary>
    [JsonPropertyName("from")]
    public string? From { get; init; }

    /// <summary>The issue on the other end.</summary>
    [JsonPropertyName("to")]
    public string? To { get; init; }

    [JsonPropertyName("relation")]
    public string? Relation { get; init; }

    [JsonPropertyName("typeName")]
    public string? TypeName { get; init; }
}
