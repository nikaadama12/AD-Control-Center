# Demo operations

## Import fictional users

Use a UTF-8 CSV file with exactly three headers. Header order is flexible and matching ignores case.

```csv
DisplayName,Username,Department
Juniper Cove,juniper.cove,Design
"Vale, Ember",ember.vale,Finance
```

Choose **Bulk Operations → Choose CSV**. The preview shows the CSV line, normalized username, display name, organizational unit and validation result. Applying a valid batch creates enabled users. Cancelling the confirmation leaves the preview available.

- Usernames accept 1–32 letters, digits, periods, underscores or hyphens and must begin with a letter or digit. They are normalized to lowercase and compared without case.
- Display names accept 1–80 characters and organizational units accept 1–64. Both must contain text and fit on a single line.
- A CSV can contain up to 5,000 user rows and 2,000,000 bytes. Existing usernames, duplicates within the file and rows with missing or extra fields block the entire batch.
- Malformed quoting, unexpected headers and empty files produce a file-level validation message. Correct the file and choose it again.
- If a directory record or saved preference changes while a preview is open, reselect the CSV to validate against the new revision.

The repository's [sample file](../samples/users.csv) contains invented identities. Use fictional inputs throughout the workspace.

## Account and group operations

Select a row to open its details. An account can be enabled when disabled, disabled when enabled, and unlocked when locked. A password-reset simulation changes only the stored last-simulation timestamp. Each account command names its target in a confirmation dialog.

Group editing uses a searchable list of users. Each check adds a member to the draft. Unchecking an existing member requires confirmation when saving. Cancelling the editor discards the draft.

## Export and recovery

Report filenames include a dataset name, UTC timestamp and unique identifier. A report captures the complete selected dataset, independently of the current table filters. Choose an absolute, writable destination in Settings and save the preference.

Clear removes all fictional users, devices, groups and memberships. Restore replaces them with fresh sample records and IDs. Both operations require confirmation and preserve settings, audit events and previously exported files.

Close the application before backing up its workspace. Copy `directory.json` to preserve the directory, preferences and audit history. If startup reports malformed or unsupported data, retain a copy of the original file before replacing it with a known-good backup. To inspect a fresh sample workspace independently, set `ADCC_DEMO_HOME` to a new local directory and start the app. A workspace-busy message indicates that another instance may already hold the lease.
