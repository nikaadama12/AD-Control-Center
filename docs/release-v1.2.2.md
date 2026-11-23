# AD Control Center 1.2.2

## Windows x64 release

Open `ADControlCenter-1.2.2-win-x64.exe` as a standard Windows user. The self-contained executable starts in Demo Mode. Use `Get-FileHash -Algorithm SHA256` to compare the executable with the accompanying `SHA256SUMS.txt`.

## Navigation and permissions

The sidebar includes the same 13 pages in all three modes. The active page stays highlighted. While connected read-only, Onboarding shows visible OUs, Account Administration shows searchable accounts and Administration History shows activity from this connection. Each page offers **Enable Live Administration…**. A denied or cancelled authorization leaves the connection read-only.

Demo CSV imports and sample-directory maintenance are confined to Demo Mode. Live Settings changes only the display theme for the current connection. Disconnect restores the saved demo theme. Refresh and authorization use the shell’s progress indicator. Empty inventories and restricted pages explain what is available.

The live administration catalog reuses the delegated identity, approved OU, group, disposable-user and protected-object checks. For eligible objects it reads the controller’s effective child classes and writable attributes. Missing evidence restricts the corresponding destination, field or group. The controller still evaluates each submitted change. In particular, a wizard destination cannot establish the password-reset and inherited attribute rights of a user that has yet to be created. [Microsoft’s effective child-class rules](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-adts/2d3f3808-daf5-49e4-b902-3dca929e0549) · [Effective writable attributes](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-ada1/a2fcdd38-df5c-410f-8665-9271941e0d96)

Refresh rechecks effective access and operator identity. The desktop checks the active machine policy every three seconds while idle. The issued backend also checks it before a prepared change can be submitted. Loss of approval requires explicit authorization again. The existing machine policy format and secure connection options remain compatible.

## Try the interface

1. In Demo Mode, open **Organizational Units** or **Onboarding**.
2. To browse the lab, open **Connection Manager**, enter `DC01.ad.lab.test`, choose signed and sealed Kerberos and enter the delegated UPN and password locally. Select **Connect read-only**.
3. Open **Account Administration** and inspect a record. Use **Enable Live Administration…** only to review the authorized lab scope. Opening the page and authorizing the session are read operations.
4. Use **Connection Manager → Return to read-only** or **Disconnect** to end administration. A new connection always starts read-only.

## Verification

Run `dotnet test ADControlCenter.sln -c Release` to exercise directory, reporting, authorization and mode regression tests with local fixtures. On an interactive Windows desktop run `scripts\Test-Desktop.ps1 -Executable <path-to-exe>` against the packaged executable. The desktop test uses a temporary demo workspace.
