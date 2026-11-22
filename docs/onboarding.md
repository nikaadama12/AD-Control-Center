# Employee onboarding and account administration

Open **Onboarding** in Demo Mode for an offline rehearsal. Demo objects and templates stay in the local workspace. For a live rehearsal, first review the [lab delegation and policy](delegated-administration.md). Every live connection starts in read-only mode. Each real change requires prior authorization for that specific lab test.

## Walk through the wizard

| Step | Operator action |
| --- | --- |
| 1. Employee information | Enter first name, surname, display name, sAMAccountName, UPN, department and job title. Email and description are optional. Use a fictional identity. The UPN must match the active directory domain. Duplicate usernames or UPNs block creation. |
| 2. Destination OU | Select a visible approved destination. In the live lab, use AppTesting or an explicitly approved child OU. |
| 3. Group membership | Select any approved security groups or apply the department template. A template replaces the checklist with that department’s choices. Review the displayed reason for each selected group. |
| 4. Account settings | Enter a temporary password in the masked field. Change at next logon is selected initially. The account stays disabled unless you choose to enable it after setup. The live controller enforces its password policy. |
| 5. Review and confirm | Check the identity, destination, group names and reasons, final enabled state and next-logon choice. The mode notice identifies the destination. Confirm the creation dialog to begin. Cancelling discards the temporary password. |
| 6. Results | Inspect each step and target. **Open account details** shows the created account when its identity is available. The selectable sanitized summary contains operation names, outcomes and status messages. Keep detailed targets and the private journal inside the lab. |

In Demo Mode the temporary password is simulated. The saved user stores a timestamp. To override a sample department template, place `demo-templates.json` beside `directory.json` and reopen the wizard. For example:

```json
{ "IT": ["Engineering Team"], "Finance": ["Finance Team"], "HR": ["People Team"] }
```

Keys are department names. Values are names of existing fictional groups. Live templates refer to approved group GUIDs in the machine policy.

## Edit an existing account or membership

Open **Account administration**. Search by username, display name, department or OU, then select a disposable account. Review its attributes, enabled state and current approved memberships. Search the group list and choose **Direct members** to inspect a group's members and scope. Select or clear memberships, then read the additions and removals under **Proposed membership changes**. **Review and apply…** opens the confirmation dialog. The existing **Groups** browser shows the broader read-only inventory.

## Read results and recover from interruption

The wizard creates a disabled user, assigns the temporary password, applies each selected membership, then commits the account settings. The editor applies attribute changes, membership removals and additions, and the requested state change in order. Each step needs its own acknowledgement. An error stops the sequence. Earlier acknowledged changes remain in effect.

- **Failed** means preparation failed or the provider rejected the change. Later operations are skipped.
- **Unconfirmed** means acknowledgement or identity readback was lost. Refresh and independently check the user, account flags and direct memberships before acting again.
- **Pending** without a later outcome, or **Audit incomplete**, requires a manual check of the directory and AD security events. The application does not retry or roll back writes automatically.

**Administration history** displays up to 2,000 recent entries from the local journal. Entries include UTC, the operator, operation ID, target, previous state, requested state, outcome and sanitized detail. Live entries go to `%LOCALAPPDATA%\AD Control Center\Administration\audit.jsonl`. Demo entries use `demo-administration.jsonl` beside the workspace. These files can contain account names, DNs and employee attributes. Restrict access, manage retention locally and keep them out of Git. The summary is suitable for sharing after review. The local journal does not replace Active Directory security auditing.

## Manual lab acceptance

1. Agree on the exact disposable identity, OU, approved group and desired state for a single test. Obtain explicit approval before the first real mutation. Use Demo Mode while authorization is pending.
2. Connect through **Connection Manager** using `DC01.ad.lab.test`, Kerberos signing and sealing, and the delegated operator credential. A successful initial connection shows **LIVE READ-ONLY MODE**. Verify the fixture OU and group in the browser.
3. Open **Enable live administration…** and review its confirmation. Verify the red **LIVE ADMINISTRATION MODE** indicator and operator. Open the onboarding wizard and compare the displayed OU and group choices to the policy.
4. If this specific creation was approved, enter the agreed fictional account, use a fresh temporary password, review all choices and confirm once. Compare per-step outcomes with an independent read-only AD view and the lab’s security log. Keep the account disabled unless enabling it was explicitly part of the test.
5. Obtain separate approval for any membership removal, attribute edit or enabled-state change beyond the original test. Review and apply the exact delta. Verify unrelated memberships remain intact. A denied-write test also requires prior approval and a fixture with the appropriate restricted ACL.
6. Select **Return to read-only**, then **Disconnect**. Reconnect and verify read-only mode is restored. Coordinate cleanup with the lab owner under separate authorization. The application exposes no live delete action.
