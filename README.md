# AD Control Center

AD Control Center **1.2.0** is a Windows desktop portfolio project for practicing directory administration. It starts in **DEMO MODE**. A live connection opens in **LIVE READ-ONLY MODE**. A delegated operator can explicitly enable **LIVE ADMINISTRATION MODE** for the isolated AppTesting lab. The demo opens a fictional **northstar.example** workspace with **128 users, 84 computers, and 12 security groups**. Explore account health, review stale devices, simulate support tasks, and export the results from a standard Windows user session.

![AD Control Center dashboard showing the sample directory and its account health](docs/screenshots/dashboard.png)

## Run on Windows 11

Build from source with the **.NET 10 SDK**. Run these PowerShell commands from the solution directory.

```powershell
dotnet restore ADControlCenter.sln
dotnet build ADControlCenter.sln -c Release --no-restore
dotnet run --project src/ADControlCenter.App -c Release --no-build
```

The first launch creates the sample directory. Subsequent launches reopen your saved workspace. Demo Mode runs offline after build or publication. The initial build restores NuGet dependencies.

To produce a self-contained Windows x64 executable with the desktop runtime included, run the following command. Launch `artifacts\win-x64\ADControlCenter.exe` as your usual Windows user.

```powershell
dotnet publish src/ADControlCenter.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o artifacts/win-x64
```

## Connect to a live directory

Open **Connection Manager**, enter the controller FQDN and select **LDAPS** or **Kerberos**. Enter your domain UPN and password in the application. **Test connection** verifies the bind and reads RootDSE. Re-enter the password and choose **Connect read-only** to load the dashboard. A visible mode badge identifies the active provider.

Live Read-Only Mode displays users, groups, computers and the OU hierarchy. **Refresh** queries the controller again. Live inventory and credentials stay in memory. Confirmed reports go to the private export directory. Live Administration Mode also keeps a private operation journal. **Disconnect** restores your demo workspace.

## Onboard an employee

Launch the application and select **Onboarding** to try the entire workflow offline. Enter a fictional identity, choose an OU, apply a department template, set initial account options and review the proposed changes. The results show each acknowledged step. **Account administration** lets you edit attributes or compare current and proposed direct group memberships. **Administration history** records the operator and outcomes.

![Reviewing a fictional employee and their proposed memberships in Demo Mode](docs/screenshots/onboarding.png)

For live use, install the machine-owned policy and delegate rights on the disposable lab objects first. Connect to `DC01.ad.lab.test`, then select **Connection Manager → Enable live administration…**. The red badge marks the write-capable session. **Return to read-only** revokes it. Each new connection starts read-only.

[Onboarding guide](docs/onboarding.md) · [Delegated permissions and policy](docs/delegated-administration.md)

[Lab DNS, transport requirements and step-by-step acceptance checks](docs/live-lab.md) · [Changelog](CHANGELOG.md) · [v1.2.0 release notes](docs/release-v1.2.md)

## Export a privacy-safe summary

Open **Reports → Directory dataset → Counts Only (Privacy-Safe)**. Choose CSV or HTML, select **Export report**, review the destination and choose **Export counts**. The file has two columns, **Metric** and **Count**, with six rows.

| Metric | Fresh demo value |
| --- | ---: |
| Total users | 128 |
| Enabled users | 120 |
| Disabled users | 8 |
| Total groups | 12 |
| Total computers | 84 |
| Total OUs | 6 |

Live counts use the last completed read-only snapshot. Enabled users include enabled accounts that are locked. Users whose account state is unavailable contribute only to total users. Total groups includes security and distribution groups. Live OU totals count visible OU objects. Demo OU totals count distinct units assigned to users or computers.

Detailed exports are available for **User accounts**, **Security groups**, **Computers** and **Organizational units**. Each selection describes its fields before export and requires explicit confirmation. Cancelling creates no report file. Live exports, including aggregate summaries, use the private export folder outside Git. Demo Mode also supports JSON.

## Explore the demo workspace

| Screen | Tasks |
| --- | --- |
| Dashboard | Inspect account counts, the six largest organizational units, account states and recent activities. Select a summary card to open its records. |
| Users | Search names, usernames and units. Create or edit accounts. Select a row for details, enable, disable, unlock or simulated password reset. |
| Computers | Combine operating system, unit and stale/current filters. A device becomes stale after more than 90 days since its last recorded activity. |
| Groups | Create a security group, edit its description and search the membership checklist. Confirm removals before saving. |
| Bulk Operations | Choose a test CSV, review all proposed users and validation messages, then confirm the complete import. |
| Reports | Export detailed records or a six-metric counts summary to CSV, JSON or HTML. The completion dialog includes the full output path. |
| Audit Logs | Search the actor, action, target, outcome or details. Sort columns, filter outcomes and open a row to read its full event. |
| Settings | Switch between dark and light themes, select an export directory, clear the demo directory or restore sample records. |

### Try an end-to-end administration task

1. Open **Users**, choose the **Locked** filter and select a fictional account. Choose **DEMO unlock** and confirm. The account leaves the locked view.
2. Open **Audit Logs** and search for the username. Open the unlock entry to inspect its timestamp, actor, target and outcome.
3. Open **Bulk Operations** and choose [the sample CSV](samples/users.csv). Review three proposed accounts, then apply the import. Previewing the same file again flags the existing usernames.
4. Export **User accounts** from **Reports**. After a fresh seed and this import, the report contains 131 users.
5. Use **Settings → Restore samples** to start again. Your preferences and audit trail are retained.

![Searchable users table with simulated account operations](docs/screenshots/users.png)

## Data and simulation boundaries

- Demo administration uses fictional records and the **Demo Operator** identity. Connection Manager accepts the operator credential. The onboarding wizard accepts the new employee’s temporary password separately.
- A simulated password reset records its timestamp. Demo accounts contain no password value.
- The default workspace lives at `%LOCALAPPDATA%\AD Control Center\directory.json`. The initial export folder is its sibling directory `Exports`. Reports are ordinary files and remain available after a directory reset.
- `ADCC_DEMO_HOME` can point to a different local workspace for isolated demonstrations and tests. Each workspace permits one running application instance.
- Audit entries use UTC timestamps with local-time display. Seeded example activities identify themselves in the details. The audit trail is a local learning tool and is editable by the owner of the JSON file.

## Architecture and tests

The WPF shell uses MVVM and explicit constructor injection. `IDirectoryService` defines directory operations, `IDemoWorkspace` owns workspace settings and maintenance, and `DemoDirectoryService` implements both over an atomic local JSON store. Views use typed page models and commands. Native dialogs are behind `IInteraction`. The live `ILiveReader` contract exposes reads and disposal. A separate per-connection `IAdminBackend` implements scoped writes through the shared `AdminWorkflow`. Its LDAP implementation uses `System.DirectoryServices.Protocols` and executes directory queries on a worker thread.

```powershell
dotnet test ADControlCenter.sln -c Release
```

To exercise the real WPF application, build Release and run the desktop smoke test from an interactive Windows session. It launches its own process, creates a temporary workspace, and cleans up that workspace after the run.

```powershell
.\scripts\Test-Desktop.ps1
```

- [Architecture and storage design](docs/architecture.md)
- [Import format, validation and recovery](docs/operations.md)
- [Testing and screenshot instructions](docs/testing.md)
- [MIT license](LICENSE)
