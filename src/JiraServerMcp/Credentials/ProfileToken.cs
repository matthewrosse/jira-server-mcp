namespace JiraServerMcp.Credentials;

/// <summary>
/// A token and the place it came from, so operator output can say which without printing it.
/// </summary>
internal readonly record struct ProfileToken(string Value, string Source)
{
    /// <summary>
    /// The environment first: a container or a CI job has no keyring to reach and no terminal to
    /// be prompted at, and the variable is the documented way in. Failing that, the store.
    /// </summary>
    public static async Task<ProfileToken?> ResolveAsync(
        string profileName,
        ICredentialStore store,
        CancellationToken cancellationToken)
    {
        if (TokenEnvironmentVariable.Read(profileName) is { } fromEnvironment)
        {
            return new ProfileToken(
                fromEnvironment,
                $"environment variable {TokenEnvironmentVariable.NameFor(profileName)}");
        }

        return await store.GetAsync(profileName, cancellationToken) is { Length: > 0 } stored
            ? new ProfileToken(stored, store.Describe())
            : null;
    }
}

/// <summary>
/// One variable per profile — the escape hatch `--token` refuses to be, and the only supported
/// way to hand a token to a process that cannot be prompted.
/// </summary>
internal static class TokenEnvironmentVariable
{
    public static string NameFor(string profileName) =>
        $"JIRA_SERVER_MCP__{Shout(profileName)}__TOKEN";

    public static string? Read(string profileName) =>
        Environment.GetEnvironmentVariable(NameFor(profileName)) is { Length: > 0 } token
            ? token
            : null;

    /// <summary>
    /// Profile names may hold characters an environment variable may not, so anything that is
    /// not a letter or a digit becomes an underscore.
    /// </summary>
    private static string Shout(string profileName) =>
        new([.. profileName.ToUpperInvariant().Select(
            character => char.IsAsciiLetterOrDigit(character) ? character : '_')]);
}
