using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using JiraServerMcp.Credentials;
using JiraServerMcp.Profiles;

namespace JiraServerMcp.Cli;

/// <summary>
/// `profile add`, `profile list`, `profile remove`. Operator-facing results go to standard
/// output; failures go to standard error.
/// </summary>
internal static class ProfileVerbs
{
    public static async Task<int> AddAsync(
        string name,
        string url,
        string? caBundlePath,
        CancellationToken cancellationToken)
    {
        if (!ProfileUrl.TryParse(url, out var baseUrl, out var error))
        {
            await Console.Error.WriteLineAsync(error);

            return 1;
        }

        if (caBundlePath is not null && !File.Exists(caBundlePath))
        {
            await Console.Error.WriteLineAsync(
                $"There is no certificate authority bundle at '{caBundlePath}'.");

            return 1;
        }

        // A file with no certificate in it would become an empty trust store, and every handshake
        // would then fail for a reason that never mentions this file.
        if (caBundlePath is not null && !HoldsACertificate(caBundlePath))
        {
            await Console.Error.WriteLineAsync(
                $"The certificate authority bundle at '{caBundlePath}' holds no certificate. A "
                + "bundle is one or more PEM CERTIFICATE blocks.");

            return 1;
        }

        var store = ProfileStore.InConfigurationDirectory();

        if (store.Find(name) is not null)
        {
            await Console.Error.WriteLineAsync(
                $"Profile '{name}' already exists. Remove it first with "
                + $"'jira-server-mcp profile remove {name}'.");

            return 1;
        }

        var now = DateTimeOffset.UtcNow;

        store.Add(name, new Profile
        {
            // The absolute form, so what is stored and listed is what was actually resolved
            // rather than whatever shorthand was typed.
            BaseUrl = new Uri(baseUrl.AbsoluteUri),
            CaBundlePath = caBundlePath,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await Console.Out.WriteLineAsync($"Added profile '{name}' for {baseUrl}.");
        await Console.Out.WriteLineAsync(
            $"Store a personal access token with 'jira-server-mcp auth login {name}'.");

        return 0;
    }

    public static async Task<int> ListAsync(CancellationToken cancellationToken)
    {
        var profiles = ProfileStore.InConfigurationDirectory().All();

        if (profiles.Count == 0)
        {
            await Console.Out.WriteLineAsync(
                "No profiles yet. Add one with 'jira-server-mcp profile add <name> --url <url>'.");

            return 0;
        }

        // Names and URLs only: nothing a profile holds is secret, and nothing secret is read.
        var width = Math.Max("NAME".Length, profiles.Keys.Max(name => name.Length));

        await Console.Out.WriteLineAsync($"{"NAME".PadRight(width)}  URL");

        foreach (var (name, profile) in profiles.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            await Console.Out.WriteLineAsync($"{name.PadRight(width)}  {profile.BaseUrl}");
        }

        return 0;
    }

    /// <summary>
    /// Takes the capability probe again. The recorded one expires after seven days, and a Jira
    /// that has just been licensed for Jira Software — or has just lost that licence — is a
    /// reason not to wait for it to.
    /// </summary>
    public static async Task<int> RefreshAsync(
        string name,
        CredentialStoreChoice storeChoice,
        CancellationToken cancellationToken)
    {
        if (await ProfileResolution.FindAsync(name) is not { } profile)
        {
            return 1;
        }

        var credentials = await ProfileResolution.SelectStoreAsync(storeChoice, cancellationToken);

        var token = await ProfileToken.ResolveAsync(name, credentials, cancellationToken);

        if (token is not { } held)
        {
            await Console.Error.WriteLineAsync(
                $"No personal access token is stored for profile '{name}', and the capability "
                + $"probe is taken as the Jira user. Store one with 'jira-server-mcp auth login "
                + $"{name}'.");

            return 1;
        }

        var capabilities = await CapabilityProbe.TakeAsync(
            name,
            profile,
            held.Value,
            cancellationToken);

        if (capabilities is null)
        {
            // Nothing was written, so whatever was recorded before is still the best answer there
            // is — an unreachable instance is not evidence that it lost Jira Software.
            await Console.Error.WriteLineAsync(
                $"The capability probe recorded for profile '{name}' is unchanged.");

            return 1;
        }

        await Console.Out.WriteLineAsync(
            $"Refreshed the capability probe for profile '{name}': "
            + CapabilityProbe.Describe(capabilities));

        return 0;
    }

    public static async Task<int> RemoveAsync(
        string name,
        CredentialStoreChoice storeChoice,
        CancellationToken cancellationToken)
    {
        if (await ProfileResolution.FindAsync(name) is null)
        {
            return 1;
        }

        // The credential goes first: a failure here leaves a profile that still names the token
        // it owns, rather than a secret on disk that no verb can reach any more.
        var credentials = await ProfileResolution.SelectStoreAsync(storeChoice, cancellationToken);

        await credentials.DeleteAsync(name, cancellationToken);

        ProfileStore.InConfigurationDirectory().Remove(name);

        await Console.Out.WriteLineAsync($"Removed profile '{name}' and its credential.");

        return 0;
    }

    /// <summary>
    /// `profile alias set`. Declaring the same alias twice replaces it: an operator correcting a
    /// field identifier should not have to remove the alias first.
    /// </summary>
    public static async Task<int> SetAliasAsync(
        string name,
        string alias,
        string field,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (await ProfileResolution.FindAsync(name) is not { } profile)
        {
            return 1;
        }

        if (!FieldAliases.IsDeclarable(alias))
        {
            await Console.Error.WriteLineAsync(
                $"'{alias}' cannot be an alias: it is spelled like a Jira field identifier, which "
                + "would leave the two ambiguous. Choose the name you want to read and write, "
                + "such as 'story_points'.");

            return 1;
        }

        if (field.Trim().Length is 0)
        {
            await Console.Error.WriteLineAsync(
                $"'{alias}' was given no field to stand for. Name the identifier Jira uses, such "
                + "as 'customfield_10010'.");

            return 1;
        }

        var aliases = new Dictionary<string, string>(
            profile.FieldAliases ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase)
        {
            [alias.Trim()] = field.Trim(),
        };

        Store(name, profile, aliases);

        await Console.Out.WriteLineAsync(
            $"Profile '{name}' now reads and writes '{alias.Trim()}' as '{field.Trim()}'.");

        return 0;
    }

    /// <summary>`profile alias list`.</summary>
    public static async Task<int> ListAliasesAsync(string name, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (await ProfileResolution.FindAsync(name) is not { } profile)
        {
            return 1;
        }

        var aliases = FieldAliases.For(profile.FieldAliases);

        if (!aliases.Any)
        {
            await Console.Out.WriteLineAsync(
                $"Profile '{name}' declares no field aliases. Add one with 'jira-server-mcp "
                + $"profile alias set {name} story_points customfield_10010'.");

            return 0;
        }

        foreach (var alias in aliases.Known)
        {
            await Console.Out.WriteLineAsync(alias);
        }

        return 0;
    }

    /// <summary>`profile alias remove`.</summary>
    public static async Task<int> RemoveAliasAsync(
        string name,
        string alias,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (await ProfileResolution.FindAsync(name) is not { } profile)
        {
            return 1;
        }

        var aliases = new Dictionary<string, string>(
            profile.FieldAliases ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        if (!aliases.Remove(alias.Trim()))
        {
            await Console.Error.WriteLineAsync(
                $"Profile '{name}' declares no alias '{alias.Trim()}'. List what it does declare "
                + $"with 'jira-server-mcp profile alias list {name}'.");

            return 1;
        }

        Store(name, profile, aliases);

        await Console.Out.WriteLineAsync($"Profile '{name}' no longer declares '{alias.Trim()}'.");

        return 0;
    }

    private static void Store(
        string name,
        Profile profile,
        IReadOnlyDictionary<string, string> aliases) =>
        ProfileStore.InConfigurationDirectory().Add(
            name,
            profile with
            {
                FieldAliases = aliases,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

    private static bool HoldsACertificate(string caBundlePath)
    {
        var roots = new X509Certificate2Collection();

        try
        {
            roots.ImportFromPemFile(caBundlePath);
        }
        catch (CryptographicException)
        {
            return false;
        }

        return roots.Count > 0;
    }
}
