using System.Diagnostics;

namespace JiraServerMcp.Tests.Support;

/// <summary>
/// Launches the host the way an MCP client does — as a subprocess — so tests observe the real
/// standard output and standard error streams rather than an in-process shortcut.
/// </summary>
internal static class HostProcess
{
    /// <summary>
    /// The muxer is used rather than the apphost: it is on the path everywhere the tests run.
    /// </summary>
    public const string Command = "dotnet";

    /// <summary>
    /// The host's own build output, not a copy beside the test assembly: an Exe-to-Exe
    /// project reference does not reliably copy-local the referenced project's package
    /// dependencies, so a copy here can be missing assemblies the host needs at runtime.
    /// </summary>
    public static string Assembly { get; } = FindHostAssembly();

    private static string FindHostAssembly()
    {
        // .../tests/<TestProject>/bin/<Configuration>/<TargetFramework>/
        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var targetFramework = outputDirectory.Name;
        var configuration = outputDirectory.Parent!.Name;
        var repositoryRoot = outputDirectory.Parent!.Parent!.Parent!.Parent!.Parent!.FullName;

        return Path.Combine(
            repositoryRoot, "src", "JiraServerMcp", "bin", configuration, targetFramework,
            "jira-server-mcp.dll");
    }

    /// <summary>
    /// Every verb that reaches a credential store is pinned to the encrypted file. Left to
    /// choose, a test run on a developer's macOS machine or a CI runner with a keyring would
    /// read and write that machine's real credential store.
    /// </summary>
    public static string[] ArgumentsFor(params string[] verb) =>
        verb is ["auth", ..] or ["serve", ..] or ["profile", "remove", ..]
            ? [Assembly, .. verb, "--credential-store", "file"]
            : [Assembly, .. verb];

    public static async Task<HostProcessResult> RunAsync(
        string[] verb,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null,
        string? standardInput = null)
    {
        var startInfo = new ProcessStartInfo(Command)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in ArgumentsFor(verb))
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The host must never see configuration leaking in from whatever ran the tests.
        startInfo.Environment.Remove("JIRA_SERVER_MCP_URL");
        startInfo.Environment.Remove("JIRA_SERVER_MCP_TOKEN");

        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start " + Command);

        try
        {
            if (standardInput is not null)
            {
                try
                {
                    await process.StandardInput.WriteAsync(standardInput);
                }
                catch (IOException)
                {
                    // A verb that refuses before it reads — an unknown profile, say — can exit
                    // first, and a broken pipe here is that verb working, not a test failure.
                }
            }

            // Nothing is being served, so the host is told at once that no more input is coming.
            process.StandardInput.Close();

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            return new HostProcessResult(
                process.ExitCode,
                await standardOutput,
                await standardError);
        }
        catch (OperationCanceledException)
        {
            // Disposing a Process does not stop it, and an orphan here outlives the test run.
            process.Kill(entireProcessTree: true);
            throw;
        }
    }
}

internal sealed record HostProcessResult(int ExitCode, string StandardOutput, string StandardError);
