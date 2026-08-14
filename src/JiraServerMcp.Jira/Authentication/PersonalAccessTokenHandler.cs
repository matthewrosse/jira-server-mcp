using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace JiraServerMcp.Jira.Authentication;

/// <summary>
/// Adds the personal access token as a bearer credential (ADR-0001) and nothing else.
/// </summary>
public sealed class PersonalAccessTokenHandler(IOptions<JiraClientOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            options.Value.PersonalAccessToken);

        return base.SendAsync(request, cancellationToken);
    }
}
