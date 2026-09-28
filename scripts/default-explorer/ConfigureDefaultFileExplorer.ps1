[CmdletBinding()]
param(
    [ValidateSet('Interactive', 'Status', 'Set', 'Restore', 'EmergencyRestore')]
    [string]$Action = 'Interactive',
    [string]$RegistryTestRoot,
    [string]$BackupDirectoryOverride,
    [string]$Confirmation
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$SnapshotVersion = 1
$ManagedPaths = @(
    'Drive\shell',
    'Drive\shell\open',
    'Drive\shell\open\command',
    'Directory\shell',
    'Directory\shell\open',
    'Directory\shell\open\command'
)
$ShellPaths = @('Drive\shell', 'Directory\shell')
$CommandPaths = @('Drive\shell\open\command', 'Directory\shell\open\command')
$ScriptDirectory = Split-Path -Parent $PSCommandPath
$AppPath = Join-Path $ScriptDirectory 'FileExplorer.exe'
$ExpectedCommand = '"{0}" "%1"' -f $AppPath
$TestMode = ![string]::IsNullOrWhiteSpace($RegistryTestRoot) -or ![string]::IsNullOrWhiteSpace($BackupDirectoryOverride)

if ($TestMode -and ([string]::IsNullOrWhiteSpace($RegistryTestRoot) -or [string]::IsNullOrWhiteSpace($BackupDirectoryOverride))) {
    throw 'RegistryTestRoot and BackupDirectoryOverride must be supplied together.'
}

$BackupDirectory = if ($TestMode) {
    [IO.Path]::GetFullPath($BackupDirectoryOverride)
}
else {
    Join-Path $env:ProgramData 'FileExplorer\DefaultExplorerBackup'
}
$SnapshotPath = Join-Path $BackupDirectory 'original-registry-snapshot.json'
$StatePath = Join-Path $BackupDirectory 'state.json'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-StartupAccess {
    if ($TestMode) {
        if ($Action -eq 'Interactive') {
            throw 'Interactive mode cannot use a test registry root.'
        }
        if (!$RegistryTestRoot.StartsWith('Software\FileExplorerTests\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'RegistryTestRoot must stay under HKCU\Software\FileExplorerTests.'
        }
        return
    }

    if (!(Test-IsAdministrator)) {
        Write-Host 'This tool must be run as administrator.' -ForegroundColor Red
        Write-Host 'Right-click the batch file and choose "Run as administrator".'
        exit 1
    }
}

function Get-RegistryBase {
    if ($TestMode) {
        return [Microsoft.Win32.Registry]::CurrentUser
    }
    return [Microsoft.Win32.Registry]::ClassesRoot
}

function Get-RegistryPath([string]$RelativePath) {
    if ($TestMode) {
        return ($RegistryTestRoot.TrimEnd('\') + '\' + $RelativePath)
    }
    return $RelativePath
}

function Open-RegistryKey([string]$RelativePath, [bool]$Writable, [bool]$Create) {
    $base = Get-RegistryBase
    $path = Get-RegistryPath $RelativePath
    if ($Create) {
        return $base.CreateSubKey($path, $Writable)
    }
    return $base.OpenSubKey($path, $Writable)
}

function Remove-RegistryTree([string]$RelativePath) {
    $base = Get-RegistryBase
    try {
        $base.DeleteSubKeyTree((Get-RegistryPath $RelativePath), $false)
    }
    catch [ArgumentException] {
        return
    }
}

function ConvertTo-SnapshotData($Value, [Microsoft.Win32.RegistryValueKind]$Kind) {
    switch ($Kind) {
        'Binary' { return [Convert]::ToBase64String([byte[]]$Value) }
        'MultiString' { return @([string[]]$Value) }
        'DWord' { return ([uint32]$Value).ToString([Globalization.CultureInfo]::InvariantCulture) }
        'QWord' { return ([uint64]$Value).ToString([Globalization.CultureInfo]::InvariantCulture) }
        default { return [string]$Value }
    }
}

function ConvertFrom-SnapshotData($Entry) {
    switch ([string]$Entry.ValueKind) {
        'Binary' { return ,([Convert]::FromBase64String([string]$Entry.ValueData)) }
        'MultiString' { return ,([string[]]@($Entry.ValueData)) }
        'DWord' { return [int32]::Parse([string]$Entry.ValueData, [Globalization.CultureInfo]::InvariantCulture) }
        'QWord' { return [int64]::Parse([string]$Entry.ValueData, [Globalization.CultureInfo]::InvariantCulture) }
        default { return [string]$Entry.ValueData }
    }
}

function Get-DefaultValueRecord([string]$RelativePath) {
    $key = Open-RegistryKey $RelativePath $false $false
    if ($null -eq $key) {
        return [ordered]@{ Path = $RelativePath; KeyExists = $false; DefaultValueExists = $false; ValueKind = $null; ValueData = $null }
    }

    try {
        $hasDefault = @($key.GetValueNames()) -contains ''
        if (!$hasDefault) {
            return [ordered]@{ Path = $RelativePath; KeyExists = $true; DefaultValueExists = $false; ValueKind = $null; ValueData = $null }
        }

        $kind = $key.GetValueKind('')
        $value = $key.GetValue('', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        return [ordered]@{
            Path = $RelativePath
            KeyExists = $true
            DefaultValueExists = $true
            ValueKind = $kind.ToString()
            ValueData = ConvertTo-SnapshotData $value $kind
        }
    }
    finally {
        $key.Dispose()
    }
}

function Get-RegistrySnapshot {
    return [ordered]@{
        FormatVersion = $SnapshotVersion
        CreatedAtUtc = [DateTime]::UtcNow.ToString('o')
        Entries = @($ManagedPaths | ForEach-Object { Get-DefaultValueRecord $_ })
    }
}

function Test-SnapshotObject($Snapshot) {
    if ($null -eq $Snapshot -or [int]$Snapshot.FormatVersion -ne $SnapshotVersion) { return $false }
    $entries = @($Snapshot.Entries)
    if ($entries.Count -ne $ManagedPaths.Count) { return $false }

    foreach ($path in $ManagedPaths) {
        $matches = @($entries | Where-Object { [string]$_.Path -eq $path })
        if ($matches.Count -ne 1) { return $false }
        $entry = $matches[0]
        if ($null -eq $entry.KeyExists -or $null -eq $entry.DefaultValueExists) { return $false }
        if ([bool]$entry.DefaultValueExists) {
            try { [void][Microsoft.Win32.RegistryValueKind]::$($entry.ValueKind) } catch { return $false }
            if ($null -eq $entry.ValueData) { return $false }
        }
    }
    return $true
}

function Read-Snapshot {
    if (!(Test-Path -LiteralPath $SnapshotPath -PathType Leaf)) { return $null }
    try {
        $snapshot = Get-Content -Raw -LiteralPath $SnapshotPath | ConvertFrom-Json
    }
    catch {
        throw "Snapshot is unreadable: $($_.Exception.Message)"
    }
    if (!(Test-SnapshotObject $snapshot)) {
        throw "Snapshot format is invalid or unsupported: $SnapshotPath"
    }
    return $snapshot
}

function Save-OriginalSnapshot {
    if (Test-Path -LiteralPath $SnapshotPath -PathType Leaf) {
        [void](Read-Snapshot)
        return $false
    }

    New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
    $temporaryPath = $SnapshotPath + '.tmp'
    Get-RegistrySnapshot | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $temporaryPath -Encoding UTF8
    Move-Item -LiteralPath $temporaryPath -Destination $SnapshotPath
    [void](Read-Snapshot)
    return $true
}

function Write-State([string]$Status) {
    New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
    [ordered]@{ FormatVersion = 1; Status = $Status; UpdatedAtUtc = [DateTime]::UtcNow.ToString('o'); AppPath = $AppPath } |
        ConvertTo-Json | Set-Content -LiteralPath $StatePath -Encoding UTF8
}

function Set-DefaultValue([string]$RelativePath, $Value, [Microsoft.Win32.RegistryValueKind]$Kind) {
    $key = Open-RegistryKey $RelativePath $true $true
    try { $key.SetValue('', $Value, $Kind) } finally { $key.Dispose() }
}

function Remove-DefaultValue([string]$RelativePath) {
    $key = Open-RegistryKey $RelativePath $true $false
    if ($null -eq $key) { return }
    try { $key.DeleteValue('', $false) } finally { $key.Dispose() }
}

function Restore-SnapshotEntry($Entry) {
    if (![bool]$Entry.KeyExists) { return }
    $key = Open-RegistryKey ([string]$Entry.Path) $true $true
    $key.Dispose()
    if ([bool]$Entry.DefaultValueExists) {
        $kind = [Microsoft.Win32.RegistryValueKind]::$($Entry.ValueKind)
        Set-DefaultValue ([string]$Entry.Path) (ConvertFrom-SnapshotData $Entry) $kind
    }
    else {
        Remove-DefaultValue ([string]$Entry.Path)
    }
}

function Test-EntryMatches($Entry) {
    $current = Get-DefaultValueRecord ([string]$Entry.Path)
    if ([bool]$current.KeyExists -ne [bool]$Entry.KeyExists) { return $false }
    if (![bool]$Entry.KeyExists) { return $true }
    if ([bool]$current.DefaultValueExists -ne [bool]$Entry.DefaultValueExists) { return $false }
    if (![bool]$Entry.DefaultValueExists) { return $true }
    return ([string]$current.ValueKind -eq [string]$Entry.ValueKind) -and
        (($current.ValueData | ConvertTo-Json -Compress) -eq ($Entry.ValueData | ConvertTo-Json -Compress))
}

function Test-RegistryMatchesSnapshot($Snapshot) {
    foreach ($entry in @($Snapshot.Entries)) {
        if (!(Test-EntryMatches $entry)) { return $false }
    }
    return $true
}

function Get-CurrentCommand([string]$RelativePath) {
    $record = Get-DefaultValueRecord $RelativePath
    if (!$record.KeyExists -or !$record.DefaultValueExists) { return $null }
    return [string](ConvertFrom-SnapshotData ([pscustomobject]$record))
}

function Test-CommandTargets([string]$Command, [string]$ExecutablePath) {
    if ([string]::IsNullOrWhiteSpace($Command)) { return $false }
    return $Command.Trim().StartsWith(('"{0}"' -f $ExecutablePath), [StringComparison]::OrdinalIgnoreCase)
}

function Get-ExplorerRegistrationState {
    $commands = @($CommandPaths | ForEach-Object { Get-CurrentCommand $_ })
    if (@($commands | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) { return 'Incomplete configuration' }
    if (@($commands | Where-Object { Test-CommandTargets $_ $AppPath }).Count -eq $CommandPaths.Count) { return 'Current FileExplorer' }
    if (@($commands | Where-Object { $_ -match '(?i)(^|[\\\"])(explorer\.exe)([\"\s]|$)' }).Count -eq $CommandPaths.Count) { return 'Windows Explorer' }
    if ($commands[0] -eq $commands[1]) { return 'Other program / custom configuration' }
    return 'Incomplete configuration'
}

function Get-SnapshotStatus {
    if (!(Test-Path -LiteralPath $SnapshotPath -PathType Leaf)) {
        return [ordered]@{ Status = 'Missing'; CreatedAtUtc = $null; Error = $null }
    }
    try {
        $snapshot = Read-Snapshot
        return [ordered]@{ Status = 'Valid'; CreatedAtUtc = $snapshot.CreatedAtUtc; Error = $null }
    }
    catch {
        return [ordered]@{ Status = 'Invalid'; CreatedAtUtc = $null; Error = $_.Exception.Message }
    }
}

function Show-Status {
    $state = Get-ExplorerRegistrationState
    $snapshot = Get-SnapshotStatus
    Write-Host ''
    Write-Host "Current registration: $state"
    Write-Host "FileExplorer target:   $AppPath"
    Write-Host "FileExplorer present:  $(Test-Path -LiteralPath $AppPath -PathType Leaf)"
    Write-Host "Snapshot status:       $($snapshot.Status)"
    if ($snapshot.CreatedAtUtc) {
        Write-Host "Snapshot created UTC:  $($snapshot.CreatedAtUtc)"
        Write-Host 'Restore mode:          Exact restore'
    }
    elseif ($snapshot.Status -eq 'Invalid') {
        Write-Host "Snapshot error:        $($snapshot.Error)" -ForegroundColor Red
        Write-Host 'Restore mode:          Emergency Windows Explorer restore only'
    }
    else {
        Write-Host 'Restore mode:          Emergency Windows Explorer restore only'
    }
    Write-Host ''
    return $state
}

function Set-FileExplorerRegistration {
    if (!(Test-Path -LiteralPath $AppPath -PathType Leaf)) {
        throw 'FileExplorer.exe was not found next to this tool. No registry changes were made.'
    }

    $created = Save-OriginalSnapshot
    if ($created) { Write-Host "Original registry snapshot saved to: $SnapshotPath" }
    else { Write-Host 'Using the existing original snapshot. It was not overwritten.' }

    foreach ($path in $ShellPaths) {
        try { Set-DefaultValue $path 'open' ([Microsoft.Win32.RegistryValueKind]::String) }
        catch { throw "Writing shell selection '$path' failed: $($_.Exception.Message)" }
    }
    foreach ($path in $CommandPaths) {
        try { Set-DefaultValue $path $ExpectedCommand ([Microsoft.Win32.RegistryValueKind]::String) }
        catch { throw "Writing FileExplorer command '$path' failed: $($_.Exception.Message)" }
    }
    if ((Get-ExplorerRegistrationState) -ne 'Current FileExplorer') {
        throw 'Registry write-back verification did not match the current FileExplorer target.'
    }
    Write-State 'FileExplorerActive'
    Write-Host 'FileExplorer is now registered for folders and drives.' -ForegroundColor Green
}

function Restore-OriginalRegistration {
    $snapshot = Read-Snapshot
    if ($null -eq $snapshot) { return $false }

    foreach ($entry in @($snapshot.Entries | Where-Object { [bool]$_.KeyExists })) {
        try { Restore-SnapshotEntry $entry }
        catch { throw "Restoring snapshot entry '$($entry.Path)' failed: $($_.Exception.Message)" }
    }
    foreach ($entry in @($snapshot.Entries | Where-Object { ![bool]$_.KeyExists } | Sort-Object { ([string]$_.Path).Length } -Descending)) {
        try { Remove-RegistryTree ([string]$entry.Path) }
        catch { throw "Removing key '$($entry.Path)' during restore failed: $($_.Exception.Message)" }
    }
    if (!(Test-RegistryMatchesSnapshot $snapshot)) { throw 'Registry verification did not match the original snapshot.' }
    Write-State 'ExactlyRestored'
    Write-Host 'The original folder and drive registration was restored exactly.' -ForegroundColor Green
    return $true
}

function Restore-WindowsExplorerEmergency([string]$ProvidedConfirmation) {
    Write-Host 'WARNING: No valid original snapshot is available.' -ForegroundColor Yellow
    Write-Host 'This cannot restore a previous third-party file manager configuration.' -ForegroundColor Yellow
    $answer = if ($null -ne $ProvidedConfirmation) { $ProvidedConfirmation } else { Read-Host 'Type RESTORE WINDOWS to continue' }
    if ($answer -cne 'RESTORE WINDOWS') {
        Write-Host 'Emergency restore cancelled. No registry changes were made.'
        return $false
    }

    foreach ($path in $ShellPaths) {
        try { Set-DefaultValue $path 'open' ([Microsoft.Win32.RegistryValueKind]::String) }
        catch { throw "Writing emergency shell selection '$path' failed: $($_.Exception.Message)" }
    }
    foreach ($path in $CommandPaths) {
        try { Set-DefaultValue $path 'explorer.exe "%1"' ([Microsoft.Win32.RegistryValueKind]::String) }
        catch { throw "Writing Windows Explorer command '$path' failed: $($_.Exception.Message)" }
    }
    if ((Get-ExplorerRegistrationState) -ne 'Windows Explorer') { throw 'Registry write-back verification did not match Windows Explorer.' }
    Write-State 'EmergencyWindowsExplorerRestored'
    Write-Host 'Windows Explorer emergency registration was applied.' -ForegroundColor Green
    Write-Host 'This was an emergency reset, not an exact restoration of the original configuration.'
    return $true
}

function Invoke-Restore {
    $snapshotStatus = Get-SnapshotStatus
    if ($snapshotStatus.Status -eq 'Valid') { return Restore-OriginalRegistration }
    if ($snapshotStatus.Status -eq 'Invalid') {
        Write-Host $snapshotStatus.Error -ForegroundColor Red
        Write-Host 'The invalid snapshot was not overwritten or deleted.'
    }
    return Restore-WindowsExplorerEmergency $Confirmation
}

function Show-Menu {
    Write-Host '=============================================='
    Write-Host ' FileExplorer default folder/drive tool'
    Write-Host '=============================================='
    Write-Host '1. View current status'
    Write-Host '2. Set FileExplorer as default'
    Write-Host '3. Restore previous configuration'
    Write-Host '0. Exit'
}

function Invoke-Interactive {
    while ($true) {
        [void](Show-Status)
        Show-Menu
        switch (Read-Host 'Choose an option') {
            '1' { [void](Show-Status) }
            '2' { try { Set-FileExplorerRegistration } catch { Write-Host "Set operation failed: $($_.Exception.Message)" -ForegroundColor Red; Write-Host 'The original snapshot was kept. You may retry or choose restore.' } }
            '3' { try { [void](Invoke-Restore) } catch { Write-Host "Restore operation failed: $($_.Exception.Message)" -ForegroundColor Red; Write-Host 'The snapshot was kept and can be retried.' } }
            '0' { return }
            default { Write-Host 'Invalid option. Choose 0, 1, 2, or 3.' -ForegroundColor Yellow }
        }
        Write-Host ''
        Read-Host 'Press Enter to continue' | Out-Null
    }
}

Assert-StartupAccess
try {
    switch ($Action) {
        'Interactive' { Invoke-Interactive }
        'Status' { Write-Output (Show-Status) }
        'Set' { Set-FileExplorerRegistration }
        'Restore' { if (!(Invoke-Restore)) { exit 3 } }
        'EmergencyRestore' { if (!(Restore-WindowsExplorerEmergency $Confirmation)) { exit 3 } }
    }
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 2
}
