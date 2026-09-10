using System.Text.Json.Serialization;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// An issue Jira created, named by the key and id Jira gave it and the project the caller named.
/// </summary>
internal static class CreatedIssue
{
    public static Rendered Render(string projectKey, JiraCreatedIssue created) =>
        new(
            $"Created {created.Key} (id {created.Id}) in {projectKey}.",
            ToolOutputs.Node(new CreatedIssueOutput
            {
                Outcome = Outcomes.Ok,
                Key = created.Key,
                Id = created.Id,
                ProjectKey = projectKey,
            }));
}

/// <summary>A created issue: what the caller needs to read it back or to say what it made.</summary>
internal sealed record CreatedIssueOutput : ToolOutput
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("projectKey")]
    public string? ProjectKey { get; init; }
}
