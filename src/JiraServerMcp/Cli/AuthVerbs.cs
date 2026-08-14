using JiraServerMcp.Credentials;
using JiraServerMcp.Profiles;

namespace JiraServerMcp.Cli;

/// <summary>
/// `auth login` in its non-interactive form: one line of standard input, stored against the
/// profile. The no-echo prompt, the validation call to Jira, and the native credential stores
/// arrive with the rest of the auth verbs.
/// </summary>
internal static class AuthVerbs
{
    public static async Task<int> LoginAsync(string profileName, CancellationToken cancellationToken)
    {
        if (ProfileStore.InConfigurationDirectory().Find(profileName) is null)
        {
            await Console.Error.WriteLineAsync(
                $"There is no profile named '{profileName}'. Add it with "
                + $"'jira-server-mcp profile add {profileName} --url <url>'.");

            return 1;
        }

        var token = (await Console.In.ReadLineAsync(cancellationToken))?.Trim();

        if (string.IsNullOrEmpty(token))
        {
            await Console.Error.WriteLineAsync(
                "No personal access token was given. Pipe one in on standard input.");

            return 1;
        }

        var store = FileCredentialStore.InConfigurationDirectory();

        await store.SetAsync(profileName, token, cancellationToken);

        await Console.Out.WriteLineAsync(
            $"Stored a personal access token for profile '{profileName}' in the {store.Describe()}.");

        return 0;
    }
}
