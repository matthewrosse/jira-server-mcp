using System.Text;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// A page of a board's sprints, one line each: identifier, name, state, and the dates where a
/// sprint has them. A future sprint has none, and the line says so by carrying none.
/// </summary>
internal static class SprintList
{
    public static string Render(JiraAgilePage<JiraSprint> page) => AgilePage.Render(page, Line);

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
