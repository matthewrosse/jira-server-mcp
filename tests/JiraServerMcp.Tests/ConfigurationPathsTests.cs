using JiraServerMcp.Configuration;

namespace JiraServerMcp.Tests;

/// <summary>
/// Configuration lives where developer CLIs put it, not where the platform's GUI applications
/// do. The environment is passed in rather than read, so every case is exercised on every
/// operating system the tests run on.
/// </summary>
public sealed class ConfigurationPathsTests
{
    [Fact]
    public void The_xdg_variable_wins_when_it_is_set()
    {
        var directory = ConfigurationPaths.ConfigurationDirectory(
            Environment(xdgConfigHome: "/tmp/xdg", home: "/home/dev", applicationData: null),
            isWindows: false);

        directory.ShouldBe(Path.Combine("/tmp/xdg", "jira-server-mcp"));
    }

    [Fact]
    public void Without_the_xdg_variable_configuration_lives_in_dot_config_under_home()
    {
        var directory = ConfigurationPaths.ConfigurationDirectory(
            Environment(xdgConfigHome: null, home: "/Users/dev", applicationData: null),
            isWindows: false);

        // On macOS this is the point: ~/Library/Application Support is the GUI location, and a
        // developer CLI's configuration does not belong there.
        directory.ShouldBe(Path.Combine("/Users/dev", ".config", "jira-server-mcp"));
        directory.ShouldNotContain("Application Support");
    }

    [Fact]
    public void On_windows_configuration_lives_in_roaming_application_data()
    {
        var directory = ConfigurationPaths.ConfigurationDirectory(
            Environment(xdgConfigHome: null, home: null, applicationData: @"C:\Users\dev\AppData\Roaming"),
            isWindows: true);

        directory.ShouldBe(Path.Combine(@"C:\Users\dev\AppData\Roaming", "jira-server-mcp"));
    }

    [Fact]
    public void The_xdg_variable_wins_on_windows_too()
    {
        var directory = ConfigurationPaths.ConfigurationDirectory(
            Environment(xdgConfigHome: @"D:\config", home: null, applicationData: @"C:\Users\dev\AppData\Roaming"),
            isWindows: true);

        directory.ShouldBe(Path.Combine(@"D:\config", "jira-server-mcp"));
    }

    [Fact]
    public void An_empty_variable_counts_as_unset()
    {
        var directory = ConfigurationPaths.ConfigurationDirectory(
            Environment(xdgConfigHome: "", home: "/home/dev", applicationData: null),
            isWindows: false);

        directory.ShouldBe(Path.Combine("/home/dev", ".config", "jira-server-mcp"));
    }

    private static Func<string, string?> Environment(
        string? xdgConfigHome,
        string? home,
        string? applicationData) =>
        variable => variable switch
        {
            "XDG_CONFIG_HOME" => xdgConfigHome,
            "HOME" => home,
            "APPDATA" => applicationData,
            _ => null,
        };
}
