using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace JiraServerMcp.Jira.Diagnostics;

/// <summary>
/// One line per attempt: method, endpoint, status, elapsed time. Never a header, because one of
/// them is the personal access token, and never a body, because Jira's bodies are the issue
/// content this server exists to keep small.
/// </summary>
public sealed class JiraRequestLoggingHandler(ILogger<JiraRequestLoggingHandler> logger)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var endpoint = request.RequestUri?.AbsolutePath ?? "(unknown endpoint)";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "{Method} {Endpoint} {Status} in {Elapsed} ms",
                request.Method,
                endpoint,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                "{Method} {Endpoint} failed after {Elapsed} ms: {Reason}",
                request.Method,
                endpoint,
                stopwatch.ElapsedMilliseconds,
                exception.HttpRequestError);

            throw;
        }
    }
}
