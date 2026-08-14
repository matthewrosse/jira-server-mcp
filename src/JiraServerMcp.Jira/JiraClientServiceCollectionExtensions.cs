using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Authentication;
using JiraServerMcp.Jira.Resilience;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class JiraClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="JiraClient"/> as a typed client on <c>IHttpClientFactory</c>, with
    /// redirects disabled and the bearer handler in the chain.
    /// </summary>
    public static IServiceCollection AddJiraClient(this IServiceCollection services)
    {
        services.AddTransient<PersonalAccessTokenHandler>();
        services.AddTransient<JiraRetryHandler>();

        services
            .AddHttpClient<JiraClient>(ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler
            {
                // A redirect would replay the bearer token at whatever host Jira names.
                AllowAutoRedirect = false,
            })
            // Outermost, so a retried read is authenticated afresh on every attempt.
            .AddHttpMessageHandler<JiraRetryHandler>()
            .AddHttpMessageHandler<PersonalAccessTokenHandler>();

        return services;
    }

    private static void ConfigureClient(IServiceProvider provider, HttpClient client)
    {
        var baseUrl = provider.GetRequiredService<IOptions<JiraClientOptions>>().Value.BaseUrl
            ?? throw new InvalidOperationException("No Jira base URL is configured.");

        // The whole call, retries included, is bounded here: a hung Jira must not hold an agent's
        // tool call open indefinitely.
        client.Timeout = TimeSpan.FromSeconds(30);

        // Relative request URIs only combine with a base address whose path ends in a slash.
        // The slash goes on the path through UriBuilder: appending it to the whole URI would
        // land it in a query string or fragment instead.
        client.BaseAddress = baseUrl.AbsolutePath.EndsWith('/')
            ? baseUrl
            : new UriBuilder(baseUrl) { Path = baseUrl.AbsolutePath + "/" }.Uri;
    }
}
