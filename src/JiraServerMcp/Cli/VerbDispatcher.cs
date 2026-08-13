namespace JiraServerMcp.Cli;

/// <summary>
/// One verb exists so far. The rest arrive in later phases, and a parser arrives with them.
/// Everything here writes to standard error: standard output belongs to the protocol (ADR-0002).
/// </summary>
internal static class VerbDispatcher
{
    private const string Usage = """
        Usage: jira-server-mcp <verb>

          serve   Serve the Model Context Protocol over stdio.
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        switch (args)
        {
            case ["serve"]:
                await ServeVerb.RunAsync(CancellationToken.None);
                return 0;

            case []:
                await Console.Error.WriteLineAsync(Usage);
                return 2;

            default:
                await Console.Error.WriteLineAsync($"Unknown verb '{args[0]}'.");
                await Console.Error.WriteLineAsync(Usage);
                return 2;
        }
    }
}
