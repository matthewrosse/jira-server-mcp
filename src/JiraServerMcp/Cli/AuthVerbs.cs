using System.Net;
using JiraServerMcp.Credentials;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Jira.Models;
using JiraServerMcp.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JiraServerMcp.Cli;

/// <summary>
/// `auth login`, `auth status`, `auth logout`. A token is validated against Jira before it is
/// stored, so a rejected one fails here rather than at an agent's first tool call.
/// </summary>
internal static class AuthVerbs
{
    public static async Task<int> LoginAsync(
        string profileName,
        CredentialStoreChoice storeChoice,
        CancellationToken cancellationToken)
    {
        if (Find(profileName) is not { } profile)
        {
            await NoSuchProfileAsync(profileName);

            return 1;
        }

        var token = await ReadTokenAsync(profileName, cancellationToken);

        if (string.IsNullOrEmpty(token))
        {
            await Console.Error.WriteLineAsync(
                "No personal access token was given. Create one in Jira under Profile → "
                + "Personal Access Tokens, then run this again.");

            return 1;
        }

        var user = await ResolveAsync(
            profile,
            token,
            failure => $"{profile.BaseUrl} did not accept that personal access token. "
                       + $"{failure.Message} Nothing was stored.",
            cancellationToken);

        if (user is null)
        {
            return 1;
        }

        var store = await SelectStoreAsync(storeChoice, cancellationToken);

        await store.SetAsync(profileName, token, cancellationToken);

        await Console.Out.WriteLineAsync(
            $"Signed in to {profile.BaseUrl} as {user.DisplayName} ({user.Name}).");
        await Console.Out.WriteLineAsync(
            $"The personal access token for profile '{profileName}' is stored in the "
            + $"{store.Describe()}.");

        // The probe comes last, and its failure does not undo a login: the token is valid — Jira
        // just said so — and losing it because the instance would not describe itself would be a
        // worse answer than a profile with no probe on it yet.
        if (await CapabilityProbe.TakeAsync(profileName, profile, token, cancellationToken)
            is { } capabilities)
        {
            await Console.Out.WriteLineAsync(CapabilityProbe.Describe(capabilities));
        }
        else
        {
            // A login onto a profile that has been probed before leaves that probe in place, so
            // saying there is none would contradict the tools `serve` goes on to register.
            await Console.Error.WriteLineAsync(profile.Capabilities is null
                ? $"Profile '{profileName}' has no capability probe, so the Jira Software tools "
                  + $"will not be registered. Take it again with 'jira-server-mcp profile refresh "
                  + $"{profileName}'."
                : $"The capability probe recorded for profile '{profileName}' is unchanged. Take "
                  + $"it again with 'jira-server-mcp profile refresh {profileName}'.");
        }

        return 0;
    }

    public static async Task<int> StatusAsync(
        string profileName,
        CredentialStoreChoice storeChoice,
        CancellationToken cancellationToken)
    {
        if (Find(profileName) is not { } profile)
        {
            await NoSuchProfileAsync(profileName);

            return 1;
        }

        var store = await SelectStoreAsync(storeChoice, cancellationToken);
        var token = await ProfileToken.ResolveAsync(profileName, store, cancellationToken);

        if (token is not { } held)
        {
            await Console.Error.WriteLineAsync(
                $"No personal access token is stored for profile '{profileName}'. Store one with "
                + $"'jira-server-mcp auth login {profileName}'.");

            return 1;
        }

        var user = await ResolveAsync(
            profile,
            held.Value,
            failure => failure.StatusCode is HttpStatusCode.Unauthorized
                ? $"Credentials for profile '{profileName}' are invalid or revoked. Run "
                  + $"'jira-server-mcp auth login {profileName}'."
                : $"{profile.BaseUrl} refused the stored personal access token. {failure.Message}",
            cancellationToken);

        if (user is null)
        {
            return 1;
        }

        await Console.Out.WriteLineAsync(
            $"Profile '{profileName}' is signed in to {profile.BaseUrl} as {user.DisplayName} "
            + $"({user.Name}).");
        await Console.Out.WriteLineAsync($"The personal access token comes from the {held.Source}.");

        return 0;
    }

