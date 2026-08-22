namespace JiraServerMcp.Tests;

/// <summary>
/// A Jira behind a certificate authority the machine does not know about. The profile names a
/// bundle, and every verb that talks to Jira has to trust it — a verb that does not cannot store
/// a token, and a profile without a token cannot be served.
/// </summary>
public sealed class PrivateCertificateAuthorityVerbTests : IDisposable
{
    private readonly VerbSeam _seam = new();

    /// <summary>
    /// This file's Jira is not the seam's double: WireMock serves its own generated certificate
    /// and does not hand out the material needed to trust it, which is what these tests need.
    /// The seam's double is never touched, so no HTTP server is started beside this one.
    /// </summary>
    private readonly TlsTestServer _jira =
        TlsTestServer.StartWithASelfSignedCertificate(JiraAccount.Payload());

    public void Dispose()
    {
        _jira.Dispose();
        _seam.Dispose();
    }

    [Fact]
    public async Task Login_trusts_the_profiles_certificate_authority_bundle()
    {
        await AddProfileAsync();

        var result = await _seam.RunAsync(
            ["auth", "login", "work"], standardInput: VerbSeam.Token + "\n");

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("Ada Lovelace");

        File.Exists(_seam.Home.CredentialsFile).ShouldBeTrue();
    }

    [Fact]
    public async Task Status_trusts_the_profiles_certificate_authority_bundle()
    {
        await AddProfileAsync();

        (await _seam.RunAsync(["auth", "login", "work"], standardInput: VerbSeam.Token + "\n"))
            .ExitCode.ShouldBe(0);

        var result = await _seam.RunAsync(["auth", "status", "work"]);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("Ada Lovelace");
    }

    /// <summary>
    /// Not the seam's own step: the bundle is what these tests are about, and it has to be written
    /// and named on the same `profile add` that registers the profile.
    /// </summary>
    private async Task AddProfileAsync()
    {
        Directory.CreateDirectory(_seam.Home.Directory);

        var bundle = Path.Combine(_seam.Home.Directory, "corporate-ca.pem");

        await File.WriteAllTextAsync(
            bundle,
            _jira.CertificatePem,
            TestContext.Current.CancellationToken);

        var added = await _seam.RunAsync(
            ["profile", "add", "work", "--url", _jira.Url.ToString(), "--ca-bundle", bundle]);

        added.ExitCode.ShouldBe(0);
    }
}
