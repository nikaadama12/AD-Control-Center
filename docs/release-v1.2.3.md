# AD Control Center 1.2.3

## Windows x64

Download `ADControlCenter-1.2.3-win-x64.exe` and `SHA256SUMS.txt` into the same folder. In PowerShell, run `Get-FileHash .\ADControlCenter-1.2.3-win-x64.exe -Algorithm SHA256` and compare the result with the checksum file. Launch the executable as a standard Windows user. It includes the desktop runtime and starts in Demo Mode.

## Onboarding changes

Initial password assignment sends one LDAP replacement of the binary `unicodePwd` value over the existing protected connection. The encoding preserves the full sequence of UTF-16 code units between the required quotes. The application clears its temporary managed and unmanaged password buffers when submission finishes or throws. An interrupted request with an uncertain outcome still requires reconciliation before retry.

When the controller rejects a request, the application reports the LDAP result and, when available, the numeric AD status. It can distinguish password-policy rejection, malformed quoting, insufficient transport protection and missing delegation. The journal omits free-form server diagnostics. Live connections still start read-only, and live administration requires the existing operator, policy, scope and confirmation checks.

## Isolated lab acceptance

On 23 November 2026 the patched v1.2.2 backend completed an approved onboarding in an isolated Windows Server 2025 domain using signed and sealed Kerberos. A second authenticated connection read the account after each step.

| Operation | Independent readback |
| --- | --- |
| Create the disposable account | Account disabled, initial password unset, no direct staff-group membership |
| Set the initial password | Password-last-set value advanced while the account remained disabled |
| Add the approved membership | Membership appeared on both the account and the target group |
| Apply account settings | Account remained disabled with password change required at next logon |

The earlier, partially onboarded account retained its object identifier and change sequence throughout that test. Its original rejection lacked the server status needed to establish the cause. The release includes the source that reproduced the accepted patched executable byte for byte. The private receipt and directory identifiers are excluded from the release.

## Regression coverage

Two new cases inspect the binary password request and buffer cleanup after success and a simulated lost acknowledgement. Eight cases check diagnostic classification and redaction. The full suite also covers read-only boundaries, policy revocation, scoped administration, demo operations and report privacy.

## Release verification

The v1.2.3 Windows x64 Release build completed with zero warnings and errors. All 137 automated cases passed (25 core and 112 Windows cases), including all ten new regressions. Seventeen existing read-only and reporting cases also passed when rerun against the application assemblies extracted from the final executable.

The packaged desktop passed the end-to-end Demo Mode smoke test. A separate live read-only session browsed users, computers, groups and OUs, exercised user search, checked that management writes were disabled, refreshed the controller snapshot and disconnected back to Demo Mode. This release run performed no Active Directory writes.

Executable SHA-256: `E08881FFCFDC41A6B028ABE7BE38A47DD2C7A9284C96CE471A3F781BF2E2F57B`.
