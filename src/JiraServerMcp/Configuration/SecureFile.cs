namespace JiraServerMcp.Configuration;

/// <summary>
/// Writes files owner-only where the platform has such permissions. The mode is set as the file
/// is created rather than afterwards, so there is no window in which it is world-readable.
/// </summary>
internal static class SecureFile
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static void WriteAllText(string path, string contents) =>
        Write(path, stream =>
        {
            using var writer = new StreamWriter(stream);
            writer.Write(contents);
        });

    public static void WriteAllBytes(string path, byte[] contents) =>
        Write(path, stream => stream.Write(contents));

    private static void Write(string path, Action<FileStream> write)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnly;
        }

        using (var stream = new FileStream(path, options))
        {
            write(stream);
        }

        if (!OperatingSystem.IsWindows())
        {
            // A file that already existed keeps whatever mode it had, and this project's files
            // are owner-only whether they were created a moment ago or a year ago.
            File.SetUnixFileMode(path, OwnerOnly);
        }
    }
}
