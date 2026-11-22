# AD Control Center v1.1.1

**Windows x64 · Self-contained executable**

Reports now displays friendly dataset labels in the open and closed selector. Choose **User accounts**, **Security groups**, **Computers**, or **Organizational units** for a detailed export. The confirmation shows the included fields, file format and destination.

Choose **Counts Only (Privacy-Safe)** for a shareable summary containing six labeled integers: total users, enabled users, disabled users, total groups, total computers and total OUs. The summary uses CSV or HTML in either mode, with JSON also available in Demo Mode. It derives all six values from the current inventory and excludes directory identifiers and credentials.

## Run

Launch `ADControlCenter-1.1.1-win-x64.exe` as your usual Windows user. The app starts in Demo Mode and opens an existing demo workspace when available. Connection Manager offers validated LDAPS and signed, sealed Kerberos LDAP for live read-only use.

## Verification

All 39 existing tests and 20 new reporting regressions passed locally. Coverage includes the themed selector, all four detailed datasets, aggregate contents, Git destination restrictions and cancellation in both modes. Live checks used a mock provider and synthetic records. The Windows desktop smoke test exercised the demo workflows and inspected the aggregate CSV.

## Release preparation

The local v1.1 checkpoint is `checkpoint/v1.1-before-v1.1.1` at `0c89ca2`. The staged release files are `ADControlCenter-1.1.1-win-x64.exe` and `SHA256SUMS.txt` under `artifacts/release-v1.1.1/`. Compare the digest with `Get-FileHash -Algorithm SHA256` before attaching the executable to a release.

Approval is pending for the GitHub push and publication. Use these notes and the staged files for the approved release. [Build and reporting guide](../README.md) · [Changelog](../CHANGELOG.md)
