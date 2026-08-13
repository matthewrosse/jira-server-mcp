using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Authentication;
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

        services
            .AddHttpClient<JiraClient>(ConfigureBaseAddress)
            .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler
            {
                // A redirect would replay the bearer token at whatever host Jira names.
                AllowAutoRedirect = false,
            })
            .AddHttpMessageHandler<PersonalAccessTokenHandler>();

        return services;
    }

    private static void ConfigureBaseAddress(IServiceProvider provider, HttpClient client)
    {
        var baseUrl = provider.GetRequiredService<IOptions<JiraClientOptions>>().Value.BaseUrl
            ?? throw new InvalidOperationException("No Jira base URL is configured.");

        // Relative request URIs only combine with a base address whose path ends in a slash.
        // The slash goes on the path through UriBuilder: appending it to the whole URI would
        // land it in a query string or fragment instead.
        client.BaseAddress = baseUrl.AbsolutePath.EndsWith('/')
            ? baseUrl
            : new UriBuilder(baseUrl) { Path = baseUrl.AbsolutePath + "/" }.Uri;
    }
}
