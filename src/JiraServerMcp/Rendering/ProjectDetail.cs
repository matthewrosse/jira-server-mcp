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
    /// <summary>
    /// The most entries any one section is worth. A project that has been running for years has
    /// hundreds of versions, and listing all of them would cost an agent its context to learn
    /// nothing it could not get from the ones it can see.
    /// </summary>
    public const int SectionCap = 50;

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

        return $"""
            {project.Project.Key}
            {UntrustedContent.Preamble}
            {UntrustedContent.Delimit(body.ToString().TrimEnd())}
            """;
    }

    private static void IssueTypes(StringBuilder body, IReadOnlyList<JiraIssueTypeStatuses> types)
    {
        var shown = types.Take(SectionCap).ToArray();

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
        var shown = components.Take(SectionCap).ToArray();

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
        var shown = versions.Take(SectionCap).ToArray();

        body.AppendLine().Append("versions ")
            .Append(Heading(shown.Length, versions.Count)).AppendLine(":");

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

    private static string Heading(int shown, int total) =>
        shown is 0
            ? "(none)"
            : shown < total
                ? $"(showing the first {shown} of {total})"
                : $"({total})";
}
