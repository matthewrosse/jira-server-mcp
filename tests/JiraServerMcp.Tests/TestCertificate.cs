using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace JiraServerMcp.Tests;

/// <summary>
/// A certificate in the PEM form a certificate authority bundle takes, for the tests that need a
/// bundle holding something real rather than a file with the right extension.
/// </summary>
internal static class TestCertificate
{
    public static string Pem()
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=corporate-ca",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, false, 0, true));

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        return certificate.ExportCertificatePem();
    }
}
