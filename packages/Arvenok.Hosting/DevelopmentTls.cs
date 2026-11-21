using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Arvenok.Hosting;

public static class DevelopmentTls
{
    public static void Create(string directory)
    {
        Directory.CreateDirectory(directory);
        string cert = Path.Combine(directory, "localhost.crt"), keyPath = Path.Combine(directory, "localhost.key");
        if (File.Exists(cert) && File.Exists(keyPath)) return;
        using var key = RSA.Create(3072);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.KeyCertSign, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(cert, certificate.ExportCertificatePem());
    }
}
