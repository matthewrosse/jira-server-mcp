namespace JiraServerMcp.Tests.Support;

/// <summary>
/// A throwaway configuration directory for one test, handed to the host through the XDG
/// variable so nothing the tests do can touch the developer's own profiles.
/// </summary>
internal sealed class ConfigurationHome : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "jira-server-mcp-tests", Path.GetRandomFileName());

    public ConfigurationHome() => System.IO.Directory.CreateDirectory(_root);

    /// <summary>
    /// The directory the host computes from <see cref="Environment"/>. It does not exist until
    /// the host writes something.
    /// </summary>
    public string Directory => Path.Combine(_root, "jira-server-mcp");

    public string ProfilesFile => Path.Combine(Directory, "profiles.json");

    public string CredentialsFile => Path.Combine(Directory, "credentials.json");

    public IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>
    {
        ["XDG_CONFIG_HOME"] = _root,
    };

    public string ReadProfiles() => File.ReadAllText(ProfilesFile);

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was written, which several tests are entitled to expect.
        }
    }
}
