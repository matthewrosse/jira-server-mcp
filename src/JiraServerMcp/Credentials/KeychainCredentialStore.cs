using System.Text;
using JiraServerMcp.Configuration;

namespace JiraServerMcp.Credentials;

/// <summary>
/// The macOS Keychain, through `security`. Storing goes through `security -i`, which takes its
/// command on standard input: the token is neither an argument any other process can read nor a
/// passphrase prompt. `-w` cannot be used for this — with a controlling terminal present it reads
/// the passphrase from /dev/tty rather than from the pipe it was given, so an interactive
/// `auth login` would hang at a second prompt [verified on macOS 15]. The token travels as hex
/// under `-X`, which has no quoting rules to get wrong.
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
            ["-i"],
            // -U so a second login replaces the entry rather than failing on the duplicate.
            $"add-generic-password -U -s {Quoted(NativeCredentialStore.Service)} "
            + $"-a {Quoted(profileName)} -X {Convert.ToHexString(Encoding.UTF8.GetBytes(personalAccessToken))}\n",
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

    /// <summary>
    /// `security -i` groups an argument with double quotes, and inside those quotes a backslash
    /// escapes the character after it — a literal backslash or a literal double quote. That is
    /// the whole of its quoting [verified on macOS 15]; backslashes are doubled first so the
    /// escapes added for quotes are not escaped in turn.
    /// </summary>
    private static string Quoted(string value) =>
        $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

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
