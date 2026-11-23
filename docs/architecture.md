# Architecture

## Composition and responsibilities

The application starts in `src/ADControlCenter.App/App.xaml.cs`. The composition root chooses a per-user storage folder, creates a single `DemoDirectoryService`, and injects its directory and workspace interfaces plus an `IInteraction` implementation into `ShellViewModel`.

| Component | Responsibility |
| --- | --- |
| `ADControlCenter.Core/Models.cs` | Typed directory records, import proposals, report types and service contracts. IDs remain stable when a record is renamed. |
| `DemoDirectoryService` | Validate and apply mutations, preserve references, serialize commits and persist the associated audit event. |
| `SampleDirectory` | Construct fictional accounts, operating systems, organizational units, memberships and illustrative history. |
| `CsvImport` / `ReportWriter` | Parse quoted CSV records and produce bounded previews. Encode output for each report format and create uniquely named files. |
| `Presentation/ShellViewModel` | Coordinate navigation, command eligibility, confirmation, validation feedback and refresh after a mutation. |
| `Presentation/Pages` | Compute dashboard aggregates, maintain filtered collection views and hold report or settings choices. |
| `Views` | Render XAML templates and charts, adapt native file/folder pickers, present editor forms and apply theme resources. |
| `ADControlCenter.Tests` | Exercise the actual provider in isolated temporary directories. |
| `scripts/Test-Desktop.ps1` | Drive the built executable through Windows UI Automation and optionally capture its window. |

WPF chooses page templates by view-model type. Shared directory grids build their columns from the selected entity type and use a `ListCollectionView` for sorting and combined filters. The UI keeps a selection by record ID when the data is refreshed. Dialogs return decisions and draft values. The provider owns domain validation, so changes arriving through imports and forms follow the same username rules.

## Persistence

The workspace uses a versioned JSON snapshot, an exclusive workspace lease and a process-local lock. A successful edit prepares a replacement state and an audit entry, writes both to a temporary file in the same directory, flushes the file, then replaces the stored snapshot. Only a successful replacement publishes the new in-memory state. Public snapshots use read-only collections.

A revision changes after directory or preference updates. Previewing an import captures the revision and validates against existing users and earlier CSV rows. Applying the preview checks the revision, validates every row again, and commits the batch once. Audit-only reads, searches and navigation leave the revision stable.

Reports use unique filenames and a temporary file before publication. CSV export neutralizes leading spreadsheet formula characters, JSON uses the platform serializer and HTML encodes field values.

## Provider extension point

`IDirectoryService` exposes an inventory snapshot, user edits, explicit account actions, group membership edits and import preview/apply. `IDemoWorkspace` owns the local audit, preferences, reports and demo-only clear/restore operations. A future provider can implement directory operations independently of the sample-reset mechanism.

Version 1.1 also injects an `ILiveConnector`. An authenticated connection returns an `ILiveReader`, which exposes inventory reads and disposal. The Windows LDAP adapter binds explicitly, discovers the domain naming context and issues search requests with paging controls. Referrals and automatic reconnects are disabled. Large membership attributes are read in ranges.

The shell serializes network operations and publishes a new snapshot only after all pages succeed. One ordered navigation collection serves all three modes. The shell chooses a page model for the current session and gates commands separately. Read-only management uses the inventory reader. An administration form is bound to the workflow that issued it. The live backend independently checks its connection and policy before preparation and submission. A failed test or incomplete initial load keeps the app in Demo Mode. Disconnect discards the session's objects and audit entries. Export uses the existing encoders and a dedicated private folder.

## Scoped administration

The shell injects an `AdminWorkflow` into the onboarding and account editor page models. `IAdminBackend` supplies a catalog and prepares one typed change at a time. The demo backend uses the existing workspace commit path. The LDAP reader exposes a separate, connection-bound administration capability after the operator and policy are verified. Disabling administration or disconnecting invalidates the capability.

Each prepared change carries the target and before/after values. The workflow flushes intent to the private journal before calling the provider. The live provider repeats its identity, location, membership and precondition checks immediately before sending the typed request on the existing secured connection. The controller evaluates its own access controls. Acknowledgement, denial and indeterminate transport outcomes are recorded individually. Setup and group updates span multiple requests. An interruption can leave a disabled account or partially applied memberships.

## Operating envelope

The synchronous snapshot store is intended for a single operator and a small fictional directory. Each commit rewrites the JSON file, including the audit history. The stale-device calculation uses elapsed UTC time. Dashboard account states are mutually exclusive: disabled accounts appear under Disabled, enabled locked accounts under Locked, and the remaining accounts under Active.
