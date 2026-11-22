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

A live provider would also need its own composition root configuration, authorization model, account-operation semantics, partial-failure handling and integration tests. Version 1.0 always composes the local demo implementation.

## Operating envelope

The synchronous snapshot store is intended for a single operator and a small fictional directory. Each commit rewrites the JSON file, including the audit history. The stale-device calculation uses elapsed UTC time. Dashboard account states are mutually exclusive: disabled accounts appear under Disabled, enabled locked accounts under Locked, and the remaining accounts under Active.
