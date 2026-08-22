using JiraServerMcp.Jira.Capabilities;
using JiraServerMcp.Profiles;

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
        var capabilities = await ConnectedProfile.RunAsync(
            profile,
            token,
            failure => $"{profile.BaseUrl} could not be asked what it is. {failure.Message}",
            whenUnreachable: string.Empty,
            whenTimedOut: ", so it could not be asked what it is",
            client => client.ProbeCapabilitiesAsync(cancellationToken),
            cancellationToken);

        if (capabilities is null)
        {
            return null;
        }

        // Outside the call above, so a configuration directory that cannot be written stays a
        // configuration failure rather than being reported as a fault in talking to Jira.
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
