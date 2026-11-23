# Changelog

## 1.2.2

- Kept all 13 sidebar destinations accessible in every mode. Added a demo OU browser, read-only management views, connection-specific appearance settings and explanations for restricted actions.
- Added an authorization shortcut on read-only management pages and permission guidance in the mode badge, account editor and onboarding wizard.
- Filtered live administration choices by policy, object protection and controller-advertised rights. Revoked outstanding write capabilities on policy change or failed access refresh.
- Added offline coverage for mode transitions, all sidebar destinations, read-only commands, scoped editing and revocation after a change has been prepared.

## 1.2.1

- Added `ApprovedOperatorGroups` to the machine policy. The configured operator must match LDAP Who Am I and have current direct and effective membership in a verified lab-domain security group.
- Kept operator roles separate from writable group GUIDs. Policies with an overlapping role and write target fail live authorization.
- Added regression coverage for removal of membership, mismatched principals, invalid and foreign SIDs, privileged groups and legacy policies.

## 1.2.0

- Added a six-step employee onboarding wizard with configurable department templates, secure temporary password input, explicit confirmation and per-step results.
- Added attribute editing, account enable/disable and confirmed direct membership changes for approved disposable lab users and groups.
- Added per-connection Live Administration Mode for the delegated AppTesting operator. The server checks permissions on each write. Demo Mode and live read-only browsing remain available.
- Added a private UTC administration journal, partial-failure handling, offline regression tests and desktop coverage for onboarding and editing.

## 1.1.1

- Fixed the Reports selector so both the open list and selected value display friendly dataset names.
- Added detailed security-group and organizational-unit datasets alongside the existing user and computer reports.
- Added **Counts Only (Privacy-Safe)** for six aggregate metrics in CSV and HTML, with JSON also available in Demo Mode.
- Added a field summary and explicit confirmation before writing each report. Live destination restrictions and CSV formula escaping apply to the new options.

## 1.1.0

- Added a Connection Manager for authenticated LDAPS and signed, sealed Kerberos LDAP from a Windows workgroup PC.
- Added a live read-only dashboard, paged inventory, user and computer attributes, direct group links and organizational-unit hierarchy.
- Added confirmed private CSV and HTML exports and in-memory live session activity.
- Preserved the v1.0 demo store and workflows. Added mock-provider, mode isolation, export and certificate validation tests.

## 1.0.0

Initial WPF release with a persistent fictional directory, demo administration workflows, bulk imports, reports, settings and audit history.
