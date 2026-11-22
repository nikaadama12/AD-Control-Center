using System.DirectoryServices.Protocols;
using System.Globalization;
using ADControlCenter.Core.Admin;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ADControlCenter.Core.Live;

[SupportedOSPlatform("windows")]
public sealed class LdapConnector : ILiveConnector
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);
    public ILiveReader Open(Controller controller, NetworkCredential credential)
    {
        controller.Validate();
        bool tls = controller.Protection == Protection.Ldaps;
        try
        {
            var addresses = Dns.GetHostAddressesAsync(controller.Host).WaitAsync(Timeout).GetAwaiter().GetResult();
            if (addresses.Length == 0) throw new DirectoryException("DNS returned no address for the controller.");
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or TimeoutException)
        {
            throw new DirectoryException("The host could not resolve the controller hostname. Check the Windows client's lab DNS configuration.");
        }
        var id = new LdapDirectoryIdentifier(controller.Host, tls ? 636 : 389, true, false);
        var connection = new LdapConnection(id) { AuthType = tls ? AuthType.Basic : AuthType.Kerberos, AutoBind = false, Timeout = Timeout };
        bool invalidCertificate = false;
        try
        {
            var options = connection.SessionOptions;
            options.ProtocolVersion = 3;
            options.ReferralChasing = ReferralChasingOptions.None;
            options.AutoReconnect = false;
            if (tls)
            {
                options.SecureSocketLayer = true;
                options.VerifyServerCertificate = (_, cert) =>
                {
                    try
                    {
                        using var leaf = X509CertificateLoader.LoadCertificate(cert.GetRawCertData());
                        using var chain = new X509Chain();
                        bool valid = ValidateCertificate(leaf, controller.Host, chain);
                        invalidCertificate = !valid;
                        return valid;
                    }
                    catch (Exception) { invalidCertificate = true; return false; }
                };
            }
            else
            {
                options.Signing = true;
                options.Sealing = true;
            }
            using var secret = credential.SecurePassword;
            if (secret.Length == 0 || string.IsNullOrWhiteSpace(credential.UserName))
                throw new DirectoryException("Enter an explicit domain account and password.");
            connection.Bind(credential);
            var root = (SearchResponse)connection.SendRequest(new SearchRequest("", "(objectClass=*)", SearchScope.Base,
                "defaultNamingContext", "supportedCapabilities"), Timeout);
            if (root.Entries.Count != 1 || !LdapReader.Values(root.Entries[0], "supportedCapabilities").Contains("1.2.840.113556.1.4.800"))
                throw new DirectoryException("The authenticated endpoint did not identify itself as Active Directory.");
            var naming = LdapReader.Values(root.Entries[0], "defaultNamingContext").SingleOrDefault();
            if (string.IsNullOrWhiteSpace(naming)) throw new DirectoryException("The domain naming context is unavailable.");
            return new LdapReader(connection, naming);
        }
        catch (Exception ex)
        {
            connection.Dispose();
            if (invalidCertificate) throw new DirectoryException("Certificate verification failed. Check the controller FQDN, server authentication certificate, trusted CA and revocation availability.");
            if (ex is LdapException or DirectoryOperationException) throw Failure(ex);
            throw;
        }
    }

    internal static bool ValidateCertificate(X509Certificate2 leaf, string host, X509Chain chain)
    {
        const string serverAuth = "1.3.6.1.5.5.7.3.1";
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(serverAuth));
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.UrlRetrievalTimeout = Timeout;
        return leaf.MatchesHostname(host, allowWildcards: false) &&
            leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(e => e.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == serverAuth)) &&
            chain.Build(leaf);
    }

    internal static DirectoryException Failure(Exception ex) => new(ex switch
    {
        LdapException { ErrorCode: 49 } => "Authentication rejected. Verify the domain UPN and password, account state and selected authentication method.",
        LdapException { ErrorCode: 85 } or DirectoryOperationException { Response.ResultCode: ResultCode.TimeLimitExceeded } => "Directory request timed out. Check the controller and retry.",
        DirectoryOperationException { Response.ResultCode: ResultCode.InsufficientAccessRights } => "The account lacks permission to read this directory data.",
        LdapException { ErrorCode: 81 or 91 } => "Secure connection unavailable. Check host DNS, the selected port, TLS trust or Kerberos service availability.",
        _ => "The protected directory request failed. Verify the server, credentials, transport policy and account read access."
    });
}

[SupportedOSPlatform("windows")]
internal sealed class LdapReader(LdapConnection connection, string namingContext) : ILiveReader, IAdministrationSource
{
    public IAdminBackend Activate(LabPolicy policy, Func<IReadOnlyList<LiveObject>> snapshot, Func<bool> active) =>
        new LdapAdministration(connection, policy, snapshot, active);
    public string NamingContext => namingContext;
    public void Dispose() => connection.Dispose();
    internal static string[] Values(SearchResultEntry entry, string attribute) =>
        entry.Attributes[attribute]?.GetValues(typeof(string)).Cast<string>().ToArray() ?? [];

