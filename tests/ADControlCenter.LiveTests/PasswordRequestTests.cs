using System.DirectoryServices.Protocols;
using System.Security;
using ADControlCenter.Core.Live;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ADControlCenter.LiveTests;

[TestClass]
public sealed class PasswordRequestTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ResetSendsOneBinaryReplaceAndClearsItsBuffer(bool loseAcknowledgement)
    {
        const string dn = "CN=fixture,OU=AppTesting,DC=ad,DC=lab,DC=test";
        using var value = new SecureString();
        foreach (char unit in new[] { 'A', '\u0000', '"', '\u20AC', '\uD83D', '\uDE42' })
            value.AppendChar(unit);
        byte[]? submitted = null;
        int calls = 0;
        void Inspect(ModifyRequest request)
        {
            calls++;
            Assert.AreEqual(dn, request.DistinguishedName);
            Assert.AreEqual(1, request.Modifications.Count);
            var modification = request.Modifications[0];
            Assert.AreEqual("unicodePwd", modification.Name);
            Assert.AreEqual(DirectoryAttributeOperation.Replace, modification.Operation);
            Assert.AreEqual(1, modification.Count);
            submitted = (byte[])modification[0];
            byte[] expected = [34, 0, 65, 0, 0, 0, 34, 0, 0xAC, 0x20, 0x3D, 0xD8, 0x42, 0xDE, 34, 0];
            Assert.IsTrue(submitted.AsSpan().SequenceEqual(expected));
            if (loseAcknowledgement) throw new TimeoutException();
        }
        if (loseAcknowledgement)
            Assert.ThrowsExactly<TimeoutException>(() => LdapAdministration.SendPasswordReset(dn, value, Inspect));
        else
            LdapAdministration.SendPasswordReset(dn, value, Inspect);
        Assert.AreEqual(1, calls);
        Assert.IsNotNull(submitted);
        Assert.IsTrue(submitted.All(b => b == 0));
    }

    [TestMethod]
    [DataRow(ResultCode.UnwillingToPerform, "0000052D: diagnostic", "effective password policy", "LDAP 53, AD 0x0000052D")]
    [DataRow(ResultCode.ConstraintViolation, "0000216c: diagnostic", "unicodePwd encoding", "LDAP 19, AD 0x0000216C")]
    [DataRow(ResultCode.UnwillingToPerform, "0000202D: diagnostic", "confidentiality", "LDAP 53, AD 0x0000202D")]
    [DataRow(ResultCode.InsufficientAccessRights, "00002098: diagnostic", "denied", "LDAP 50, AD 0x00002098")]
    [DataRow(ResultCode.UnwillingToPerform, "0000001F: diagnostic", "declined", "LDAP 53, AD 0x0000001F")]
    [DataRow(ResultCode.ConstraintViolation, "diagnostic contains 0000052D", "constraint", "LDAP 19)")]
    [DataRow(ResultCode.UnwillingToPerform, "0000052Ddiagnostic", "declined", "LDAP 53)")]
    [DataRow(ResultCode.StrongAuthRequired, null, "protection", "LDAP 8)")]
    public void RejectionsExposeOnlyServerCodes(ResultCode result, string? diagnostic, string meaning, string codes)
    {
        var message = LdapAdministration.ControllerRejection(result, diagnostic).Message;
        StringAssert.Contains(message, meaning);
        StringAssert.Contains(message, codes);
        Assert.IsFalse(message.Contains("diagnostic", StringComparison.Ordinal));
    }
}
