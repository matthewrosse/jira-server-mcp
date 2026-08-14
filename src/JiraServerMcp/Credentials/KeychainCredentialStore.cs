using JiraServerMcp.Configuration;

namespace JiraServerMcp.Credentials;

/// <summary>
/// The macOS Keychain, through `security`. The token goes in on standard input — twice, because
/// `add-generic-password -w` asks for it and then asks again to confirm — so it never reaches an
/// argument list any other process on the machine can read.
/// </summary>
internal sealed class KeychainCredentialStore(IProcessRunner processRunner) : INativeCredentialStore
{
    /// <summary>
    /// `security` answers 44 for an item that is not there, on lookup and on delete alike.
    /// </summary>
    private const int ItemNotFound = 44;

    public async Task<string?> GetAsync(string profileName, CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            ["find-generic-password", "-s", NativeCredentialStore.Service, "-a", profileName, "-w"],
            standardInput: null,
            cancellationToken);

        if (result.ExitCode is ItemNotFound)
        {
            return null;
        }

        Ensure(result, $"read the credential for profile '{profileName}'");

        // The token itself carries no newline; `security` adds one when it prints.
        return result.StandardOutput.TrimEnd('\n');
    }

    public async Task SetAsync(
        string profileName,
        string personalAccessToken,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            // -U so a second login replaces the entry rather than failing on the duplicate.
            ["add-generic-password", "-U", "-s", NativeCredentialStore.Service, "-a", profileName, "-w"],
            $"{personalAccessToken}\n{personalAccessToken}\n",
            cancellationToken);

        Ensure(result, $"store the credential for profile '{profileName}'");
    }

    public async Task DeleteAsync(string profileName, CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            ["delete-generic-password", "-s", NativeCredentialStore.Service, "-a", profileName],
            standardInput: null,
            cancellationToken);

        if (result.ExitCode is ItemNotFound)
        {
            return;
        }

        Ensure(result, $"remove the credential for profile '{profileName}'");
    }

    public string Describe() => $"macOS keychain, under the service '{NativeCredentialStore.Service}'";

    public async Task<bool> IsUsableAsync(CancellationToken cancellationToken)
    {
        // Listing the keychains touches the same search list a lookup does, and asks for no
        // secret, so an unlocked-keychain prompt cannot be provoked by the probe itself.
        var result = await RunAsync(["list-keychains"], standardInput: null, cancellationToken);

        return result is { Started: true, ExitCode: 0 };
    }

    private Task<ProcessResult> RunAsync(
        string[] arguments,
        string? standardInput,
        CancellationToken cancellationToken) =>
        processRunner.RunAsync("security", arguments, standardInput, cancellationToken);

    private static void Ensure(ProcessResult result, string attempt)
    {
        if (result is { Started: true, ExitCode: 0 })
        {
            return;
        }

        var reason = result.Started
            ? result.StandardError.Trim()
            : "the 'security' tool is not installed";

        throw new ConfigurationException(
            $"The macOS keychain could not {attempt}: {reason}. Use "
            + "'--credential-store file' to keep credentials in an encrypted file instead.");
    }
}
