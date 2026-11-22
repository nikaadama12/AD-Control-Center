# Testing and screenshots

## Core tests

Run these commands from the repository root on Windows 11 with the .NET 10 SDK.

```powershell
dotnet restore ADControlCenter.sln
dotnet build ADControlCenter.sln -c Release --no-restore
dotnet test ADControlCenter.sln -c Release --no-build --logger "console;verbosity=normal"
```

MSTest creates a unique temporary workspace for each case. The suite covers sample integrity, normalized and duplicate usernames, membership edits, account simulations, restart persistence, import validation and replay, exclusive access, concurrent edits, failed writes, corrupt JSON, clear/restore and all six dataset/format export combinations.

The Windows GitHub Actions workflow builds and runs the core suite on pushes and pull requests. The desktop smoke test needs a logged-in interactive session.

## Desktop smoke test

Close any prior smoke-test file picker and run the script in a Windows PowerShell session after building Release.

```powershell
.\scripts\Test-Desktop.ps1
```

The test launches a separate application instance with `ADCC_DEMO_HOME` set to a temporary folder. It uses Windows UI Automation to operate the actual buttons, fields, filters and modal dialogs. It checks the initial sample totals, a blank form error, creating and editing a user, enabling/disabling, simulated unlock and reset, computer filters, group membership changes, theme persistence, CSV import preview/cancel/apply, report contents and clear/restore confirmation. It closes the process and deletes only its generated workspace.

The script also accepts an executable path, so the same checks can exercise a published build.

```powershell
.\scripts\Test-Desktop.ps1 -Executable .\artifacts\win-x64\ADControlCenter.exe
```

## Capture portfolio screenshots

Regenerate the README images from a fresh isolated sample directory. The script captures the app window itself and writes `dashboard.png` and `users.png`. Existing images with these names are replaced.

```powershell
.\scripts\Test-Desktop.ps1 -ScreenshotDirectory .\docs\screenshots
```

For an additional manual capture, open the sample workspace, navigate to the desired screen and use **Alt + Print Screen** to copy the active window. Paste into an image editor and save a PNG. For bulk previews, use the supplied `samples\users.csv`. Keep the demo badge visible and crop any personal path shown by a file picker.

## Verification scope

The core suite checks the provider and files on disk. The desktop script checks the most important operator workflows on Windows 11. A successful run of the workflow on GitHub appears in the repository's Actions tab after publication.
