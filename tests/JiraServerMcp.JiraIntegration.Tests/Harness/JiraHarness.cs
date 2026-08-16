using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;

namespace JiraServerMcp.JiraIntegration.Tests.Harness;

/// <summary>
/// A genuine Jira Server 8.20.7, set up, licensed, seeded, and holding a personal access token
/// the suite authenticates with.
/// </summary>
/// <remarks>
/// <para>
/// Shared across every Jira-backed test class, because bringing one up costs minutes. It is
/// deliberately lazy: the assembly fixture is constructed for every run of this project, including
/// the pull-request run that executes only the parser and readiness tests, and none of those may
/// pay for a container.
/// </para>
/// <para>
/// Two provisioning paths, one set of provisioning code. Set <c>JIRA_HARNESS_BASE_URL</c> and the
/// harness configures the instance already running there — which is what <c>scripts/jira-up.sh</c>
/// gives a developer through Compose. Leave it unset and it starts its own containers, which is
/// what CI does.
/// </para>
/// </remarks>
public sealed class JiraHarness : IAsyncDisposable
{
    /// <summary>
    /// The canonical version: the one the primary users run.
    /// </summary>
    internal const string JiraImage = "atlassian/jira-software:8.20.7-jdk11";

    internal const string PostgresImage = "postgres:13";

    /// <summary>
    /// The spike measured 199s to a servable wizard and 378s end to end on an Apple Silicon host
    /// under emulation, and took no hosted-runner measurement at all. This is set well above both
    /// rather than tuned to numbers that do not describe CI.
    /// </summary>
    private static readonly TimeSpan _bootBudget = TimeSpan.FromMinutes(15);

    private readonly JiraAdministrator _administrator = new();

    private readonly SemaphoreSlim _gate = new(1, 1);

    private INetwork? _network;

    private IContainer? _postgres;

    private IContainer? _jira;

    private Task<ProvisionedJira>? _provisioning;

    /// <summary>
    /// The task is cached rather than its result, so a Jira that failed to come up is reported to
    /// every waiting test immediately instead of being attempted once per test.
    /// </summary>
    internal async Task<ProvisionedJira> ReadyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            _provisioning ??= ProvisionAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        return await _provisioning;
    }

    private async Task<ProvisionedJira> ProvisionAsync(CancellationToken cancellationToken)
    {
        var baseUrl = Environment.GetEnvironmentVariable("JIRA_HARNESS_BASE_URL") is { Length: > 0 } existing
            ? new Uri(existing.TrimEnd('/') + "/")
            : await StartContainersAsync(cancellationToken);

        using var client = new HttpClient { BaseAddress = baseUrl, Timeout = TimeSpan.FromMinutes(5) };

        var readiness = new JiraReadiness(client, TimeSpan.FromSeconds(5));

        await readiness.WaitForSetupWizardAsync(_bootBudget, cancellationToken);

        var licenseKey = ReadLicenseKey();
        var wizard = new JiraSetupWizard(baseUrl, _administrator, licenseKey);

        await wizard.RunAsync(cancellationToken);

        // Setup reconfigures and restarts the instance, so the second gate is a different question
        // from the first: not "will it serve the wizard" but "is the platform API answering".
        await readiness.WaitForPlatformApiAsync(_bootBudget, cancellationToken);

        var seeded = await new JiraSeeder(client, _administrator).SeedAsync(cancellationToken);

        var token = await PersonalAccessTokenMinter.MintAsync(client, _administrator, cancellationToken);

        return new ProvisionedJira(baseUrl, token, seeded, _administrator);
    }

    private async Task<Uri> StartContainersAsync(CancellationToken cancellationToken)
    {
        _network = new NetworkBuilder().Build();

        await _network.CreateAsync(cancellationToken);

        _postgres = new ContainerBuilder(PostgresImage)
            .WithNetwork(_network)
            .WithNetworkAliases("postgres")
            .WithEnvironment("POSTGRES_DB", "jira")
            .WithEnvironment("POSTGRES_USER", "jira")
            .WithEnvironment("POSTGRES_PASSWORD", "jira")
            // Jira requires this collation. Pinned so a different host locale cannot change it.
            .WithEnvironment("POSTGRES_INITDB_ARGS", "--encoding=UTF8 --lc-collate=C --lc-ctype=C")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-U", "jira", "-d", "jira"))
            .Build();

        await _postgres.StartAsync(cancellationToken);

        _jira = new ContainerBuilder(JiraImage)
            .WithNetwork(_network)
            .WithEnvironment("ATL_DB_TYPE", "postgres72")
            // Required, and its absence is silent: the image's dbconfig.xml.j2 renders
            // <driver-class> straight from this with no default, and Jira then reports the
            // database as not configured and falls back to the wizard's database step.
            .WithEnvironment("ATL_DB_DRIVER", "org.postgresql.Driver")
            .WithEnvironment("ATL_JDBC_URL", "jdbc:postgresql://postgres:5432/jira")
            .WithEnvironment("ATL_JDBC_USER", "jira")
            .WithEnvironment("ATL_JDBC_PASSWORD", "jira")
            .WithEnvironment("ATL_DB_SCHEMA_NAME", "public")
            .WithEnvironment("JVM_MINIMUM_MEMORY", "1024m")
            .WithEnvironment("JVM_MAXIMUM_MEMORY", "2048m")
            .WithEnvironment("ATL_TOMCAT_PORT", "8080")
            .WithPortBinding(8080, assignRandomHostPort: true)
            // The 8.20.7 tag publishes a single-arch amd64 manifest, so an Apple Silicon
            // developer runs it under emulation. Stated rather than left to chance.
            .WithCreateParameterModifier(parameters => parameters.Platform = "linux/amd64")
            .Build();

        await _jira.StartAsync(cancellationToken);

        return new Uri($"http://{_jira.Hostname}:{_jira.GetMappedPublicPort(8080)}/");
    }

    /// <summary>
    /// Atlassian's published ten-user, three-hour testing licence. Committed, because it needs no
    /// account, no purchase, and no repository secret.
    /// </summary>
    private static string ReadLicenseKey()
    {
        var file = Path.Combine(
            RepositoryRoot.Find().FullName, "tests", "fixtures", "jira-dc-timebomb-3h.license");

        // The published key is line-wrapped for display; Jira wants it unwrapped.
        return new string([.. File.ReadAllText(file).Where(character => !char.IsWhiteSpace(character))]);
    }

    /// <summary>
    /// Containers and their volumes go together: state carried between runs would make a green
    /// run mean less than it says.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_jira is not null)
        {
            await _jira.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        if (_network is not null)
        {
            await _network.DisposeAsync();
        }

        _gate.Dispose();
    }
}

/// <summary>
/// A Jira the suite can point at: where it is, the personal access token to authenticate with,
/// and what was seeded into it.
/// </summary>
internal sealed record ProvisionedJira(
    Uri BaseUrl,
    string PersonalAccessToken,
    SeededJira Seeded,
    JiraAdministrator Administrator);
