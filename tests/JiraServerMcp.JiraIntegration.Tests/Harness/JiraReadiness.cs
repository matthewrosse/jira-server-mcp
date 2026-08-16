using System.Diagnostics;
using System.Text.Json;

namespace JiraServerMcp.JiraIntegration.Tests.Harness;

/// <summary>
/// Waits for the two moments the harness needs: the setup wizard being servable, and — once
/// setup has restarted the instance — the platform API answering.
/// </summary>
/// <remarks>
/// <c>/status</c> is not a readiness signal on its own. The Phase 0 spike measured it flipping to
/// <c>FIRST_RUN</c> roughly a hundred seconds before the web layer served anything, with <c>/</c>
/// answering a redirect to a 503 page in between. Both gates poll <c>/status</c> first and then
/// the thing that is actually about to be used.
/// </remarks>
internal sealed class JiraReadiness(HttpClient client, TimeSpan pollInterval)
{
    public Task WaitForSetupWizardAsync(TimeSpan budget, CancellationToken cancellationToken) =>
        WaitAsync(
            "setup wizard",
            budget,
            // Any state at all means the status endpoint is up; FIRST_RUN is what an unconfigured
            // instance reports, and a re-run against a configured one reports RUNNING.
            state => state is not null,
            // Redirects are followed by the handler, so this is the wizard page's own status.
            "/",
            cancellationToken);

    public Task WaitForPlatformApiAsync(TimeSpan budget, CancellationToken cancellationToken) =>
        WaitAsync(
            "platform API",
            budget,
            state => state is "RUNNING",
            "/rest/api/2/serverInfo",
            cancellationToken);

    private async Task WaitAsync(
        string gate,
        TimeSpan budget,
        Func<string?, bool> statusAccepted,
        string secondGatePath,
        CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        var lastSeen = "no answer at all";
        var statusOpen = false;

        while (elapsed.Elapsed < budget)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!statusOpen)
            {
                var state = await StateAsync(cancellationToken);
                lastSeen = "/status reported " + (state ?? "nothing");
                statusOpen = statusAccepted(state);
            }

            if (statusOpen)
            {
                var (reached, description) = await ProbeAsync(secondGatePath, cancellationToken);
                lastSeen = description;

                if (reached)
                {
                    return;
                }
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        throw new TimeoutException(
            $"Jira's {gate} was not ready within {budget.TotalSeconds:F0}s. Last seen: {lastSeen}.");
    }

    private async Task<string?> StateAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync("/status", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var body = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));

            return body.RootElement.TryGetProperty("state", out var state)
                ? state.GetString()
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            // The port not listening, or an HTML error page where JSON was expected. Both mean
            // the instance is still starting.
            return null;
        }
    }

    private async Task<(bool Reached, string Description)> ProbeAsync(
        string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(path, cancellationToken);

            return ((int)response.StatusCode is 200,
                $"{path} answered {(int)response.StatusCode}");
        }
        catch (HttpRequestException exception)
        {
            return (false, $"{path} could not be reached: {exception.Message}");
        }
    }
}
