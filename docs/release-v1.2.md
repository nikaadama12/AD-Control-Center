# AD Control Center 1.2.0

This Windows x64 release adds employee onboarding and scoped account administration to the existing WPF desktop application.

## What changed

- Practice the six-step onboarding wizard with fictional data, then review each account, password setup and group assignment outcome.
- Compare and confirm changes to employee attributes, account state and direct group memberships.
- Activate Live Administration Mode explicitly for one protected LDAP connection, using a dedicated delegated operator and a machine-owned AppTesting policy.
- Inspect private administration history with UTC timestamps, before/after values and sanitized errors. An unconfirmed operation stops further steps.

## Windows package

The local release directory is `artifacts\release-v1.2.0\`. It contains the self-contained `ADControlCenter-1.2.0-win-x64.exe` and `SHA256SUMS.txt`. Run the executable on Windows 11 x64 as your usual user. A fresh workspace starts in Demo Mode. Existing demo workspaces reopen automatically.

To verify the packaged executable from the release directory:

```powershell
$asset = 'ADControlCenter-1.2.0-win-x64.exe'
$expected = ((Get-Content -LiteralPath .\SHA256SUMS.txt) -split '\s+')[0]
if ((Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash -ne $expected) {
    throw 'Release checksum mismatch'
}
.\ADControlCenter-1.2.0-win-x64.exe
```

## Verification

The Release solution built for x64 with zero warnings or errors. All 101 automated tests passed, including the 59 original tests and 42 wizard, provider, validation, authorization and partial-failure cases. The Windows desktop smoke test exercises the application in an isolated fictional workspace. Live Read-Only Mode and administration coverage use mock providers and synthetic records. A real bind, password assignment, controller ACL decision and AD mutation require separate manual lab verification.

The local v1.1.1 checkpoint is `checkpoint/v1.1.1-before-v1.2`. The intended release tag is `v1.2.0`. GitHub publication requires the maintainer's approval.

[Onboarding and manual acceptance](onboarding.md) · [Delegation and machine policy](delegated-administration.md) · [Secure connection setup](live-lab.md)
