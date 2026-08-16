using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Capabilities;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Profiles;
using Microsoft.Extensions.DependencyInjection;

namespace JiraServerMcp.Cli;

/// <summary>
/// Taking the capability probe and recording it on the profile. It runs at `auth login` and at
/// `profile refresh`, and nowhere else: `serve` reads what is recorded and asks Jira nothing.
/// </summary>
internal static class CapabilityProbe
{
    /// <summary>
    /// Probes the instance and stores the result on the profile, or returns null having already
    /// said on standard error why there is none. The caller adds what that means for it, because
    /// a failed probe during a login and a failed refresh call for different next steps.
    /// </summary>
    public static async Task<JiraCapabilities?> TakeAsync(
        string profileName,
        Profile profile,
        string token,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();

        services.Configure<JiraClientOptions>(options =>
        {
            options.BaseUrl = profile.BaseUrl;
            options.PersonalAccessToken = token;
            options.CaBundlePath = profile.CaBundlePath;
        });

        services.AddJiraClient();

        await using var provider = services.BuildServiceProvider();

        JiraCapabilities capabilities;

        try
        {
            capabilities = await provider.GetRequiredService<JiraClient>()
                .ProbeCapabilitiesAsync(cancellationToken);
        }
        catch (JiraApiException failure)
        {
            await Console.Error.WriteLineAsync(
                $"{profile.BaseUrl} could not be asked what it is. {failure.Message}");

            return null;
        }
        catch (HttpRequestException failure)
        {
            await Console.Error.WriteLineAsync(
                $"{profile.BaseUrl} could not be reached: {failure.Message}");

            return null;
        }

        ProfileStore.InConfigurationDirectory().Add(profileName, profile with
        {
            Capabilities = capabilities,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        return capabilities;
    }

    /// <summary>
    /// What a probe found, in the one line an operator wants back.
    /// </summary>
    public static string Describe(JiraCapabilities capabilities) =>
        $"Jira {capabilities.Version} ({capabilities.DeploymentType}), "
        + (capabilities.SoftwareLicensed
            ? "with Jira Software: the board, sprint and backlog tools are registered."
            : "without Jira Software: the board, sprint and backlog tools are not registered.");
}
