using JiraServerMcp.Credentials;
using JiraServerMcp.Profiles;

namespace JiraServerMcp.Cli;

/// <summary>
/// The two lookups every CLI verb needs before it can do anything: the named profile, and the
/// credential store for this run. Owning them in one place keeps the operator-facing wording for
/// a missing profile from drifting between verbs.
/// </summary>
internal static class ProfileResolution
{
    /// <summary>
    /// The named profile, or null having already explained on standard error how to add one.
    /// </summary>
    public static async Task<Profile?> FindAsync(string profileName)
    {
        var profile = ProfileStore.InConfigurationDirectory().Find(profileName);

        if (profile is null)
        {
            await Console.Error.WriteLineAsync(
                $"There is no profile named '{profileName}'. Add it with "
                + $"'jira-server-mcp profile add {profileName} --url <url>'.");
        }

        return profile;
    }

    public static Task<ICredentialStore> SelectStoreAsync(
        CredentialStoreChoice storeChoice,
        CancellationToken cancellationToken) =>
        CredentialStoreSelector.ForThisMachine()
            .SelectAsync(storeChoice, Console.Error, cancellationToken);
}
