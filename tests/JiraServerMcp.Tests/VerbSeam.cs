using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JiraServerMcp.Tests;

/// <summary>
/// The staging a verb test needs before it can assert anything (ADR-0008, clause 4): a throwaway
/// configuration directory, a Jira double, and the two steps that climb from an unregistered
/// profile to a logged-in one. A test holds one as a field rather than inheriting from it, so what
/// it stages stays visible at the call site.
/// </summary>
/// <remarks>
/// <para>
/// Composed rather than started, deliberately unlike <c>ProtocolSeam.StartAsync</c>. At the
/// protocol seam every test wants the same logged-in server; here the ladder from unregistered to
/// logged in is the thing under test, so the precondition is exactly what has to vary.
/// </para>
/// <para>
/// <b>When a verb is the subject, spell it as <see cref="RunAsync"/>; when it is staging, call the
/// step.</b> A test proving what <c>profile add</c> does spells <c>profile add</c>; a test that
/// only needs a profile to exist calls <see cref="AddProfileAsync"/>. Both halves read at the call
/// site, so a file using half the fixture is not a file exempt from it.
/// </para>
/// </remarks>
internal sealed class VerbSeam : IDisposable
{
    public const string Token = "s3cr3t-personal-access-token";

    private WireMockServer? _jira;

    /// <summary>
    /// The double, started on the first touch. xUnit builds one of these per test, and a verb that
    /// never reaches Jira — an unknown verb, a profile that was never registered, a URL that is
    /// refused before anything is dialled — should not pay for an HTTP server to prove it.
    /// </summary>
    public WireMockServer Jira => _jira ??= WireMockServer.Start();

    /// <summary>The configuration the host reads, for the tests that go at what it writes.</summary>
    public ConfigurationHome Home { get; } = new();

    /// <summary>
    /// A registered profile. <paramref name="url"/> defaults to the double's and is given
    /// explicitly by the test whose Jira is somewhere else.
    /// </summary>
    public async Task AddProfileAsync(string name = "work", string? url = null)
    {
        var result = await RunAsync(["profile", "add", name, "--url", url ?? Jira.Url!]);

        result.ExitCode.ShouldBe(0, result.StandardError);
    }

    /// <summary>
    /// A stored token. <c>auth login</c> validates one before storing it, so Jira has to answer for
    /// the login itself. The stub is left standing rather than reset: the next <c>auth status</c>
    /// asks again, and a test's own stubs have to survive the login that reads them.
    /// </summary>
    public async Task LoginAsync(string name = "work", string? token = null)
    {
        Jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JiraAccount.Payload()));

        var result = await RunAsync(["auth", "login", name], (token ?? Token) + "\n");

        result.ExitCode.ShouldBe(0, result.StandardError);
    }

    /// <summary>
    /// A verb against this configuration, asserting nothing: at this seam the exit code is what a
    /// test is here to assert, not something staging can decide on its behalf.
    /// </summary>
    /// <remarks>
    /// An <c>environment</c> is merged over <see cref="ConfigurationHome.Environment"/> rather
    /// than substituted for it. The variable under test is the one entry; the throwaway
    /// configuration directory is staging, and a caller that had to re-spell it could drop it
    /// without anything saying so.
    /// </remarks>
    public Task<HostProcessResult> RunAsync(
        string[] verb,
        string? standardInput = null,
        IReadOnlyDictionary<string, string>? environment = null) =>
        HostProcess.RunAsync(
            verb,
            TestContext.Current.CancellationToken,
            MergedOverHome(environment),
            standardInput);

    public void Dispose()
    {
        _jira?.Stop();
        Home.Dispose();
    }

    private IReadOnlyDictionary<string, string> MergedOverHome(
        IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is null)
        {
            return Home.Environment;
        }

        var merged = new Dictionary<string, string>(Home.Environment);

        foreach (var (key, value) in environment)
        {
            merged[key] = value;
        }

        return merged;
    }
}
