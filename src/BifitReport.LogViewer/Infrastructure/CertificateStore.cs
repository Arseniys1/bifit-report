using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BifitReport.LogViewer.Infrastructure;

public static class CertificateStore
{
    public static X509Certificate2 LoadServerCertificate(string thumbprint)
    {
        var normalized = NormalizeThumbprint(thumbprint);

        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var certificate = store.Certificates
            .Find(X509FindType.FindByThumbprint, normalized, validOnly: true)
            .OfType<X509Certificate2>()
            .FirstOrDefault(item => item.HasPrivateKey && SupportsServerAuthentication(item));
        return certificate ?? throw new InvalidOperationException(
            $"A valid server-authentication certificate with a private key was not found in LocalMachine/My: {normalized}");
    }

    public static string NormalizeThumbprint(string thumbprint)
    {
        var normalized = new string(thumbprint.Where(character => !char.IsWhiteSpace(character) && character != ':').ToArray()).ToUpperInvariant();
        if (normalized.Length != 40 || normalized.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new InvalidOperationException("Server certificate thumbprint must contain exactly 40 hexadecimal characters.");
        }
        return normalized;
    }

    private static bool SupportsServerAuthentication(X509Certificate2 certificate)
    {
        var enhancedKeyUsage = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        return enhancedKeyUsage is null || enhancedKeyUsage.EnhancedKeyUsages
            .OfType<Oid>()
            .Any(usage => usage.Value == "1.3.6.1.5.5.7.3.1");
    }
}
