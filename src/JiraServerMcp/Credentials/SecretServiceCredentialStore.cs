using JiraServerMcp.Configuration;

namespace JiraServerMcp.Credentials;

/// <summary>
/// The Secret Service on Linux, through `secret-tool`. Exit code 1 means both "no such secret"
/// and "nothing on this session bus provides org.freedesktop.secrets" — measured, see
/// tests/README.md finding 6 — so the two are told apart by standard error: silence is a missing
/// credential, anything else is a missing backend.
/// </summary>
internal sealed class SecretServiceCredentialStore(IProcessRunner processRunner) : INativeCredentialStore
{
    /// <summary>
    /// Looked up to see whether the Secret Service answers at all. Nothing is stored under this
    /// name, and a lookup creates nothing, so the probe leaves the keyring as it found it.
    /// </summary>
    private const string ProbeAccount = "__probe__";

    public async Task<string?> GetAsync(string profileName, CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            ["lookup", .. Attributes(profileName)],
            standardInput: null,
            cancellationToken);

        if (result is { Started: true, ExitCode: 0 })
        {
            // Byte for byte, with no newline of its own — unlike `security`, which adds one when
            // it prints, so the Keychain store has to trim and this one must not.
            return result.StandardOutput;
        }

        if (IsAbsence(result))
        {
            return null;
        }

        throw Unreachable(result, $"read the credential for profile '{profileName}'");
    }

    public async Task SetAsync(
        string profileName,
        string personalAccessToken,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            [
                "store",
                $"--label=jira-server-mcp ({profileName})",
                .. Attributes(profileName),
            ],
            // Every byte of standard input becomes the secret, a trailing newline included.
            personalAccessToken,
            cancellationToken);

        if (result is not { Started: true, ExitCode: 0 })
        {
            throw Unreachable(result, $"store the credential for profile '{profileName}'");
        }
    }

    public async Task DeleteAsync(string profileName, CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            ["clear", .. Attributes(profileName)],
            standardInput: null,
            cancellationToken);

        if (result is { Started: true, ExitCode: 0 } || IsAbsence(result))
        {
            return;
        }

        throw Unreachable(result, $"remove the credential for profile '{profileName}'");
    }

    public string Describe() =>
        $"Linux Secret Service, under the service '{NativeCredentialStore.Service}'";

    public async Task<bool> IsUsableAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            ["lookup", .. Attributes(ProbeAccount)],
            standardInput: null,
            cancellationToken);

        // The probe secret does not exist, so a reachable Secret Service answers exactly the way
        // a missing credential does: non-zero, and nothing on standard error.
        return result.Started && (result.ExitCode is 0 || IsAbsence(result));
    }

    private static string[] Attributes(string profileName) =>
        ["service", NativeCredentialStore.Service, "account", profileName];

    private static bool IsAbsence(ProcessResult result) =>
        result.Started && string.IsNullOrWhiteSpace(result.StandardError);

    private Task<ProcessResult> RunAsync(
        string[] arguments,
        string? standardInput,
        CancellationToken cancellationToken) =>
        processRunner.RunAsync("secret-tool", arguments, standardInput, cancellationToken);

    private static ConfigurationException Unreachable(ProcessResult result, string attempt)
    {
        var reason = result.Started
            ? result.StandardError.Trim()
            : "the 'secret-tool' command is not installed";

        return new ConfigurationException(
            $"The Secret Service could not {attempt}: {reason}. Use "
            + "'--credential-store file' to keep credentials in an encrypted file instead.");
    }
}
