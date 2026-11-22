[CmdletBinding()]
param(
    [string]$Executable,
    [string]$ScreenshotDirectory
)
$ErrorActionPreference = 'Stop'
if (!$Executable) { $Executable = Join-Path $PSScriptRoot '..\src\ADControlCenter.App\bin\Release\net10.0-windows\ADControlCenter.exe' }
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
if (!('DemoCapture' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DemoCapture {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out Rect rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr handle, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, string text);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
'@
}
$previousDpi = [DemoCapture]::SetThreadDpiAwarenessContext([IntPtr](-4))
$testBase = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'ADCC-DesktopTests'))
$testData = Join-Path $testBase ([Guid]::NewGuid().ToString('N'))
$previousFolder = $env:ADCC_DEMO_HOME
$process = $null

function Wait-For([scriptblock]$Check, [string]$Description) {
    $limit = [DateTime]::UtcNow.AddSeconds(12)
    do {
        $value = & $Check
        if ($value) { return $value }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    throw "Timed out waiting for $Description"
}
function Find-Element($Root, [string]$Name, $Type) {
    $matchName = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $Name)
    $matchType = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, $Type)
    $both = [Windows.Automation.AndCondition]::new($matchName,$matchType)
    $Root.FindFirst([Windows.Automation.TreeScope]::Descendants,$both)
}
function Click-Button($Root,[string]$Name) {
    $button = Wait-For { Find-Element $Root $Name ([Windows.Automation.ControlType]::Button) } "button '$Name'"
    $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Wait-Text($Root,[string]$Name) {
    [void](Wait-For { Find-Element $Root $Name ([Windows.Automation.ControlType]::Text) } "text '$Name'")
}
function Set-Field($Root,[string]$Name,[string]$Value) {
    $input = Wait-For { Find-Element $Root $Name ([Windows.Automation.ControlType]::Edit) } "field '$Name'"
    $input.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
}
function Choose-Option($Root, [string]$Name, [string]$Value) {
    $combo = Wait-For { Find-Element $Root $Name ([Windows.Automation.ControlType]::ComboBox) } "selector '$Name'"
    $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $item = Wait-For { Find-Element $combo $Value ([Windows.Automation.ControlType]::ListItem) } "option '$Value'"
    $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
}
function Select-FirstRecord($Root) {
    $grid = Find-Element $Root 'Directory records' ([Windows.Automation.ControlType]::DataGrid)
    $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::DataItem)
    $row = Wait-For { $grid.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition) } 'the first matching record'
    $row.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Confirm-Action($Root,[string]$Title,[string]$Button) {
    $form = Wait-For { Find-Element $Root $Title ([Windows.Automation.ControlType]::Window) } "confirmation '$Title'"
    Click-Button $form $Button
}
function Snapshot([string]$Name) {
    if (!$ScreenshotDirectory) { return }
    [void][IO.Directory]::CreateDirectory($ScreenshotDirectory)
    Start-Sleep -Milliseconds 450
    $rect = New-Object DemoCapture+Rect
    [void][DemoCapture]::GetWindowRect($process.MainWindowHandle,[ref]$rect)
    $bitmap = New-Object Drawing.Bitmap ($rect.Right-$rect.Left),($rect.Bottom-$rect.Top)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    try {
        if (![DemoCapture]::PrintWindow($process.MainWindowHandle,$dc,2)) { throw 'Window capture failed.' }
    } finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
    try { $bitmap.Save((Join-Path $ScreenshotDirectory "$Name.png"),[Drawing.Imaging.ImageFormat]::Png) }
    finally { $bitmap.Dispose() }
}
try {
    $env:ADCC_DEMO_HOME = $testData
    $process = Start-Process -FilePath (Resolve-Path $Executable) -PassThru
    $handle = Wait-For { $process.Refresh(); if ($process.HasExited) { throw 'The desktop application exited.' }; if ($process.MainWindowHandle -ne 0) { $process.MainWindowHandle } } 'the desktop window'
    $window = [Windows.Automation.AutomationElement]::FromHandle($handle)
    Wait-Text $window 'Directory overview'
    Snapshot 'dashboard'
    Click-Button $window 'Users'
    Wait-Text $window '128 records'
    Set-Field $window 'Search records' 'avery.vale'
    Wait-Text $window '1 record'
    Set-Field $window 'Search records' ''
    Wait-Text $window '128 records'
    Snapshot 'users'
    Click-Button $window '+  Create user'
    $dialog = Wait-For { Find-Element $window 'Create demo user' ([Windows.Automation.ControlType]::Window) } 'the user form'
    Click-Button $dialog 'Create user'
    Wait-Text $dialog 'Enter a display name of 1–80 characters.'
    Set-Field $dialog 'Display name' 'Fixture Row'
    Set-Field $dialog 'Username' 'fixture.row'
    Set-Field $dialog 'Organizational unit' 'Operations'
    Click-Button $dialog 'Create user'
    Wait-Text $window '129 records'
    Set-Field $window 'Search records' 'fixture.row'
    Wait-Text $window '1 record'
    Select-FirstRecord $window
    Click-Button $window 'Edit'
    $dialog = Wait-For { Find-Element $window 'Edit demo user' ([Windows.Automation.ControlType]::Window) } 'the user editor'
    Set-Field $dialog 'Display name' 'Edited Fixture'
    Click-Button $dialog 'Save changes'
    Wait-Text $window 'Record updated'
    foreach ($operation in @('disable','enable','reset password')) {
        Select-FirstRecord $window
        Click-Button $window "DEMO $operation"
        $label = if ($operation -eq 'reset password') { 'password reset' } else { $operation }
        Confirm-Action $window "DEMO $label" 'Simulate action'
        Wait-Text $window "DEMO $label completed for fixture.row"
    }
    Set-Field $window 'Search records' ''
    Wait-Text $window '129 records'
    Choose-Option $window 'Record filter' 'Locked'
    Wait-Text $window '4 records'
    Select-FirstRecord $window
    Click-Button $window 'DEMO unlock'
    Confirm-Action $window 'DEMO unlock' 'Simulate action'
    Wait-Text $window '3 records'
    foreach ($item in @(@('Computers','84 records'),@('Groups','12 records'),@('Bulk Operations','Import preview'),@('Reports','Export a report'),@('Audit Logs','Directory records'),@('Settings','Workspace preferences'))) {
        Click-Button $window $item[0]
        Wait-Text $window $item[1]
    }
    Choose-Option $window 'Theme' 'Light'
    Click-Button $window 'Save preferences'
    Wait-Text $window 'Settings saved'
    $state = Get-Content -LiteralPath (Join-Path $testData 'directory.json') -Raw | ConvertFrom-Json
    if ($state.Settings.Theme -ne 'Light') { throw 'Theme preference was not saved.' }
    Choose-Option $window 'Theme' 'Dark'
    Click-Button $window 'Save preferences'
    Click-Button $window 'Computers'
    Choose-Option $window 'Record filter' 'Stale'
    Wait-Text $window '10 records'
    Choose-Option $window 'Organizational unit' 'Engineering'
    Wait-Text $window '3 records'
    Choose-Option $window 'Operating system' 'Windows Server 2022'
    Wait-Text $window '1 record'
    Click-Button $window 'Groups'
    Click-Button $window '+  Create group'
    $dialog = Wait-For { Find-Element $window 'Create demo group' ([Windows.Automation.ControlType]::Window) } 'the group editor'
    Set-Field $dialog 'Group name' 'Fixture readers'
    Set-Field $dialog 'Filter members' 'fixture.row'
    $member = Wait-For { Find-Element $dialog 'Edited Fixture   ·   fixture.row' ([Windows.Automation.ControlType]::CheckBox) } 'fixture member'
    $member.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
    Click-Button $dialog 'Create group'
    Wait-Text $window '13 records'
    Set-Field $window 'Search records' 'Fixture readers'
    Wait-Text $window '1 record'
    Select-FirstRecord $window
    Click-Button $window 'Edit group'
    $dialog = Wait-For { Find-Element $window 'Edit demo group' ([Windows.Automation.ControlType]::Window) } 'the group editor'
    Set-Field $dialog 'Filter members' 'fixture.row'
    $member = Wait-For { Find-Element $dialog 'Edited Fixture   ·   fixture.row' ([Windows.Automation.ControlType]::CheckBox) } 'existing member'
    $member.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
    Click-Button $dialog 'Save group'
    Confirm-Action $dialog 'Remove group memberships?' 'Remove and save'
    Wait-Text $window 'Record updated'
    $state = Get-Content -LiteralPath (Join-Path $testData 'directory.json') -Raw | ConvertFrom-Json
    $savedGroup = $state.Groups | Where-Object { $_.Name -eq 'Fixture readers' }
    if (!$savedGroup -or $savedGroup.Members.Count -ne 0) { throw 'Membership removal was not saved.' }
    Click-Button $window 'Bulk Operations'
    Click-Button $window 'Choose CSV…'
    $picker = Wait-For { Find-Element $window 'Choose fictional user CSV' ([Windows.Automation.ControlType]::Window) } 'the CSV picker'
    $csvPath = (Resolve-Path (Join-Path $PSScriptRoot '..\samples\users.csv')).Path
    $fileBox = Wait-For { $picker.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'1148')) } 'the file name field'
    $comboHandle = [IntPtr]$fileBox.Current.NativeWindowHandle
    if ($comboHandle -eq [IntPtr]::Zero) { throw 'The file name selector has no window handle.' }
    $childCombo = [DemoCapture]::FindWindowEx($comboHandle,[IntPtr]::Zero,'ComboBox',$null)
    if ($childCombo -ne [IntPtr]::Zero) { $comboHandle = $childCombo }
    $editHandle = [DemoCapture]::FindWindowEx($comboHandle,[IntPtr]::Zero,'Edit',$null)
    if ($editHandle -eq [IntPtr]::Zero) { throw ('File name editor unavailable in ' + $fileBox.Current.ClassName) }
    [void][DemoCapture]::SendMessage($editHandle,12,[IntPtr]::Zero,$csvPath)
    # The Win32 picker can expose Open as either a button or a split button.
    [void][DemoCapture]::PostMessage([IntPtr]$picker.Current.NativeWindowHandle, 0x0111, [IntPtr]1, [IntPtr]::Zero)
    Wait-Text $window 'Preview ready for confirmation'
    Choose-Option $window 'Validation filter' 'Ready'
    Click-Button $window 'Apply import…'
    Confirm-Action $window 'Apply CSV import?' 'Cancel'
    Wait-Text $window 'Action cancelled'
    Click-Button $window 'Apply import…'
    Confirm-Action $window 'Apply CSV import?' 'Create users'
    Wait-Text $window 'CSV import applied'
    Click-Button $window 'Reports'
    Click-Button $window 'Export report'
    Confirm-Action $window 'Report exported' 'Close'
    $export = Get-ChildItem -LiteralPath (Join-Path $testData 'Exports') -Filter '*.csv' -File
    if (@(Import-Csv $export.FullName).Count -ne 132) { throw 'Unexpected exported report size.' }
    Click-Button $window 'Settings'
    Click-Button $window 'Clear directory…'
    $confirm = Wait-For { Find-Element $window 'Clear demo directory?' ([Windows.Automation.ControlType]::Window) } 'clear confirmation'
    Click-Button $confirm 'Cancel'
    Wait-Text $window 'Action cancelled'
    Click-Button $window 'Users'
    Wait-Text $window '132 records'
    Click-Button $window 'Settings'
    Click-Button $window 'Clear directory…'
    $confirm = Wait-For { Find-Element $window 'Clear demo directory?' ([Windows.Automation.ControlType]::Window) } 'clear confirmation'
    Click-Button $confirm 'Clear directory'
    Wait-Text $window 'Demo directory cleared'
    Click-Button $window 'Dashboard'
    Wait-Text $window 'No users in the directory.'
    Click-Button $window 'Settings'
    Click-Button $window 'Restore samples…'
    $confirm = Wait-For { Find-Element $window 'Restore sample directory?' ([Windows.Automation.ControlType]::Window) } 'restore confirmation'
    Click-Button $confirm 'Restore samples'
    Wait-Text $window 'Sample directory restored'
    Click-Button $window 'Users'
    Wait-Text $window '128 records'
    'Desktop smoke test passed: all eight screens, search and filters, account actions, membership editing, CSV preview and apply, report export, themes, cancellation, clear and restore.'
} finally {
    if ($previousDpi -ne [IntPtr]::Zero) { [void][DemoCapture]::SetThreadDpiAwarenessContext($previousDpi) }
    $env:ADCC_DEMO_HOME = $previousFolder
    if ($process -and !$process.HasExited) {
        [void]$process.CloseMainWindow()
        if (!$process.WaitForExit(3000)) { $process.Kill(); $process.WaitForExit() }
    }
    if ((Test-Path -LiteralPath $testData) -and
        ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($testData)) -eq $testBase)) {
        Remove-Item -LiteralPath $testData -Recurse -Force
    }
}