    private string[] ReadLinks(SearchResultEntry entry, string name, CancellationToken token)
    {
        List<string> links = [];
        var current = entry;
        int start = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            string prefix = $"{name};range={start}-";
            string? key = current.Attributes.AttributeNames.Cast<string>().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (key == null)
            {
                if (start == 0) return Values(entry, name);
                throw new DirectoryException("Membership changed while reading. Refresh the snapshot.");
            }
            links.AddRange(Values(current, key));
            if (key.EndsWith('*')) return links.ToArray();
            if (!int.TryParse(key.AsSpan(prefix.Length), out int end) || end < start || end == int.MaxValue)
                throw new DirectoryException("The server returned an invalid membership range.");
            start = end + 1;
            var response = (SearchResponse)connection.SendRequest(new SearchRequest(entry.DistinguishedName, "(objectClass=*)",
                SearchScope.Base, $"{name};range={start}-*"), LdapConnector.Timeout);
            if (response.Entries.Count != 1) throw new DirectoryException("Membership is unavailable. Refresh the snapshot.");
            current = response.Entries[0];
        }
    }

    public IReadOnlyList<LiveObject> Read(CancellationToken token)
    {
        List<LiveObject> objects = [];
        try
        {
            foreach (var (kind, filter) in new[] {
                (ObjectKind.Users, "(&(objectCategory=person)(objectClass=user))"), (ObjectKind.Computers, "(objectCategory=computer)"),
                (ObjectKind.Groups, "(objectCategory=group)"), (ObjectKind.OrganizationalUnits, "(objectClass=organizationalUnit)") })
            {
                var request = new SearchRequest(NamingContext, filter, SearchScope.Subtree, "name", "displayName", "sAMAccountName",
                    "userAccountControl", "msDS-User-Account-Control-Computed", "operatingSystem", "mail", "description", "dNSHostName",
                    "lastLogonTimestamp", "member", "memberOf", "groupType", "objectGUID", "givenName", "sn", "department", "title", "userPrincipalName");
                var paging = new PageResultRequestControl(250) { IsCritical = true };
                request.Controls.Add(paging);
                do
                {
                    token.ThrowIfCancellationRequested();
                    var result = (SearchResponse)connection.SendRequest(request, LdapConnector.Timeout);
                    foreach (SearchResultEntry entry in result.Entries)
                    {
                        string Get(string a) => Values(entry, a).FirstOrDefault() ?? "";
                        string status = "";
                        if (kind is ObjectKind.Users or ObjectKind.Computers)
                        {
                            bool flagsRead = int.TryParse(Get("userAccountControl"), out int flags);
                            bool computedRead = int.TryParse(Get("msDS-User-Account-Control-Computed"), out int computed);
                            status = !flagsRead ? "Unknown" : (flags & 2) != 0 ? "Disabled" : computedRead && (computed & 16) != 0 ? "Locked" : "Enabled";
                        }
                        long.TryParse(Get("lastLogonTimestamp"), CultureInfo.InvariantCulture, out long time);
                        string display = Get("displayName");
                        long.TryParse(Get("groupType"), CultureInfo.InvariantCulture, out long groupType);
                        objects.Add(new(kind, display.Length > 0 ? display : Get("name"), entry.DistinguishedName, Get("sAMAccountName"),
                            status, Get("operatingSystem"), Get("mail"), Get("description"), Get("dNSHostName"),
                            time > 0 && time <= DateTime.MaxValue.ToFileTimeUtc() ? DateTimeOffset.FromFileTime(time).ToUniversalTime() : null,
                            ReadLinks(entry, "member", token), ReadLinks(entry, "memberOf", token))
                        {
                            Id = entry.Attributes["objectGUID"]?.GetValues(typeof(byte[])).FirstOrDefault() is byte[] guid && guid.Length == 16 ? new Guid(guid) : Guid.Empty,
                            EmployeeInfo = kind == ObjectKind.Users ? new(Get("givenName"),Get("sn"),display,Get("sAMAccountName"),Get("userPrincipalName"),
                                Get("department"),Get("title"),Get("mail"),Get("description")) : null,
                            GroupScope = kind != ObjectKind.Groups ? "" : (groupType & 8) != 0 ? "Universal" : (groupType & 4) != 0 ? "Domain local" : "Global",
                            IsSecurityGroup = kind == ObjectKind.Groups && (groupType & 0x80000000L) != 0
                        });
                    }
                    paging.Cookie = result.Controls.OfType<PageResultResponseControl>().SingleOrDefault()?.Cookie
                        ?? throw new DirectoryException("The server did not acknowledge paging. The partial snapshot was discarded.");
                } while (paging.Cookie.Length > 0);
            }
            return objects.AsReadOnly();
        }
        catch (Exception ex) when (ex is LdapException or DirectoryOperationException) { throw LdapConnector.Failure(ex); }
    }
}
