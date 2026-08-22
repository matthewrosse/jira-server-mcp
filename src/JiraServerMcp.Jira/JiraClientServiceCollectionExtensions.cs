using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Authentication;
using JiraServerMcp.Jira.Diagnostics;
using JiraServerMcp.Jira.Resilience;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class JiraClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="JiraClient"/> as a typed client on <c>IHttpClientFactory</c>: the
    /// retry, logging, and bearer handlers in that order, redirects disabled, HTTPS enforced, and
    /// the whole call bounded by a timeout.
    /// </summary>
    public static IServiceCollection AddJiraClient(this IServiceCollection services)
    {
        services.AddTransient<PersonalAccessTokenHandler>();
        services.AddTransient<JiraRetryHandler>();
        services.AddTransient<JiraRequestLoggingHandler>();

        services
            .AddHttpClient<JiraClient>(ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler)
            // The factory's own logging writes every request and response header at trace level,
            // and one of those headers is the personal access token.
            .RemoveAllLoggers()
            // Outermost, so a retried read is authenticated afresh on every attempt and every
            // attempt is logged in its own right.
            .AddHttpMessageHandler<JiraRetryHandler>()
            .AddHttpMessageHandler<JiraRequestLoggingHandler>()
            .AddHttpMessageHandler<PersonalAccessTokenHandler>();

        return services;
    }

    private static HttpMessageHandler CreatePrimaryHandler(IServiceProvider provider)
    {
        var handler = new HttpClientHandler
        {
            // A redirect would replay the bearer token at whatever host Jira names.
            AllowAutoRedirect = false,
        };

        if (provider.GetRequiredService<IOptions<JiraClientOptions>>().Value.CaBundlePath
            is { } caBundlePath)
        {
            handler.ServerCertificateCustomValidationCallback =
                PrivateCertificateAuthority.TrustingBundleAt(caBundlePath);
        }

        return handler;
    }

    private static void ConfigureClient(IServiceProvider provider, HttpClient client)
    {
        var options = provider.GetRequiredService<IOptions<JiraClientOptions>>().Value;

        var baseUrl = options.BaseUrl
            ?? throw new InvalidOperationException("No Jira base URL is configured.");

        // The token is a bearer secret with nothing else protecting it in transit. Loopback is
        // the only exception, because there is no network there to intercept.
        if (baseUrl.Scheme is not "https" && !baseUrl.IsLoopback)
        {
            throw new InvalidOperationException(
                $"The Jira base URL '{baseUrl}' does not use HTTPS. The base URL must use HTTPS, "
                + "except for a loopback address such as http://localhost or http://127.0.0.1.");
        }

        // The whole call, retries included, is bounded here: a hung Jira must not hold an agent's
        // tool call open indefinitely.
        client.Timeout = options.Timeout;

        // Relative request URIs only combine with a base address whose path ends in a slash.
        // The slash goes on the path through UriBuilder: appending it to the whole URI would
        // land it in a query string or fragment instead.
        client.BaseAddress = baseUrl.AbsolutePath.EndsWith('/')
            ? baseUrl
            : new UriBuilder(baseUrl) { Path = baseUrl.AbsolutePath + "/" }.Uri;
    }
}
