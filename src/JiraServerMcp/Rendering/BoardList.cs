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
    public static string Render(JiraAgilePage<JiraBoard> page) => AgilePage.Render(page, Line);

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
