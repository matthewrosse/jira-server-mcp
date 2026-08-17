using System.Text;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// One project as text: what it is, which issue types it takes, what statuses each of those can be
/// in, and the components and versions a create call may name. Everything an agent needs before it
/// writes, in the order it will need it.
/// </summary>
internal static class ProjectDetail
{
    public static string Render(JiraProjectDetail project)
    {
        var body = new StringBuilder();

        body.Append(project.Project.Name);

        if (project.Project.ProjectTypeKey is { Length: > 0 } type)
        {
            body.Append(" (").Append(type).Append(')');
        }

        body.Append(" — id ").AppendLine(project.Project.Id);

        if (project.Lead is { Length: > 0 } lead)
        {
            body.Append("lead: ").AppendLine(lead);
        }

        if (project.Description is { Length: > 0 } description)
        {
            body.Append("description: ").AppendLine(Truncation.Body(description));
        }

        IssueTypes(body, project.IssueTypes);
        Components(body, project.Components);
        Versions(body, project.Versions);

        return UntrustedContent.Envelope(project.Project.Key, body.ToString().TrimEnd());
    }

    private static void IssueTypes(StringBuilder body, IReadOnlyList<JiraIssueTypeStatuses> types)
    {
        var shown = types.Take(ResponseBudget.ProjectSectionCap).ToArray();

        body.AppendLine().Append("issue types ")
            .Append(Heading(shown.Length, types.Count)).AppendLine(":");

        foreach (var type in shown)
        {
            body.Append("  ").Append(type.Name).Append(" (id ").Append(type.Id);

            if (type.Subtask)
            {
                body.Append(", sub-task");
            }

            body.Append(')').Append(" — statuses: ")
                .AppendLine(type.Statuses.Count is 0
                    ? "(none)"
                    : string.Join(", ", type.Statuses.Select(status => status.Name)));
        }
    }

    private static void Components(StringBuilder body, IReadOnlyList<JiraProjectComponent> components)
    {
        var shown = components.Take(ResponseBudget.ProjectSectionCap).ToArray();

        body.AppendLine().Append("components ")
            .Append(Heading(shown.Length, components.Count)).AppendLine(":");

        foreach (var component in shown)
        {
            body.Append("  ").Append(component.Name);

            if (component.Description is { Length: > 0 } description)
            {
                body.Append(" — ").Append(Truncation.Body(description));
            }

            body.AppendLine();
        }
    }

    private static void Versions(StringBuilder body, IReadOnlyList<JiraProjectVersion> versions)
    {
        // Jira orders versions oldest first, so a project with a long release history would be cut
        // down to versions released years ago — and the unreleased ones at the end are the only
        // ones a create call would sensibly name.
        IReadOnlyList<JiraProjectVersion> shown = versions.Count > ResponseBudget.ProjectSectionCap
            ? [.. versions.TakeLast(ResponseBudget.ProjectSectionCap)]
            : versions;

        body.AppendLine().Append("versions ")
            .Append(Heading(shown.Count, versions.Count, "most recent")).AppendLine(":");

        foreach (var version in shown)
        {
            body.Append("  ").Append(version.Name).Append(" (")
                .Append(version.Released ? "released" : "unreleased");

            if (version.Archived)
            {
                body.Append(", archived");
            }

            if (version.ReleaseDate is { Length: > 0 } date)
            {
                body.Append(", ").Append(date);
            }

            body.AppendLine(")");
        }
    }

    private static string Heading(int shown, int total, string which = "first") =>
        shown is 0
            ? "(none)"
            : shown < total
                ? $"(showing the {which} {shown} of {total})"
                : $"({total})";
}
