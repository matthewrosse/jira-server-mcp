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
        var width = profiles.Keys.Max(name => name.Length);

        await Console.Out.WriteLineAsync($"{"NAME".PadRight(width)}  URL");

        foreach (var (name, profile) in profiles.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            await Console.Out.WriteLineAsync($"{name.PadRight(width)}  {profile.BaseUrl}");
        }

        return 0;
    }

    public static async Task<int> RemoveAsync(string name, CancellationToken cancellationToken)
    {
        if (!ProfileStore.InConfigurationDirectory().Remove(name))
        {
            await Console.Error.WriteLineAsync($"There is no profile named '{name}'.");

            return 1;
        }

        // The credential goes with the profile, so removing one leaves no orphaned secret.
        await FileCredentialStore.InConfigurationDirectory().DeleteAsync(name, cancellationToken);

        await Console.Out.WriteLineAsync($"Removed profile '{name}' and its credential.");

        return 0;
    }
}
