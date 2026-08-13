namespace JiraServerMcp.Tests.Support;

/// <summary>
/// Locates the repository the test binary was built from, so structural tests can read the
/// project files themselves.
/// </summary>
internal static class RepositoryRoot
{
    public static DirectoryInfo Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !directory.GetFiles("JiraServerMcp.slnx").Any())
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException(
            "Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// The project file of a source project, by name. Built from the repository root rather than
    /// searched for: a recursive search also finds copies under nested working directories.
    /// </summary>
    public static FileInfo SourceProject(string name) =>
        new(Path.Combine(Find().FullName, "src", name, name + ".csproj"));
}
