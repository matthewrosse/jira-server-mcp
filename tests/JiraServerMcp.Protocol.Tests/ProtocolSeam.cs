using ModelContextProtocol.Client;
using WireMock.RequestBuilders;
using WireMock.Server;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// The staging every protocol test needs before it can assert anything (ADR-0008): a WireMock Jira,
/// a registered profile, a stored token, and MCP clients over stdio against the real host process.
/// A test holds one as a field rather than inheriting from it, so what it stages stays visible at
/// the call site.
/// </summary>
internal sealed class ProtocolSeam : IAsyncDisposable
{
    public const string Token = "s3cr3t-personal-access-token";

    public const string Profile = "work";

    private readonly List<McpClient> _clients = [];

    private ProtocolSeam()
    {
    }

    /// <summary>The double itself, stubbed and asserted on directly by the test that holds it.</summary>
    public WireMockServer Jira { get; } = WireMockServer.Start();

    /// <summary>The configuration the host reads, for the tests that go at the profiles file.</summary>
    public ConfigurationHome Home { get; } = new();

    /// <summary>
    /// A seam that is already logged in, because a seam that exists and is not is a state no test
    /// wants. The server is configured the way a user configures it: a registered profile and a
    /// stored credential, with no environment variable in sight.
    /// </summary>
    public static async Task<ProtocolSeam> StartAsync()
    {
        var seam = new ProtocolSeam();

        await seam.RunAsync(["profile", "add", Profile, "--url", seam.Jira.Url!]);

        // `auth login` validates the token before storing it, so Jira has to answer for the login
        // itself. The stub and the request it logged are then cleared, leaving each test the empty
        // slate it asserts against.
        seam.Jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(JiraResponse.Json(200, JiraAccount.Payload()));

        await seam.RunAsync(["auth", "login", Profile], standardInput: Token + "\n");

        seam.Jira.Reset();

        return seam;
    }

    /// <summary>
    /// A server launched with the grants named here, exactly as an operator's MCP configuration
    /// would (ADR-0005). The client is disposed with the seam.
    /// </summary>
    public async Task<McpClient> ConnectAsync(params string[] grants)
    {
        string[] allow = [.. grants.SelectMany(grant => (string[])["--allow", grant])];

        var client = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "jira-server-mcp",
                Command = HostProcess.Command,
                Arguments = HostProcess.ArgumentsFor(["serve", "--profile", Profile, .. allow]),
                EnvironmentVariables = Home.Environment.ToDictionary(
                    entry => entry.Key,
                    entry => (string?)entry.Value),
            }),
            cancellationToken: TestContext.Current.CancellationToken);

        _clients.Add(client);

        return client;
    }

    /// <summary>A further verb against the same configuration, which is expected to succeed.</summary>
    public async Task RunAsync(string[] verb, string? standardInput = null)
    {
        var result = await HostProcess.RunAsync(
            verb,
            TestContext.Current.CancellationToken,
            Home.Environment,
            standardInput);

        result.ExitCode.ShouldBe(0, result.StandardError);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        Jira.Stop();
        Home.Dispose();
    }
}
