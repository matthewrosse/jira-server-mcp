namespace JiraServerMcp.Credentials;

/// <summary>
/// Where a profile's personal access token lives. One implementation exists so far — the
/// encrypted file, which works everywhere including headless Linux and CI.
/// </summary>
internal interface ICredentialStore
{
    /// <summary>
    /// The stored personal access token for a profile, or null when there is none.
    /// </summary>
    Task<string?> GetAsync(string profileName, CancellationToken cancellationToken);

    Task SetAsync(string profileName, string personalAccessToken, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the credential. Removing one that is not there is not an error.
    /// </summary>
    Task DeleteAsync(string profileName, CancellationToken cancellationToken);

    /// <summary>
    /// One line naming this store for a person reading terminal output. Never includes a secret.
    /// </summary>
    string Describe();
}
