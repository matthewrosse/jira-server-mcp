using System.Text;
using System.Text.Json.Serialization;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// A page of boards, one line each, the identifier first because every other software API call
/// asks for it.
/// </summary>
internal static class BoardList
{
    public static Rendered Render(JiraAgilePage<JiraBoard> page) =>
        AgilePage.Render(page, Line, (boards, position) => new BoardListOutput
        {
            Outcome = Outcomes.Ok,
            StartAt = position.StartAt,
            Count = position.Count,
            NextStartAt = position.NextStartAt,
            Boards =
            [
                .. boards.Select(board => new BoardRowOutput
                {
                    Id = board.Id,
                    Name = board.Name,
                    Type = board.Type,
                }),
            ],
        });

    private static string Line(JiraBoard board)
    {
        var line = new StringBuilder()
            .Append(board.Id).Append(" | ").Append(Truncation.Body(board.Name));

        if (board.Type is { Length: > 0 } type)
        {
            line.Append(" | ").Append(type);
        }

        return line.ToString();
    }
}

/// <summary>
/// A page from the software API. It carries no total, and not a null one: that API does not report
/// how many rows exist, and a paging field is present only where the server was actually given the
/// number (ADR-0009, as amended). Absence means unknown; zero would mean none.
/// </summary>
internal sealed record BoardListOutput : ToolOutput
{
    [JsonPropertyName("startAt")]
    public int? StartAt { get; init; }

    [JsonPropertyName("count")]
    public int? Count { get; init; }

    /// <summary>Absent when Jira said this was the last page.</summary>
    [JsonPropertyName("nextStartAt")]
    public int? NextStartAt { get; init; }

    [JsonPropertyName("boards")]
    public IReadOnlyList<BoardRowOutput>? Boards { get; init; }
}

/// <summary>
/// One board. The name is a selection label: a board id names nothing, and the name is the only
/// basis an agent has for choosing between rows.
/// </summary>
internal sealed record BoardRowOutput
{
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}
