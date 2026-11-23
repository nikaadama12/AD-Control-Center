# AD Control Center 1.2.1

This patch lets a dedicated operator use a machine-approved delegation group for Live Administration. Demo Mode starts offline. Every live connection opens read-only and requires an explicit confirmation before administration is enabled.

## Upgrade

Add `ApprovedOperatorGroups` to the machine-owned `%ProgramData%\AD Control Center\administration.json`. Use verified domain security-group SIDs. Preserve existing writable group GUIDs, approved user GUIDs, OUs, templates and file permissions. Older policies remain readable and require migration for administration. [Policy and delegation instructions](delegated-administration.md)

## Authorization

The protected LDAP session verifies the configured operator identity and trusted domain. Each approved operator SID must resolve uniquely to an ordinary security group. Direct membership and current effective group evidence must agree. Missing, partial or inconsistent evidence denies administration. The provider repeats the checks before writes. The controller continues to enforce its own ACLs.

## Windows x64 package

The local package is `artifacts\release-v1.2.1\ADControlCenter-1.2.1-win-x64.exe`. Compare `Get-FileHash -Algorithm SHA256` against the accompanying `SHA256SUMS.txt`. Launch as a standard Windows user, connect read-only, then review the per-connection administration confirmation.

## Verification scope

All 118 automated tests passed on Windows, including all 101 v1.2.0 cases and 17 new cases. The Release solution built for x64 with zero warnings and errors. Automated regression tests cover the fictional directory, read-only mode, onboarding, report privacy and operator authorization. Live validation for this patch exercises authentication and authorization using directory reads. Actual user creation, password assignment and membership modification require a separately authorized lab test. [Testing guide](testing.md)
