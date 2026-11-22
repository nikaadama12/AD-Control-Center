using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ADControlCenter.Core.Live;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADControlCenter.LiveTests;

[TestClass]
public sealed class CertificateTests
{
    [TestMethod]
    [DataRow("dc.fixture.example", true, true, false, true)]
    [DataRow("other.fixture.example", true, true, false, false)]
    [DataRow("dc.fixture.example", false, true, false, false)]
    [DataRow("dc.fixture.example", true, false, false, false)]
    [DataRow("dc.fixture.example", true, true, true, false)]
    public void ValidatesHostPurposeTrustAndLifetime(string host, bool trust, bool serverAuth, bool expired, bool expected)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=dc.fixture.example", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("dc.fixture.example");
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
        {
            new Oid(serverAuth ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2")
        }, false));
        using var leaf = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow.AddDays(expired ? -1 : 1));
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        if (trust) chain.ChainPolicy.CustomTrustStore.Add(leaf);
        Assert.AreEqual(expected, LdapConnector.ValidateCertificate(leaf, host, chain));
    }
}
