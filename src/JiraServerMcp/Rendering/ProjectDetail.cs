using System.Text;
using System.Text.Json.Serialization;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Rendering;

/// <summary>
/// One project as text: what it is, which issue types it takes, what statuses each of those can be
/// in, and the components and versions a create call may name. Everything an agent needs before it
/// writes, in the order it will need it.
/// </summary>
internal static class ProjectDetail
{
    public static Rendered Render(JiraProjectDetail project)
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

        var issueTypes = IssueTypes(body, project.IssueTypes);
        var components = Components(body, project.Components);
        var versions = Versions(body, project.Versions);

        return new Rendered(
            UntrustedContent.Envelope(project.Project.Key, body.ToString().TrimEnd()),
            ToolOutputs.Node(new ProjectDetailOutput
            {
                Outcome = Outcomes.Ok,
                Key = project.Project.Key,
                Id = project.Project.Id,
                Name = project.Project.Name,
                IssueTypeNames = [.. issueTypes.Select(type => type.Name)],
                IssueTypeCount = project.IssueTypes.Count,
                IssueTypesTruncated = issueTypes.Count < project.IssueTypes.Count,
                VersionNames = [.. versions.Select(version => version.Name)],
                VersionCount = project.Versions.Count,
                VersionsTruncated = versions.Count < project.Versions.Count,
                ComponentNames = [.. components.Select(component => component.Name)],
                ComponentCount = project.Components.Count,
                ComponentsTruncated = components.Count < project.Components.Count,
            }));
    }

    /// <summary>
    /// The section, and the entries it showed — which is what the structured half carries, so the
    /// two halves cannot cut differently.
    /// </summary>
    private static IReadOnlyList<JiraIssueTypeStatuses> IssueTypes(
        StringBuilder body,
        IReadOnlyList<JiraIssueTypeStatuses> types)
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

        return shown;
    }

    private static IReadOnlyList<JiraProjectComponent> Components(
        StringBuilder body,
        IReadOnlyList<JiraProjectComponent> components)
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

        return shown;
    }

    private static IReadOnlyList<JiraProjectVersion> Versions(
        StringBuilder body,
        IReadOnlyList<JiraProjectVersion> versions)
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

        return shown;
    }

    private static string Heading(int shown, int total, string which = "first") =>
        shown is 0
            ? "(none)"
            : shown < total
                ? $"(showing the {which} {shown} of {total})"
                : $"({total})";
}

/// <summary>
/// One project: what a create call may name in it. The names are selection labels under ADR-0009's
/// amended rule 2 — a version name is what <c>fixVersions</c> must be given verbatim, and its id
/// is opaque.
/// </summary>
/// <remarks>
/// The project lead is deliberately absent. It is a username, which rule 2 admits on its face, but
/// nothing branches on it — and rule 1 makes carrying a field permanent while leaving it out stays
/// reversible. The description is prose and is not carried at all.
/// </remarks>
internal sealed record ProjectDetailOutput : ToolOutput
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("issueTypeNames")]
    public IReadOnlyList<string>? IssueTypeNames { get; init; }

    [JsonPropertyName("issueTypeCount")]
    public int? IssueTypeCount { get; init; }

    [JsonPropertyName("issueTypesTruncated")]
    public bool? IssueTypesTruncated { get; init; }

    /// <summary>
    /// The most recent versions, which are the ones a create would name — Jira orders them oldest
    /// first, so a cut taken from the front would carry only releases from years ago.
    /// </summary>
    [JsonPropertyName("versionNames")]
    public IReadOnlyList<string>? VersionNames { get; init; }

    [JsonPropertyName("versionCount")]
    public int? VersionCount { get; init; }

    [JsonPropertyName("versionsTruncated")]
    public bool? VersionsTruncated { get; init; }

    [JsonPropertyName("componentNames")]
    public IReadOnlyList<string>? ComponentNames { get; init; }

    [JsonPropertyName("componentCount")]
    public int? ComponentCount { get; init; }

    [JsonPropertyName("componentsTruncated")]
    public bool? ComponentsTruncated { get; init; }
}
