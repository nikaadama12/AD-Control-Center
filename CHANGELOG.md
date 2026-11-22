# Changelog

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
