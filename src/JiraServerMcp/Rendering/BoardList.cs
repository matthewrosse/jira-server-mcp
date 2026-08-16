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
    public static string Render(JiraAgilePage<JiraBoard> page)
    {
        var lines = new StringBuilder();

        foreach (var board in page.Values)
        {
            lines.Append(board.Id).Append(" | ").Append(Truncation.Body(board.Name));

            if (board.Type is { Length: > 0 } type)
            {
                lines.Append(" | ").Append(type);
            }

            lines.AppendLine();
        }

        return $"""
            {AgilePage.Header(page)}
            {UntrustedContent.Preamble}
            {UntrustedContent.Delimit(lines.ToString().TrimEnd())}
            """;
    }
}
