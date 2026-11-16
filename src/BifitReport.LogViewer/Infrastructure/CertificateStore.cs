using System.Security.Cryptography.X509Certificates;

namespace BifitReport.LogViewer.Infrastructure;

public static class CertificateStore
{
    public static X509Certificate2 LoadServerCertificate(string thumbprint)
    {
        var normalized = new string(thumbprint.Where(char.IsAsciiHexDigit).ToArray()).ToUpperInvariant();
        if (normalized.Length is not (40 or 64))
        {
            throw new InvalidOperationException("Server certificate thumbprint must be a SHA-1 or SHA-256 hexadecimal value.");
        }

        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var certificate = store.Certificates
            .Find(X509FindType.FindByThumbprint, normalized, validOnly: true)
            .OfType<X509Certificate2>()
            .FirstOrDefault(item => item.HasPrivateKey);
        return certificate ?? throw new InvalidOperationException(
            $"A valid server certificate with a private key was not found in LocalMachine/My: {normalized}");
    }
}
