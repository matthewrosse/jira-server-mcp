using System.Text.Json.Serialization;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// A workflow names its transitions, so the confirmation carries Jira-authored text and is
/// framed as such (ADR-0005's grant is the bound on what a transition can do; the framing is
/// the bound on what its name can talk the model into).
/// </summary>
/// <remarks>
/// The structured half carries the transition id and the status name, the second only where
/// Jira reported a destination — matching the prose's conditional "now in:" line rather than
/// promising a field the prose does not have. A status name is admin-authored and so is
/// untrusted in provenance, but it is a value Jira enumerates rather than prose someone typed,
/// which is where ADR-0009 draws the line.
/// </remarks>
internal static class TransitionedIssue
{
    public static Rendered Render(string key, JiraTransition matched) =>
        new(
            UntrustedContent.Envelope(
                $"Transitioned {key}. The transition and the status it led to are named below.",
                matched.ToStatus is { } status
                    ? $"transition: {matched.Name}\nnow in: {status}"
                    : $"transition: {matched.Name}"),
            ToolOutputs.Node(new TransitionedIssueOutput
            {
                Outcome = Outcomes.Ok,
                Key = key,
                TransitionId = matched.Id,
                Status = matched.ToStatus,
            }));
}

/// <summary>
/// <see cref="Status"/> appears only where Jira reported the destination, matching the prose's
/// conditional "now in:" line.
/// </summary>
internal sealed record TransitionedIssueOutput : ToolOutput
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("transitionId")]
    public string? TransitionId { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}
