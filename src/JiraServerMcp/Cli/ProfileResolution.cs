using JiraServerMcp.Credentials;
using JiraServerMcp.Profiles;

namespace JiraServerMcp.Cli;

/// <summary>
/// The profile and token this run will use, or the reason there are none. Owning the sequence in
/// one place keeps the operator-facing wording for a missing profile, or a missing token, from
/// drifting between verbs. The exit code for "there are none" stays with the caller: 1 where that
/// is the verb's answer, 2 where this installation cannot do it at all.
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

    /// <summary>
    /// The named profile and its token, or null having already explained on standard error which
    /// of the two is missing. <paramref name="whenNoToken"/> is the clause naming what the caller
    /// therefore did not do, appended to the shared missing-token sentence — required rather than
    /// defaulted to empty, so a verb added tomorrow cannot silently say nothing.
    /// </summary>
    public static async Task<ResolvedProfile?> ResolveAsync(
        string profileName,
        CredentialStoreChoice storeChoice,
        string whenNoToken,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(profileName) is not { } profile)
        {
            return null;
        }

        var store = await SelectStoreAsync(storeChoice, cancellationToken);

        if (await ProfileToken.ResolveAsync(profileName, store, cancellationToken) is not { } token)
        {
            await Console.Error.WriteLineAsync(
                $"No personal access token is stored for profile '{profileName}'{whenNoToken}. "
                + $"Store one with 'jira-server-mcp auth login {profileName}'.");

            return null;
        }

        return new ResolvedProfile(profile, token);
    }
}

/// <summary>
/// The profile and the personal access token this run will use, taken together. See "Connected
/// profile" for what this becomes once a client is built from it.
/// </summary>
internal sealed record ResolvedProfile(Profile Profile, ProfileToken Token);
