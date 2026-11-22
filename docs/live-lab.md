# Connect from a Windows 11 workgroup PC

Launch the Windows x64 executable or run the WPF project using the build commands in the README. The application always opens in **DEMO MODE**. Connecting uses an explicitly entered lab-domain account with directory read permission.

## Verify the host path

From the Windows client, run these checks. The first query uses the client's configured DNS. The second asks the lab DNS server directly. Both should return the lab IP.

```powershell
Resolve-DnsName DC01.ad.lab.test -Type A
Resolve-DnsName DC01.ad.lab.test -Type A -Server 192.168.56.10
Test-NetConnection DC01.ad.lab.test -Port 636
```

If only the second lookup succeeds, configure the Windows host's lab DNS or split-DNS policy to send queries for `ad.lab.test` to `192.168.56.10`. Verify the first query again. Use the controller FQDN in the application so Windows can identify its certificate or Kerberos service principal.

| Transport | Client and lab prerequisites |
| --- | --- |
| LDAPS on 636 | The controller presents a current server-authentication certificate for its FQDN. Windows trusts its issuing chain, and required revocation information is available. The app validates the chain, purpose and hostname before the authenticated bind. |
| Kerberos on 389 | The Windows client can discover the lab KDC through DNS and reach it, clocks are synchronized, and the controller's LDAP service principal is registered. The app requests Kerberos with both LDAP signing and sealing. |

For Kerberos, verify `Resolve-DnsName _kerberos._tcp.ad.lab.test -Type SRV`, `Test-NetConnection DC01.ad.lab.test -Port 88` and `Test-NetConnection DC01.ad.lab.test -Port 389`. A successful TCP check establishes only that a listener accepted the connection. The authenticated connection test completes the protocol check. Transport selection is explicit. [Microsoft LDAPS certificate requirements](https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/configure-ldap-signing-certificates) · [Microsoft LDAP session security requirements](https://learn.microsoft.com/en-us/troubleshoot/windows-server/active-directory/ldap-session-security-settings-requirements-adv190023)

## Test, connect and disconnect

1. Open **Connection Manager** in the sidebar. Enter `DC01.ad.lab.test` and choose the transport supported by your lab.
2. Enter your lab account's actual user principal name and password. Select **Test connection**. Success requires authentication and a RootDSE read from an AD server. The test closes its connection and leaves Demo Mode active.
3. Re-enter the password and select **Connect read-only**. The dashboard switches to **LIVE READ-ONLY MODE** after all LDAP pages load. The password box clears after each attempt.
4. Open **Users**, **Computers**, **Groups** or **Organizational Units**. Search the last completed snapshot, filter by parent or account state, and use **Previous** and **Next** for 50-row pages. **View attributes** opens the full DN, available properties and direct membership links. The OU tree shows nested visible OUs. Column sorting applies to the displayed page.
5. Use **Refresh** to query AD again. A failed refresh retains the last complete snapshot and reports its timestamp. Counts cover objects visible to the authenticated account in the domain's default naming context.
6. **Reports** offers detailed CSV and HTML for users, security groups, computers and OUs. Choose **Counts Only (Privacy-Safe)** for six aggregate metrics. Review the selected dataset, fields and destination before confirming. The dedicated folder is `%LOCALAPPDATA%\AD Control Center\LiveExports`. The application rejects destinations under a Git worktree or linked directory.
7. Select **Connection Manager → Disconnect** to release the LDAP handle, discard live objects and session events, and return to your saved demo workspace.

The viewer displays direct `member` and `memberOf` links. It does not calculate nested effective authorization or infer primary-group membership. Computer last logon uses the replicated `lastLogonTimestamp` where present. Hidden and unavailable attributes can be empty.

## Manual lab acceptance

| Check | Expected observation |
| --- | --- |
| Authenticated test | RootDSE test succeeds with the intended lab account. The badge remains DEMO MODE. |
| Live inventory | The dashboard totals agree with a read-only directory browser using the same identity and scope. Find the IT, Finance, HR and Workstations OUs where visible. |
| Attributes and membership | Inspect a known lab user, group and computer. Compare direct memberships, account state, OU path and OS to your lab inventory. |
| Read-only boundary | Live navigation offers browsing and reports. Bulk import and account mutation controls are unavailable. |
| Refresh and disconnect | Refresh updates the snapshot timestamp. Disconnect restores the demo dashboard. Restart opens Demo Mode. |
| Privacy | Cancel an export and verify that no file appears. Confirm one export to check the private output, then handle it according to your lab policy. Use fictional screenshots for GitHub. |
| Failure cases | On an unavailable endpoint or untrusted LDAPS service, connection fails before live mode opens. Check a rejected sign-in only under the lab's normal account lockout policy. |

This checklist requires no domain controller configuration changes or AD writes.
