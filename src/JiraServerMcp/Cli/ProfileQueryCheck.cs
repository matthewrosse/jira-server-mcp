using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Profiles;
using JiraServerMcp.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JiraServerMcp.Cli;

/// <summary>
/// Running a declared query against Jira once, before it is stored. A query Jira will not accept
/// is refused here, in front of the human who wrote it, rather than months later in front of an
/// agent that can do nothing about it.
/// </summary>
internal static class ProfileQueryCheck
{
    public static async Task<bool> RunsAsync(
        Profile profile,
        string token,
        string jql,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();

        services.AddSingleton(Options.Create(ConnectedProfile.OptionsFor(profile, token)));
        services.AddJiraClient();

        await using var provider = services.BuildServiceProvider();

        try
        {
            // The smallest page Jira will serve: what is being checked is whether the query parses
            // and resolves, not what it currently matches.
            await provider.GetRequiredService<JiraClient>()
                .SearchAsync(jql, 0, 1, FieldProjection.Default, cancellationToken);

            return true;
        }
        catch (JiraApiException failure)
        {
            await Console.Error.WriteLineAsync(
                $"{profile.BaseUrl} would not run that query, so it was not stored. "
                + $"{failure.Message}");

            return false;
        }
        catch (HttpRequestException failure)
        {
            await Console.Error.WriteLineAsync(
                $"{profile.BaseUrl} could not be reached, so the query was not checked and not "
                + $"stored: {failure.Message}");

            return false;
        }
    }
}
