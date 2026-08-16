using System.Net.Http.Headers;
using System.Text.Json;
using ModelContextProtocol.Client;

namespace JiraServerMcp.JiraIntegration.Tests.Harness;

/// <summary>
/// The product, configured and running against the real Jira, with an MCP client on the other end
/// of its standard input and output.
/// </summary>
/// <remarks>
/// This is the same seam <c>JiraServerMcp.Protocol.Tests</c> drives — a real client, a real host
/// process, the real protocol — with a genuine Jira behind it instead of WireMock. Nothing about
/// the product is configured differently here: a profile is registered and a token stored through
/// the same verbs a user runs.
/// </remarks>
internal sealed class HarnessSession : IAsyncDisposable
{
    private const string Profile = "harness";

    private readonly ConfigurationHome _home = new();

    private McpClient _client = null!;

    public required ProvisionedJira Jira { get; init; }

    public McpClient Client => _client;

    /// <summary>
    /// A direct line to Jira over the same personal access token, for asserting what a write
    /// actually did rather than trusting the tool's own account of it.
    /// </summary>
    public HttpClient JiraApi { get; private set; } = null!;

    public static async Task<HarnessSession> StartAsync(
        ProvisionedJira jira, IReadOnlyList<string> grants, CancellationToken cancellationToken)
    {
        var session = new HarnessSession { Jira = jira };

        await session.InitializeAsync(grants, cancellationToken);

        return session;
    }

    private async Task InitializeAsync(
        IReadOnlyList<string> grants, CancellationToken cancellationToken)
    {
        JiraApi = new HttpClient { BaseAddress = Jira.BaseUrl };
        JiraApi.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jira.PersonalAccessToken);

        var added = await HostProcess.RunAsync(
            ["profile", "add", Profile, "--url", Jira.BaseUrl.ToString().TrimEnd('/')],
            cancellationToken,
            _home.Environment);

        if (added.ExitCode is not 0)
        {
            throw new InvalidOperationException("profile add failed: " + added.StandardError);
        }

        // `auth login` validates the token against the real Jira before storing it, which makes
        // this the first end-to-end proof that the minted token authenticates the product.
        var loggedIn = await HostProcess.RunAsync(
            ["auth", "login", Profile],
            cancellationToken,
            _home.Environment,
            standardInput: Jira.PersonalAccessToken + "\n");

        if (loggedIn.ExitCode is not 0)
        {
            throw new InvalidOperationException("auth login failed: " + loggedIn.StandardError);
        }

        string[] allow = [.. grants.SelectMany(grant => (string[])["--allow", grant])];

        _client = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "jira-server-mcp",
                Command = HostProcess.Command,
                Arguments = HostProcess.ArgumentsFor(["serve", "--profile", Profile, .. allow]),
                EnvironmentVariables = _home.Environment.ToDictionary(
                    entry => entry.Key,
                    entry => (string?)entry.Value),
            }),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Reads an issue straight from Jira, so a post-condition is asserted against Jira's state
    /// rather than against what the tool said it did.
    /// </summary>
    public async Task<JsonElement> ReadIssueAsync(string key, CancellationToken cancellationToken)
    {
        using var response = await JiraApi.GetAsync(
            $"/rest/api/2/issue/{key}?expand=changelog", cancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken))
            .RootElement.Clone();
    }

    public async Task<JsonElement> ReadAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await JiraApi.GetAsync(path, cancellationToken);

        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken))
            .RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();

        JiraApi.Dispose();
        _home.Dispose();
    }
}
