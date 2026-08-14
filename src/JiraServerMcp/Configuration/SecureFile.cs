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
            // The writer leaves the stream open: the caller still has to flush it to disk.
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write(contents);
        });

    public static void WriteAllBytes(string path, byte[] contents) =>
        Write(path, stream => stream.Write(contents));

    private static void Write(string path, Action<FileStream> write)
    {
        // Written beside the target and moved over it, so an interrupted write leaves the
        // previous file intact rather than a truncated one holding every profile and token.
        var temporaryPath = path + ".tmp";

        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnly;
        }

        using (var stream = new FileStream(temporaryPath, options))
        {
            write(stream);
            stream.Flush(flushToDisk: true);
        }

        if (!OperatingSystem.IsWindows())
        {
            // A temporary file left by an earlier run keeps the mode it had, and this project's
            // files are owner-only however they came to exist.
            File.SetUnixFileMode(temporaryPath, OwnerOnly);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }
}
