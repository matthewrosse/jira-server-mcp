using System.Text.Json.Serialization;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// Work Jira logged, with the duration as Jira recorded it rather than as the caller wrote it.
/// </summary>
internal static class AddedWorklog
{
    public static Rendered Render(string key, JiraAddedWorklog logged) =>
        new(
            $"Logged {logged.TimeSpent} against {key} as worklog {logged.Id}.",
            ToolOutputs.Node(new AddedWorklogOutput
            {
                Outcome = Outcomes.Ok,
                Key = key,
                WorklogId = logged.Id,
                TimeSpent = logged.TimeSpent,
            }));
}

/// <summary>
/// <see cref="TimeSpent"/> is the duration as Jira recorded it, which is what says how it read the
/// duration it was given.
/// </summary>
internal sealed record AddedWorklogOutput : ToolOutput
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("worklogId")]
    public string? WorklogId { get; init; }

    [JsonPropertyName("timeSpent")]
    public string? TimeSpent { get; init; }
}
