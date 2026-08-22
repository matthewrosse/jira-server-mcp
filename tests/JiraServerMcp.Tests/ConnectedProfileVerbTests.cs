using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Tests;

/// <summary>
/// The failure ladder a verb shows an operator when a call against a connected profile does not
/// come back: what is printed, and the exit code (ADR-0008, clause 4). `auth login` stands in for
/// all three verbs — the ladder itself has one implementation.
/// </summary>
public sealed class ConnectedProfileVerbTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    private readonly ConfigurationHome _home = new();
    private readonly WireMockServer _jira = WireMockServer.Start();

    public void Dispose()
    {
        _jira.Stop();
        _home.Dispose();
    }

    /// <summary>
    /// The arm `auth login` was missing: an address that accepts the connection and then says
    /// nothing used to throw here while `profile refresh` printed a sentence.
    /// </summary>
    [Fact]
    public async Task Logging_in_against_a_jira_that_never_answers_says_so_and_does_not_throw()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"name":"ada","displayName":"Ada Lovelace","active":true}""")
                .WithDelay(TimeSpan.FromSeconds(10)));

        await AddAsync("work");

        var result = await LoginAsync("work", new Dictionary<string, string>(_home.Environment)
        {
            ["JIRA_SERVER_MCP__TIMEOUT_SECONDS"] = "1",
        });

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldNotContain("Unhandled exception");
        result.StandardError.ShouldContain("did not answer in time");
        result.StandardError.ShouldContain("the token could not be checked and was not stored");
    }

    /// <summary>
    /// A captive portal or a proxy answering 200 with a login page. The success paths read the
    /// body as JSON unguarded, so this arrives as neither an API failure nor an unreachable Jira.
    /// </summary>
    [Fact]
    public async Task Logging_in_against_a_body_that_is_not_json_reports_the_fault_by_name()
    {
        _jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("<html><body>Sign in to the network</body></html>"));

        await AddAsync("work");

        var result = await LoginAsync("work");

        result.ExitCode.ShouldNotBe(0);
        result.StandardError.ShouldNotContain("Unhandled exception");
        result.StandardError.ShouldContain("JsonException");
        result.StandardError.ShouldContain("fault in this tool rather than in Jira");
    }

    [Fact]
    public async Task A_timeout_that_is_not_a_positive_whole_number_of_seconds_is_refused()
    {
        await AddAsync("work");

        var result = await LoginAsync("work", new Dictionary<string, string>(_home.Environment)
        {
            ["JIRA_SERVER_MCP__TIMEOUT_SECONDS"] = "half a minute",
        });

        result.ExitCode.ShouldBe(2);
        result.StandardError.ShouldContain("JIRA_SERVER_MCP__TIMEOUT_SECONDS");
        result.StandardError.ShouldContain("whole number of seconds");
        result.StandardError.ShouldContain("default of 30 seconds");
    }

    [Fact]
    public async Task A_timeout_longer_than_the_client_accepts_is_refused_rather_than_thrown()
    {
        await AddAsync("work");

        var result = await LoginAsync("work", new Dictionary<string, string>(_home.Environment)
        {
            ["JIRA_SERVER_MCP__TIMEOUT_SECONDS"] = "99999999",
        });

        result.ExitCode.ShouldBe(2);
        result.StandardError.ShouldContain("JIRA_SERVER_MCP__TIMEOUT_SECONDS");
        result.StandardError.ShouldNotContain("ArgumentOutOfRangeException");
    }

    [Fact]
    public async Task A_timeout_of_zero_seconds_is_refused_too()
    {
        await AddAsync("work");

        var result = await LoginAsync("work", new Dictionary<string, string>(_home.Environment)
        {
            ["JIRA_SERVER_MCP__TIMEOUT_SECONDS"] = "0",
        });

        result.ExitCode.ShouldBe(2);
        result.StandardError.ShouldContain("JIRA_SERVER_MCP__TIMEOUT_SECONDS");
    }

    private Task<HostProcessResult> AddAsync(string name) =>
        HostProcess.RunAsync(
            ["profile", "add", name, "--url", _jira.Url!],
            TestContext.Current.CancellationToken,
            _home.Environment);

    private Task<HostProcessResult> LoginAsync(
        string name,
        IReadOnlyDictionary<string, string>? environment = null) =>
        HostProcess.RunAsync(
            ["auth", "login", name],
            TestContext.Current.CancellationToken,
            environment ?? _home.Environment,
            Token + "\n");
}
