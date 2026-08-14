namespace JiraServerMcp.Configuration;

/// <summary>
/// Where this installation keeps its configuration. Developer-CLI convention rather than
/// platform-GUI convention: the XDG variable first, then a dot-config directory under home on
/// macOS as well as Linux, and roaming application data on Windows. The macOS path is computed
/// because <c>SpecialFolder.ApplicationData</c> answers with the GUI location there.
/// </summary>
internal static class ConfigurationPaths
{
    private const string DirectoryName = "jira-server-mcp";

    public static string ConfigurationDirectory() =>
        ConfigurationDirectory(System.Environment.GetEnvironmentVariable, OperatingSystem.IsWindows());

    internal static string ConfigurationDirectory(Func<string, string?> environment, bool isWindows)
    {
        var xdgConfigHome = environment("XDG_CONFIG_HOME");

        if (!string.IsNullOrEmpty(xdgConfigHome))
        {
            return Path.Combine(xdgConfigHome, DirectoryName);
        }

        if (isWindows)
        {
            var applicationData = environment("APPDATA")
                ?? throw new InvalidOperationException(
                    "APPDATA is not set, so there is nowhere to keep configuration. "
                    + "Set XDG_CONFIG_HOME to a directory this tool may write to.");

            return Path.Combine(applicationData, DirectoryName);
        }

        var home = environment("HOME")
            ?? throw new InvalidOperationException(
                "HOME is not set, so there is nowhere to keep configuration. "
                + "Set XDG_CONFIG_HOME to a directory this tool may write to.");

        return Path.Combine(home, ".config", DirectoryName);
    }

    /// <summary>
    /// Creates the configuration directory if it is missing, owner-only where the platform has
    /// such permissions.
    /// </summary>
    public static void Ensure(string directory)
    {
        if (Directory.Exists(directory))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite
                                                 | UnixFileMode.UserExecute);
        }
    }
}
