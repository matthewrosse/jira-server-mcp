using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace JiraServerMcp.Tests.Support;

/// <summary>
/// A minimal HTTPS endpoint with a certificate this test owns, so a private certificate authority
/// can be exercised for real rather than simulated. WireMock.Net serves its own generated
/// certificate and does not hand out the material needed to trust it.
/// </summary>
internal sealed class TlsTestServer : IDisposable
{
    private readonly string _response;
    private readonly TcpListener _listener;
    private readonly X509Certificate2 _certificate;
    private readonly CancellationTokenSource _stopping = new();

    private TlsTestServer(X509Certificate2 certificate, string body)
    {
        _certificate = certificate;
        _response = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
                    + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n"
                    + $"Connection: close\r\n\r\n{body}";
        _listener = new TcpListener(IPAddress.Loopback, 0);

        _listener.Start();

        Url = new Uri($"https://localhost:{((IPEndPoint)_listener.LocalEndpoint).Port}", UriKind.Absolute);

        _ = AcceptAsync();
    }

    public Uri Url { get; }

    /// <summary>The server's own certificate, in the PEM form a CA bundle takes.</summary>
    public string CertificatePem => _certificate.ExportCertificatePem();

    /// <summary>
    /// Answers every request with <paramref name="body"/>. The default is enough for a test that
    /// only cares whether the handshake happened; a test driving a verb that reads the response
    /// passes the JSON that verb expects.
    /// </summary>
    public static TlsTestServer StartWithASelfSignedCertificate(string body = "ok")
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=localhost",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, false, 0, true));

        var names = new SubjectAlternativeNameBuilder();

        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());

        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        // macOS refuses to use an ephemeral key for a TLS server, so the certificate makes a
        // round trip through PKCS#12 to get a persisted one.
        return new TlsTestServer(
            X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), password: null),
            body);
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Stop();
        _stopping.Dispose();
        _certificate.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                await using var stream = new SslStream(client.GetStream());

                await stream.AuthenticateAsServerAsync(_certificate, false, false);

                var request = new byte[4096];

                await stream.ReadAtLeastAsync(request, 1, false, _stopping.Token);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(_response), _stopping.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException
                                                  or AuthenticationException
                                                  or SocketException
                                                  or ObjectDisposedException)
            {
                // A client that rejected the certificate hangs up mid-handshake, which is the
                // point of one of these tests.
            }
        }
    }
}
