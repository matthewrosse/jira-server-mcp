using System.Text;
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
