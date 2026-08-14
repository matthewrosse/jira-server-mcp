using System.ComponentModel;
using System.Diagnostics;

namespace JiraServerMcp.Credentials;

/// <summary>
/// Running one platform tool to completion. The native credential stores shell out rather than
/// interop, so this is the seam a test fakes: no keyring, no keychain, no daemon.
/// </summary>
internal interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="Started"/> is false when the tool is not installed at all — a machine to fall back
/// on rather than a failure to report.
/// </summary>
internal readonly record struct ProcessResult(
    bool Started,
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public static ProcessResult NotInstalled { get; } = new(false, -1, "", "");
}

internal sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            return ProcessResult.NotInstalled;
        }

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
        }

        process.StandardInput.Close();

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(true, process.ExitCode, await standardOutput, await standardError);
    }
}