    public static async Task<int> LogoutAsync(
        string profileName,
        CredentialStoreChoice storeChoice,
        CancellationToken cancellationToken)
    {
        if (Find(profileName) is null)
        {
            await NoSuchProfileAsync(profileName);

            return 1;
        }

        var store = await SelectStoreAsync(storeChoice, cancellationToken);

        // Read only to tell "removed it" from "there was nothing to remove". The token itself
        // goes no further than this variable.
        var stored = await store.GetAsync(profileName, cancellationToken);

        await store.DeleteAsync(profileName, cancellationToken);

        await Console.Out.WriteLineAsync(stored is { Length: > 0 }
            ? $"Removed the personal access token for profile '{profileName}' from the "
              + $"{store.Describe()}. The profile is still there."
            : $"There was no personal access token stored for profile '{profileName}'.");

        // A profile served from the environment keeps working after a logout, which is worth
        // knowing before wondering why.
        if (TokenEnvironmentVariable.Read(profileName) is not null)
        {
            await Console.Error.WriteLineAsync(
                $"{TokenEnvironmentVariable.NameFor(profileName)} is still set in this "
                + "environment, and a token there is used ahead of the credential store.");
        }

        return 0;
    }

    /// <summary>
    /// Arguments are visible to every process on the machine and land in shell history, so this
    /// is an error with an explanation rather than a warning.
    /// </summary>
    public static async Task<int> RefuseTokenArgumentAsync(string profileName)
    {
        await Console.Error.WriteLineAsync(
            "A personal access token is not accepted as the '--token' argument: arguments are "
            + "visible to every process on the machine and land in shell history.");
        await Console.Error.WriteLineAsync(
            $"Pipe it in instead — 'jira-server-mcp auth login {profileName}' reads standard "
            + "input when it is not a terminal — or, for a container or a CI job, set "
            + $"{TokenEnvironmentVariable.NameFor(profileName)}.");

        return 1;
    }

    private static Profile? Find(string profileName) =>
        ProfileStore.InConfigurationDirectory().Find(profileName);

    private static Task NoSuchProfileAsync(string profileName) =>
        Console.Error.WriteLineAsync(
            $"There is no profile named '{profileName}'. Add it with "
            + $"'jira-server-mcp profile add {profileName} --url <url>'.");

    private static Task<ICredentialStore> SelectStoreAsync(
        CredentialStoreChoice storeChoice,
        CancellationToken cancellationToken) =>
        CredentialStoreSelector.ForThisMachine()
            .SelectAsync(storeChoice, Console.Error, cancellationToken);

    /// <summary>
    /// A terminal is prompted with echo off; anything else — a pipe, a here-string, a CI job —
    /// hands over one line, so setup can be scripted.
    /// </summary>
    private static async Task<string?> ReadTokenAsync(
        string profileName,
        CancellationToken cancellationToken)
    {
        if (Console.IsInputRedirected)
        {
            return (await Console.In.ReadLineAsync(cancellationToken))?.Trim();
        }

        // The prompt goes to standard error: standard output carries what the verb was asked to
        // produce, and on `serve` it carries the protocol itself (ADR-0002).
        await Console.Error.WriteAsync($"Personal access token for profile '{profileName}': ");

        // intercept: true is what keeps the token off the screen.
        var token = NoEchoPrompt.Read(() => Console.ReadKey(intercept: true));

        await Console.Error.WriteLineAsync();

        return token.Trim();
    }

    /// <summary>
    /// The Jira account a token belongs to, or null having already said on standard error why
    /// there is none.
    /// </summary>
    private static async Task<JiraUser?> ResolveAsync(
        Profile profile,
        string token,
        Func<JiraApiException, string> describeRejection,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();

        services.AddSingleton(Options.Create(ConnectedProfile.OptionsFor(profile, token)));
        services.AddJiraClient();

        await using var provider = services.BuildServiceProvider();

        try
        {
            return await provider.GetRequiredService<JiraClient>().GetMyselfAsync(cancellationToken);
        }
        catch (JiraApiException failure)
        {
            await Console.Error.WriteLineAsync(describeRejection(failure));

            return null;
        }
        catch (HttpRequestException failure)
        {
            await Console.Error.WriteLineAsync(
                $"{profile.BaseUrl} could not be reached: {failure.Message}");

            return null;
        }
    }
}
