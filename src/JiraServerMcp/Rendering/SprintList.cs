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
    public static string Render(JiraAgilePage<JiraSprint> page)
    {
        var lines = new StringBuilder();

        foreach (var sprint in page.Values)
        {
            lines.Append(sprint.Id).Append(" | ").Append(Truncation.Body(sprint.Name))
                .Append(" | ").Append(sprint.State);

            if (sprint.StartDate is { Length: > 0 } start)
            {
                lines.Append(" | start ").Append(start);
            }

            if (sprint.EndDate is { Length: > 0 } end)
            {
                lines.Append(" | end ").Append(end);
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
