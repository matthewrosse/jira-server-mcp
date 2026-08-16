using System.Text;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// The projects an account can see, one line each: key first, because the key is what every other
/// tool asks for. An orientation call, not a data dump.
/// </summary>
internal static class ProjectList
{
    /// <summary>
    /// The most projects one response is worth. Jira's project endpoint has no page of its own —
    /// it answers with every project at once — so a large instance is cut here or not at all.
    /// </summary>
    public const int Cap = 100;

    public static string Render(IReadOnlyList<JiraProject> projects)
    {
        var shown = projects.Take(Cap).ToArray();
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

        return $"""
            {Header(shown.Length, projects.Count)}
            {UntrustedContent.Preamble}
            {UntrustedContent.Delimit(lines.ToString().TrimEnd())}
            """;
    }

    private static string Header(int shown, int total) =>
        shown < total
            ? $"projects: {total} — showing the first {shown}. Jira's project endpoint has no page "
              + "of its own, so the rest are not available from this tool; a project outside them "
              + "has to be named by its key, which jira_get_project takes directly."
            : $"projects: {total}.";
}
