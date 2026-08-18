using System.Text;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// The projects an account can see, one line each: key first, because the key is what every other
/// tool asks for. An orientation call, not a data dump.
/// </summary>
internal static class ProjectList
{
    public static Rendered Render(IReadOnlyList<JiraProject> projects)
    {
        var shown = projects.Take(ResponseBudget.ProjectListCap).ToArray();
        var lines = new StringBuilder();

        foreach (var project in shown)
        {
            lines.Append(project.Key).Append(" | ").Append(Truncation.Body(project.Name))
                .Append(" | id ").Append(project.Id);

            if (project.ProjectTypeKey is { Length: > 0 } type)
            {
                lines.Append(" | ").Append(type);
            }

            lines.AppendLine();
        }

        // The rows the cap admitted are the rows the structure carries, off the one traversal.
        return new Rendered(
            UntrustedContent.Envelope(
                Header(shown.Length, projects.Count),
                lines.ToString().TrimEnd()),
            ToolOutputs.Node(new ProjectListOutput
            {
                Outcome = Outcomes.Ok,
                Count = shown.Length,
                TotalCount = projects.Count,
                CutByCap = shown.Length < projects.Count,
                Projects =
                [
                    .. shown.Select(project => new ProjectRowOutput
                    {
                        Key = project.Key,
                        Id = project.Id,
                        Name = project.Name,
                    }),
                ],
            }));
    }

    private static string Header(int shown, int total) =>
        shown < total
            ? $"projects: {total} — showing the first {shown}. Jira's project endpoint has no page "
              + "of its own, so the rest are not available from this tool; a project outside them "
              + "has to be named by its key, which jira_get_project takes directly."
            : $"projects: {total}.";
}
