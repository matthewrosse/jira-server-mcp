using System.Net.Http.Json;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;

namespace JiraServerMcp.Jira;

/// <summary>
/// Typed client over the Jira Server platform API.
/// </summary>
public sealed class JiraClient(HttpClient httpClient)
{
    /// <summary>
    /// The Jira account the configured personal access token belongs to.
    /// </summary>
    public async Task<JiraUser> GetMyselfAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .GetAsync("rest/api/2/myself", cancellationToken)
            .ConfigureAwait(false);

        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        return await response.Content
                   .ReadFromJsonAsync<JiraUser>(cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException(
                   "Jira returned an empty body for /rest/api/2/myself.");
    }
}
