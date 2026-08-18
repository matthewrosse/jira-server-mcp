using System.Net.Http.Json;
using System.Text.Json;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;
namespace JiraServerMcp.Jira;

/// <summary>
/// Projects and the metadata a create is built from: what exists, and what a create screen will
/// accept for it.
/// </summary>
public sealed partial class JiraClient
{
    /// <summary>
    /// Every project this account can see. Jira answers with all of them at once — the platform API
    /// offers no page here — so the caller decides what to do with a very large instance.
    /// </summary>
    public Task<IReadOnlyList<JiraProject>> ListProjectsAsync(CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<JiraProject>>("rest/api/2/project", cancellationToken);

    /// <summary>
    /// One project with its issue types, their statuses, its components, and its versions. Jira
    /// keeps them behind four endpoints; an agent preparing a write needs all four, and four tool
    /// calls to collect them is exactly the mechanical REST mapping this server is not.
    /// </summary>
    public async Task<JiraProjectDetail> GetProjectAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var path = $"rest/api/2/project/{Uri.EscapeDataString(key)}";

        var project = await GetAsync<JiraProjectResponse>(path, cancellationToken)
            .ConfigureAwait(false);

        var issueTypes = await GetAsync<IReadOnlyList<JiraIssueTypeStatuses>>(
            $"{path}/statuses",
            cancellationToken).ConfigureAwait(false);

        var components = await GetAsync<IReadOnlyList<JiraProjectComponent>>(
            $"{path}/components",
            cancellationToken).ConfigureAwait(false);

        var versions = await GetAsync<IReadOnlyList<JiraProjectVersion>>(
            $"{path}/versions",
            cancellationToken).ConfigureAwait(false);

        return new JiraProjectDetail(
            Project: new JiraProject(
                project.Key,
                project.Name,
                project.Id,
                project.ProjectTypeKey),
            Description: project.Description,
            Lead: project.Lead?.DisplayName,
            IssueTypes: issueTypes,
            Components: components,
            Versions: versions);
    }

    /// <summary>
    /// What Jira will accept when an issue of one type is created in one project, or null when it
    /// knows neither the project nor the type.
    /// </summary>
    public async Task<JiraCreateFields?> GetCreateFieldsAsync(
        string projectKey,
        string issueTypeName,
        CancellationToken cancellationToken)
    {
        var query = "rest/api/2/issue/createmeta"
                    + $"?projectKeys={Uri.EscapeDataString(projectKey)}"
                    + $"&issuetypeNames={Uri.EscapeDataString(issueTypeName)}"
                    + "&expand=projects.issuetypes.fields";

        using var response = await httpClient
            .GetAsync(query, cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        using var document = await response.Content
                                 .ReadFromJsonAsync<JsonDocument>(cancellationToken)
                                 .ConfigureAwait(false)
                             ?? throw new InvalidOperationException(
                                 "Jira returned an empty body for /rest/api/2/issue/createmeta.");

        return CreateFieldsReader.Read(document.RootElement);
    }
}
