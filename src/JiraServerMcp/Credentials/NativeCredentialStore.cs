namespace JiraServerMcp.Credentials;

/// <summary>
/// A credential store the operating system provides, which may or may not be reachable from the
/// session the tool is running in.
/// </summary>
internal interface INativeCredentialStore : ICredentialStore
{
    /// <summary>
    /// Whether this store can be reached right now. False for a Linux box with no session bus,
    /// a machine without the platform tool installed, or a keychain that cannot be searched.
    /// </summary>
    Task<bool> IsUsableAsync(CancellationToken cancellationToken);
}

internal static class NativeCredentialStore
{
    /// <summary>
    /// The service name every backend keys its entries under. One profile is one entry.
    /// </summary>
    public const string Service = "jira-server-mcp";

    /// <summary>
    /// The store this operating system offers, or null where there is none to offer.
    /// </summary>
    public static INativeCredentialStore? ForThisPlatform(IProcessRunner processRunner)
    {
        if (OperatingSystem.IsMacOS())
        {
            return new KeychainCredentialStore(processRunner);
        }

        if (OperatingSystem.IsLinux())
        {
            return new SecretServiceCredentialStore(processRunner);
        }

        if (OperatingSystem.IsWindows())
        {
            return new CredentialManagerCredentialStore();
        }

        return null;
    }
}
