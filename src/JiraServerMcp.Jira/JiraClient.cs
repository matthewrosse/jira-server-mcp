using System.Net.Http.Json;
using System.Text.Json;
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

        if (!response.IsSuccessStatusCode)
        {
            throw new JiraApiException(
                response.StatusCode,
                await ReadErrorMessagesAsync(response, cancellationToken).ConfigureAwait(false));
        }

        return await response.Content
                   .ReadFromJsonAsync<JiraUser>(cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException(
                   "Jira returned an empty body for /rest/api/2/myself.");
    }

    /// <summary>
    /// Jira reports failures as <c>errorMessages</c>, but a proxy, a login redirect, or an
    /// outage page can answer instead, so an unreadable body is not itself an error.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadErrorMessagesAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var body = await response.Content
                .ReadFromJsonAsync<JsonDocument>(cancellationToken)
                .ConfigureAwait(false);

            if (body?.RootElement.TryGetProperty("errorMessages", out var messages) is not true
                || messages.ValueKind is not JsonValueKind.Array)
            {
                return [];
            }

            return [.. messages.EnumerateArray()
                .Select(message => message.GetString())
                .OfType<string>()];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }
}
