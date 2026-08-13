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

    public static string Assembly { get; } =
        Path.Combine(AppContext.BaseDirectory, "jira-server-mcp.dll");

    public static string[] ArgumentsFor(params string[] verb) => [Assembly, .. verb];

    public static async Task<HostProcessResult> RunAsync(
        string[] verb,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(Command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in ArgumentsFor(verb))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start " + Command);

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new HostProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }
}

internal sealed record HostProcessResult(int ExitCode, string StandardOutput, string StandardError);
