using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace JiraServerMcp.Jira.Authentication;

/// <summary>
/// A profile's optional CA bundle, added as a private root for that profile alone. There is no
/// counterpart that skips verification, and adding one is out of scope permanently.
/// </summary>
internal static class PrivateCertificateAuthority
{
    public static Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>
        TrustingBundleAt(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"This profile's certificate authority bundle '{path}' is not there. Point the "
                + "profile at the bundle again with 'jira-server-mcp profile add'.");
        }

        var roots = new X509Certificate2Collection();

        roots.ImportFromPemFile(path);

        // An empty trust store rejects every certificate, and the handshake error says nothing
        // about the file that caused it.
        if (roots.Count is 0)
        {
            throw new InvalidOperationException(
                $"This profile's certificate authority bundle '{path}' holds no certificate. A "
                + "bundle is one or more PEM CERTIFICATE blocks.");
        }

        return (_, certificate, chain, errors) => IsTrusted(roots, certificate, chain, errors);
    }

    private static bool IsTrusted(
        X509Certificate2Collection roots,
        X509Certificate2? certificate,
        X509Chain? chain,
        SslPolicyErrors errors)
    {
        if (errors is SslPolicyErrors.None)
        {
            return true;
        }

        // The bundle vouches for the issuer, never for the name: a certificate issued for another
        // host is still the wrong certificate.
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch)
            || certificate is null
            || chain is null)
        {
            return false;
        }

        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Clear();
        chain.ChainPolicy.CustomTrustStore.AddRange(roots);

        return chain.Build(certificate);
    }
}
