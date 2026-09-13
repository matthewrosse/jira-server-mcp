using System.Text;
using System.Text.Json.Serialization;
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

/// <summary>
/// The projects an account can see. Jira's project endpoint has no page of its own, so what bounds
/// this is a cap rather than a position, and there is nothing to resume from.
/// </summary>
internal sealed record ProjectListOutput : ToolOutput
{
    /// <summary>
    /// The rows in <see cref="Projects"/>, as the page output means it: what is carried here, not
    /// what Jira has. <see cref="TotalCount"/> is the second number.
    /// </summary>
    [JsonPropertyName("count")]
    public int? Count { get; init; }

    /// <summary>Every project Jira answered with, including the ones the cap left out.</summary>
    [JsonPropertyName("totalCount")]
    public int? TotalCount { get; init; }

    /// <summary>
    /// Whether the cap left projects out. There is no next page to ask for: a project outside the
    /// cap is reached by naming its key, or by narrowing with a search.
    /// </summary>
    [JsonPropertyName("cutByCap")]
    public bool? CutByCap { get; init; }

    [JsonPropertyName("projects")]
    public IReadOnlyList<ProjectRowOutput>? Projects { get; init; }
}

/// <summary>
/// One project as a row. The key leads because the key is what every other tool takes as input,
/// and an agent listing projects is almost always looking for one to pass on.
/// </summary>
internal sealed record ProjectRowOutput
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
