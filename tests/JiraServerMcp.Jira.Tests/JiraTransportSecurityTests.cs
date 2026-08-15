using Microsoft.Extensions.DependencyInjection;

namespace JiraServerMcp.Jira.Tests;

/// <summary>
/// The personal access token is a bearer secret with nothing else protecting it in transit, so
/// the transport carries the whole weight: HTTPS everywhere but loopback, a private certificate
/// authority per profile, and no way to turn verification off.
/// </summary>
public sealed class JiraTransportSecurityTests : IDisposable
{
    private readonly List<ServiceProvider> _providers = [];
    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        foreach (var file in _files)
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("http://jira.example.com")]
    [InlineData("http://192.168.1.10:8080")]
    public void Plain_http_is_refused(string baseUrl)
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateClient(new Uri(baseUrl, UriKind.Absolute)));

        exception.Message.ShouldContain("HTTPS");
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:8080")]
    public void Loopback_over_plain_http_is_allowed(string baseUrl)
    {
        // There is no network to intercept, and a developer running Jira locally should not be
        // asked to provision a certificate for it.
        CreateClient(new Uri(baseUrl, UriKind.Absolute)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_profile_with_a_private_certificate_authority_connects()
    {
        using var jira = TlsTestServer.StartWithASelfSignedCertificate();

        var client = CreateClient(jira.Url, CaBundle(jira.CertificatePem));

        using var response = await client.GetAsync("read", TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task A_certificate_no_bundle_vouches_for_fails()
    {
        using var jira = TlsTestServer.StartWithASelfSignedCertificate();
        using var somebodyElse = TlsTestServer.StartWithASelfSignedCertificate();

        var client = CreateClient(jira.Url, CaBundle(somebodyElse.CertificatePem));

        await Should.ThrowAsync<HttpRequestException>(
            () => client.GetAsync("read", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_certificate_nothing_vouches_for_fails()
    {
        using var jira = TlsTestServer.StartWithASelfSignedCertificate();

        var client = CreateClient(jira.Url);

        await Should.ThrowAsync<HttpRequestException>(
            () => client.GetAsync("read", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_bundle_holding_no_certificate_is_refused_rather_than_trusted_emptily()
    {
        // An empty trust store rejects every certificate, and the handshake error it produces
        // says nothing about the file that caused it.
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateClient(
                new Uri("https://jira.example.com", UriKind.Absolute),
                CaBundle("not a certificate")));

        exception.Message.ShouldContain("jira-ca-");
    }

    [Fact]
    public void A_bundle_that_is_not_there_is_refused_by_name()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no-such-bundle.pem");

        var exception = Should.Throw<InvalidOperationException>(
            () => CreateClient(new Uri("https://jira.example.com", UriKind.Absolute), missing));

        exception.Message.ShouldContain("no-such-bundle.pem");
    }

    [Fact]
    public void Nothing_on_the_options_can_switch_certificate_validation_off()
    {
        // Permanently out of scope: an insecure shortcut would be pasted into every teammate's
        // configuration within a week.
        var settings = typeof(JiraClientOptions).GetProperties().Select(property => property.Name);

        settings.ShouldNotContain(
            name => name.Contains("Insecure", StringComparison.OrdinalIgnoreCase)
                || name.Contains("SkipCertificate", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Validation", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Verify", StringComparison.OrdinalIgnoreCase));
    }

    private string CaBundle(string certificatePem)
    {
        var path = Path.Combine(Path.GetTempPath(), $"jira-ca-{Guid.NewGuid():N}.pem");

        File.WriteAllText(path, certificatePem);
        _files.Add(path);

        return path;
    }

    private HttpClient CreateClient(Uri baseUrl, string? caBundlePath = null)
    {
        var services = new ServiceCollection();

        services.AddJiraClient();
        services.Configure<JiraClientOptions>(options =>
        {
            options.BaseUrl = baseUrl;
            options.PersonalAccessToken = "s3cr3t-personal-access-token";
            options.CaBundlePath = caBundlePath;
        });

        var provider = services.BuildServiceProvider();

        _providers.Add(provider);

        return provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(JiraClient));
    }
}
