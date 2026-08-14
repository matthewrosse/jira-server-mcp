using System.Diagnostics;
using System.Text.Json;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// ADR-0002: standard output belongs to the protocol. The host is driven with raw JSON-RPC and
/// logging turned all the way up, and every line it emits on standard output must still be a
/// protocol message.
/// </summary>
public sealed class StandardOutputTests
{
    private const string Handshake = """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"stdout-test","version":"1.0.0"}}}
        {"jsonrpc":"2.0","method":"notifications/initialized"}
        {"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
        """;

    [Fact]
    public async Task Nothing_but_protocol_traffic_reaches_standard_output()
    {
        using var home = new ConfigurationHome();

        await SetUpProfileAsync(home);

        var startInfo = new ProcessStartInfo(HostProcess.Command)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in HostProcess.ArgumentsFor("serve", "--profile", "work"))
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Every logger at its loudest, so a log line landing on standard output would be seen.
        startInfo.Environment["Logging__LogLevel__Default"] = "Trace";

        // The token comes from the environment, the way a container is given one: nothing
        // answers on this profile's URL, so there is no Jira to validate a login against.
        startInfo.Environment["JIRA_SERVER_MCP__WORK__TOKEN"] = "unused-by-this-test";

        foreach (var (key, value) in home.Environment)
        {
            startInfo.Environment[key] = value;
        }

        using var process = Process.Start(startInfo).ShouldNotBeNull();

        try
        {
            await process.StandardInput.WriteAsync(Handshake.ReplaceLineEndings("\n") + "\n");
            await process.StandardInput.FlushAsync(TestContext.Current.CancellationToken);

            var lines = await ReadLinesAsync(process, expected: 2);

            lines.Count.ShouldBe(2);

            // Shutdown is part of the guarantee too, so the rest of the stream is read to the
            // end rather than the process being killed the moment the responses arrive.
            process.StandardInput.Close();
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);

            var remainder = await process.StandardOutput.ReadToEndAsync(
                TestContext.Current.CancellationToken);

            lines.AddRange(remainder.Split('\n', StringSplitOptions.RemoveEmptyEntries
                                                 | StringSplitOptions.TrimEntries));

            foreach (var line in lines)
            {
                var message = JsonDocument.Parse(line).RootElement;

                message.GetProperty("jsonrpc").GetString().ShouldBe("2.0");
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static async Task SetUpProfileAsync(ConfigurationHome home)
    {
        // Nothing answers on port 1, which is all this test needs: no tool is called.
        await HostProcess.RunAsync(
            ["profile", "add", "work", "--url", "http://localhost:1/"],
            TestContext.Current.CancellationToken,
            home.Environment);
    }

    private static async Task<List<string>> ReadLinesAsync(Process process, int expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var lines = new List<string>();

        while (lines.Count < expected)
        {
            var line = await process.StandardOutput.ReadLineAsync(timeout.Token);

            if (line is null)
            {
                break;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        return lines;
    }
}
