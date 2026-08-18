using JiraServerMcp.Jira.Capabilities;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;
using System.Globalization;
using System.Net.Http.Json;
using System.Net;
namespace JiraServerMcp.Jira;

/// <summary>
/// The account this profile is authenticated as, and what the instance it is pointed at is:
/// the two questions every other call assumes an answer to.
/// </summary>
public sealed partial class JiraClient
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

    /// <summary>
    /// What this Jira is and what it has: its version, what it calls its deployment, and whether
    /// the software API answers. Two requests, taken together, because they are only ever wanted
    /// together.
    /// </summary>
    public async Task<JiraCapabilities> ProbeCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var serverInfo = await GetAsync<JiraServerInfo>("rest/api/2/serverInfo", cancellationToken)
            .ConfigureAwait(false);

        return new JiraCapabilities(
            serverInfo.Version,
            serverInfo.DeploymentType,
            await IsSoftwareLicensedAsync(cancellationToken).ConfigureAwait(false),
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The instance's own clock, whose offset is the zone a JQL date literal is read in. Asked at
    /// the moment it is needed rather than recorded on the capability probe: an offset is not a
    /// property of the Jira, it is a property of the Jira and the date, and a probe taken before a
    /// daylight-saving change would put every query an hour out.
    /// </summary>
    public async Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken)
    {
        var serverTime = await GetAsync<JiraServerTime>("rest/api/2/serverInfo", cancellationToken)
            .ConfigureAwait(false);

        // Parsed here rather than by the serializer: Jira Server writes the offset as +0200, and
        // System.Text.Json accepts only the +02:00 that ISO 8601-1 requires.
        return DateTimeOffset.TryParse(
            serverTime.ServerTime,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Jira reported its server time as '{serverTime.ServerTime}', which is not a "
                + "timestamp this client can read.");
    }

    /// <summary>
    /// Whether Jira Software is licensed, asked with the smallest page the software API will
    /// serve. The answer is the status code and nothing else: Jira Core's 404 here carries an HTML
    /// body, so reading it would throw where the absence of a licence is the ordinary case.
    /// </summary>
    private async Task<bool> IsSoftwareLicensedAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .GetAsync("rest/agile/1.0/board?maxResults=1", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return false;
        }

        // Anything else that failed — a revoked token, an outage — is a failed probe rather than
        // an instance without Jira Software, and recording it as the latter would hide four tools
        // until someone refreshed the profile.
        await JiraResponse.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        return true;
    }
}
