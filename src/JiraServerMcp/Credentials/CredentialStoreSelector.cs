using JiraServerMcp.Configuration;

namespace JiraServerMcp.Credentials;

internal enum CredentialStoreChoice
{
    /// <summary>
    /// The operating system's store where it can be reached, and the encrypted file where it
    /// cannot.
    /// </summary>
    Auto,

    Native,

    File,
}

/// <summary>
/// Which store a run works against. A machine with no reachable keyring — headless Linux, WSL,
/// a session over SSH, a KDE desktop with no Secret Service bridge — falls back to the encrypted
/// file, and is told so once rather than left to wonder where its tokens went.
/// </summary>
internal sealed class CredentialStoreSelector(INativeCredentialStore? native, ICredentialStore file)
{
    public static CredentialStoreSelector ForThisMachine() =>
        new(NativeCredentialStore.ForThisPlatform(new ProcessRunner()),
            FileCredentialStore.InConfigurationDirectory());

    public async Task<ICredentialStore> SelectAsync(
        CredentialStoreChoice choice,
        TextWriter fallbackLog,
        CancellationToken cancellationToken)
    {
        if (choice is CredentialStoreChoice.File)
        {
            return file;
        }

        var usable = native is not null && await native.IsUsableAsync(cancellationToken);

        if (usable)
        {
            return native!;
        }

        if (choice is CredentialStoreChoice.Native)
        {
            throw new ConfigurationException(
                $"The {native?.Describe() ?? "operating system credential store"} cannot be "
                + "reached from this session. Use '--credential-store file' to keep credentials "
                + "in an encrypted file instead.");
        }

        await fallbackLog.WriteLineAsync(
            $"No operating system credential store is reachable here, so the {file.Describe()} "
            + "is being used instead.");

        return file;
    }
}
