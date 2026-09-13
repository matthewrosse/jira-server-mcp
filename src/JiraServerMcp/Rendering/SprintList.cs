using System.Text;
using System.Text.Json.Serialization;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// A page of a board's sprints, one line each: identifier, name, state, and the dates where a
/// sprint has them. A future sprint has none, and the line says so by carrying none.
/// </summary>
internal static class SprintList
{
    public static Rendered Render(JiraAgilePage<JiraSprint> page) =>
        AgilePage.Render(page, Line, (sprints, position) => new SprintListOutput
        {
            Outcome = Outcomes.Ok,
            StartAt = position.StartAt,
            Count = position.Count,
            NextStartAt = position.NextStartAt,
            Sprints =
            [
                .. sprints.Select(sprint => new SprintRowOutput
                {
                    Id = sprint.Id,
                    Name = sprint.Name,
                    State = sprint.State,
                }),
            ],
        });

    private static string Line(JiraSprint sprint)
    {
        var line = new StringBuilder()
            .Append(sprint.Id).Append(" | ").Append(Truncation.Body(sprint.Name))
            .Append(" | ").Append(sprint.State);

        if (sprint.StartDate is { Length: > 0 } start)
        {
            line.Append(" | start ").Append(start);
        }

        if (sprint.EndDate is { Length: > 0 } end)
        {
            line.Append(" | end ").Append(end);
        }

        return line.ToString();
    }
}

/// <summary>A page of a board's sprints, paged as <see cref="BoardListOutput"/> is.</summary>
internal sealed record SprintListOutput : ToolOutput
{
    [JsonPropertyName("startAt")]
    public int? StartAt { get; init; }

    [JsonPropertyName("count")]
    public int? Count { get; init; }

    [JsonPropertyName("nextStartAt")]
    public int? NextStartAt { get; init; }

    [JsonPropertyName("sprints")]
    public IReadOnlyList<SprintRowOutput>? Sprints { get; init; }
}

/// <summary>
/// One sprint. <see cref="State"/> answers "which sprint is current", which is the known use; the
/// dates are deliberately absent, because rule 1 would make anything carried a permanent contract
/// over a date format this server does not control and does not normalise.
/// </summary>
internal sealed record SprintRowOutput
{
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }
}
