using ModelContextProtocol.Client;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Protocol.Tests;

/// <summary>
/// The credential store's contract — store, overwrite, retrieve, delete — observed the only way
/// that matters: through the verbs a user runs and the token Jira is eventually shown.
/// </summary>
public sealed class CredentialRoundTripTests : IDisposable
{
    private const string Profile = "work";
    private const string FirstToken = "first-personal-access-token";
    private const string SecondToken = "second-personal-access-token";

    private readonly WireMockServer _jira = WireMockServer.Start();
    private readonly ConfigurationHome _home = new();

    public void Dispose()
    {
        _jira.Stop();
        _home.Dispose();
    }

    [Fact]
    public async Task Storing_a_token_twice_leaves_the_second_one_in_use()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"name":"mrosse","displayName":"Mateusz Różański","active":true}"""));

        await RunAsync(["profile", "add", Profile, "--url", _jira.Url!]);
        await RunAsync(["auth", "login", Profile], FirstToken + "\n");
        await RunAsync(["auth", "login", Profile], SecondToken + "\n");

        await CallWhoamiAsync();

        var authorization = _jira.LogEntries.ShouldHaveSingleItem().ShouldNotBeNull()
            .RequestMessage.ShouldNotBeNull()
            .Headers.ShouldNotBeNull()["Authorization"].ShouldHaveSingleItem();

        authorization.ShouldBe("Bearer " + SecondToken);
    }

    [Fact]
    public async Task A_stored_token_is_not_readable_in_the_file_that_holds_it()
    {
        await RunAsync(["profile", "add", Profile, "--url", _jira.Url!]);
        await RunAsync(["auth", "login", Profile], FirstToken + "\n");

        var contents = await File.ReadAllTextAsync(
            _home.CredentialsFile, TestContext.Current.CancellationToken);

        contents.ShouldNotContain(FirstToken);
        contents.ShouldNotContain("personal-access-token");
    }

    private async Task CallWhoamiAsync()
    {
        await using var client = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "jira-server-mcp",
                Command = HostProcess.Command,
                Arguments = HostProcess.ArgumentsFor("serve", "--profile", Profile),
                EnvironmentVariables = _home.Environment.ToDictionary(
                    entry => entry.Key,
                    entry => (string?)entry.Value),
            }),
            cancellationToken: TestContext.Current.CancellationToken);

        await client.CallToolAsync("jira_whoami", cancellationToken: TestContext.Current.CancellationToken);
    }

    private async Task RunAsync(string[] verb, string? standardInput = null)
    {
        var result = await HostProcess.RunAsync(
            verb, TestContext.Current.CancellationToken, _home.Environment, standardInput);

        result.ExitCode.ShouldBe(0);
    }
}
