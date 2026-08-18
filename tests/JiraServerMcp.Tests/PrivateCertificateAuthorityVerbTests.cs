namespace JiraServerMcp.Tests;

/// <summary>
/// A Jira behind a certificate authority the machine does not know about. The profile names a
/// bundle, and every verb that talks to Jira has to trust it — a verb that does not cannot store
/// a token, and a profile without a token cannot be served.
/// </summary>
public sealed class PrivateCertificateAuthorityVerbTests : IDisposable
{
    private const string Token = "s3cr3t-personal-access-token";

    private const string MyselfPayload =
        """{"key":"JIRAUSER10100","name":"ada","displayName":"Ada Lovelace","active":true}""";

    private readonly ConfigurationHome _home = new();
    private readonly TlsTestServer _jira =
        TlsTestServer.StartWithASelfSignedCertificate(MyselfPayload);

    public void Dispose()
    {
        _jira.Dispose();
        _home.Dispose();
    }

    [Fact]
    public async Task Login_trusts_the_profiles_certificate_authority_bundle()
    {
        await AddProfileAsync();

        var result = await RunAsync(["auth", "login", "work"], standardInput: Token + "\n");

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("Ada Lovelace");

        File.Exists(_home.CredentialsFile).ShouldBeTrue();
    }

    [Fact]
    public async Task Status_trusts_the_profiles_certificate_authority_bundle()
    {
        await AddProfileAsync();

        (await RunAsync(["auth", "login", "work"], standardInput: Token + "\n")).ExitCode
            .ShouldBe(0);

        var result = await RunAsync(["auth", "status", "work"]);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("Ada Lovelace");
    }

    private async Task AddProfileAsync()
    {
        Directory.CreateDirectory(_home.Directory);

        var bundle = Path.Combine(_home.Directory, "corporate-ca.pem");

        await File.WriteAllTextAsync(
            bundle,
            _jira.CertificatePem,
            TestContext.Current.CancellationToken);

        var added = await RunAsync(
            ["profile", "add", "work", "--url", _jira.Url.ToString(), "--ca-bundle", bundle]);

        added.ExitCode.ShouldBe(0);
    }

    private Task<HostProcessResult> RunAsync(string[] verb, string? standardInput = null) =>
        HostProcess.RunAsync(
            verb,
            TestContext.Current.CancellationToken,
            _home.Environment,
            standardInput);
}
