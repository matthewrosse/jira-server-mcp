using System.Globalization;
using JiraServerMcp.Configuration;
using JiraServerMcp.Jira;
using JiraServerMcp.Jira.Errors;
using JiraServerMcp.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JiraServerMcp.Cli;

/// <summary>
/// A profile and its personal access token taken together as a working Jira client: the client is
/// built, used and disposed here, and what happened when using it failed is said here too. A verb
/// that wants something from Jira hands over the call and the clause naming what it therefore did
/// not do; it never assembles a client of its own, which is how one verb came to be missing a
/// timeout arm that its two siblings had.
/// </summary>
internal static class ConnectedProfile
{
    /// <summary>
    /// The mapping from a profile and a personal access token to a configured Jira client: base
    /// URL, token, certificate authority bundle path, and how long a call may take. Public for
    /// `serve`, whose client lives for the process rather than for one call.
    /// </summary>
    public static JiraClientOptions OptionsFor(Profile profile, string token) => new()
    {
        BaseUrl = profile.BaseUrl,
        PersonalAccessToken = token,
        CaBundlePath = profile.CaBundlePath,
        Timeout = TimeoutFromEnvironment(),
    };

    /// <summary>
    /// Runs one call against a client for this profile, or returns null having already said on
    /// standard error what happened. The base URL and the shape of the sentence come from here;
    /// what did not happen comes from the caller, because only the caller knows it. The exit code
    /// is the caller's too.
    /// </summary>
    /// <remarks>
    /// <paramref name="work"/> is the Jira call and nothing else. Anything else inside it — a
    /// write to the profile store, say — would be reported by the last arm as a fault in this
    /// tool, which is the wrong vocabulary for a disk that is full.
    /// </remarks>
    public static async Task<T?> RunAsync<T>(
        Profile profile,
        string token,
        Func<JiraApiException, string> describeApiFailure,
        string whenUnreachable,
        string whenTimedOut,
        Func<JiraClient, Task<T>> work,
        CancellationToken cancellationToken)
        where T : class
    {
        var services = new ServiceCollection();

        services.AddSingleton(Options.Create(OptionsFor(profile, token)));
        services.AddJiraClient();

        await using var provider = services.BuildServiceProvider();

        try
        {
            return await work(provider.GetRequiredService<JiraClient>());
        }
        catch (JiraApiException failure)
        {
            await Console.Error.WriteLineAsync(describeApiFailure(failure));
        }
        catch (HttpRequestException failure)
        {
            await Console.Error.WriteLineAsync(
                $"{profile.BaseUrl} could not be reached{whenUnreachable}: {failure.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The client's own timeout, which arrives as a cancellation nobody asked for. A Jira
            // that hangs — a laptop off the VPN, an address that black-holes — is a call that did
            // not happen, not a crash in front of the operator.
            await Console.Error.WriteLineAsync(
                $"{profile.BaseUrl} did not answer in time{whenTimedOut}.");
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Something no arm above expected — a captive portal answering 200 with a login page,
            // say. Left alone it reaches the operator as a stack trace, which says nothing about
            // whose fault it is. Naming it costs one arm and makes the crash reportable.
            await Console.Error.WriteLineAsync(
                $"jira-server-mcp failed while talking to {profile.BaseUrl}, which is a fault in "
                + $"this tool rather than in Jira: {failure.GetType().Name}: {failure.Message}");
        }

        return null;
    }

    /// <summary>
    /// Deliberately undocumented in the README, whose environment section is about credentials:
    /// the sentence below is the whole of what an operator who sets it needs. A typo must not
    /// quietly retune the server's timeout, so anything unreadable is refused rather than ignored.
    /// </summary>
    private const string TimeoutVariable = "JIRA_SERVER_MCP__TIMEOUT_SECONDS";

    private static TimeSpan TimeoutFromEnvironment()
    {
        var configured = Environment.GetEnvironmentVariable(TimeoutVariable);

        if (string.IsNullOrWhiteSpace(configured))
        {
            return TimeSpan.FromSeconds(30);
        }

        // The upper bound is HttpClient's own: a longer timeout is refused here, with the
        // variable named, rather than thrown from the client on the first call that uses it.
        const int longest = int.MaxValue / 1000;

        if (!int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds is <= 0 or > longest)
        {
            throw new ConfigurationException(
                $"{TimeoutVariable} is '{configured}', which is not a whole number of seconds "
                + $"between 1 and {longest}. Set it to one, such as 30, or unset it to use the "
                + "default of 30 seconds.");
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
