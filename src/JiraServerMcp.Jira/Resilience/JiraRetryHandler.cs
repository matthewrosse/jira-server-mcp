using System.Net;

namespace JiraServerMcp.Jira.Resilience;

/// <summary>
/// The retry half of the resilience pipeline, hand-built rather than taken from
/// <c>AddStandardResilienceHandler</c>: that one retries every HTTP method, including POST, which
/// would silently create the same issue twice (dotnet/extensions#5248).
/// </summary>
/// <remarks>
/// There is no circuit breaker. This is a single-user tool, and a breaker would only add a second
/// confusing failure mode on top of the one Jira already produced.
/// </remarks>
public sealed class JiraRetryHandler : DelegatingHandler
{
    private const int Attempts = 3;

    private const int FirstBackoffMilliseconds = 250;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Only a read is safe to repeat. Everything else is surfaced the first time it fails.
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        for (var attempt = 1; ; attempt++)
        {
            var lastAttempt = attempt == Attempts;

            HttpResponseMessage response;

            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (!lastAttempt)
            {
                await Task.Delay(Backoff(attempt, null), cancellationToken).ConfigureAwait(false);

                continue;
            }

            if (lastAttempt || !IsWorthRetrying(response.StatusCode))
            {
                return response;
            }

            var backoff = Backoff(attempt, RetryAfter(response));

            response.Dispose();

            await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsWorthRetrying(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        || (int)status >= 500;

    /// <summary>
    /// Exponential backoff with jitter, so a Jira coming back up is not hit by every retry at
    /// once. Jira's own <c>Retry-After</c> wins when it sends one: it knows more than we do.
    /// </summary>
    private static TimeSpan Backoff(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } asked)
        {
            return asked;
        }

        var backoff = TimeSpan.FromMilliseconds(
            FirstBackoffMilliseconds * Math.Pow(2, attempt - 1));

        return backoff + backoff * Random.Shared.NextDouble() * 0.5;
    }

    /// <summary>
    /// <c>Retry-After</c> comes in a delta-seconds form and an HTTP-date form, and Jira sends
    /// either. A date already in the past means "now".
    /// </summary>
    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;

            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }
}
