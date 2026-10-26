param([string]$InstallerPath = 'dist/SnapFlow-setup-x64.exe', [switch]$PreserveExistingShortcuts)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$taskRoot = $PWD.Path
$InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
$taskVersion = (Get-Item -LiteralPath $InstallerPath).VersionInfo.ProductVersion.Trim()
$taskManifest = Get-Content -Raw -LiteralPath "dist/SnapFlow-$taskVersion-installer-manifest.json" | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $InstallerPath).Hash -ne $taskManifest.installer) { throw '安装包与清单不符' }
$taskRegistry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{5C42C19A-441D-477A-AB91-E67E065F14DA}_is1'
if (Test-Path -LiteralPath $taskRegistry) { throw '已存在 SnapFlow 安装记录，停止隔离测试以免覆盖' }
if (Get-Process SnapFlow -ErrorAction SilentlyContinue) { throw 'SnapFlow 正在运行，请先正常退出' }
$taskToken = [Guid]::NewGuid().ToString('N')
$taskTest = Join-Path $taskRoot "artifacts/installer-validation-$taskToken"
$taskApp = Join-Path $taskTest '应用程序'
$taskGroup = 'SnapFlow'
$taskPrograms = Join-Path ([Environment]::GetFolderPath('Programs')) $taskGroup
$taskDesktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'SnapFlow.lnk'
if (!$PreserveExistingShortcuts -and ((Test-Path -LiteralPath $taskPrograms) -or (Test-Path -LiteralPath $taskDesktopShortcut))) { throw '已存在 SnapFlow 快捷方式；如需备份并恢复，请显式指定 -PreserveExistingShortcuts' }
New-Item -ItemType Directory -Path $taskTest -Force | Out-Null
$taskShortcutBackups = @()
$taskProgramsExisted = Test-Path -LiteralPath $taskPrograms
if ($taskProgramsExisted) {
    if ((Get-Item -LiteralPath $taskPrograms).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) -or
        (Get-ChildItem -LiteralPath $taskPrograms -Recurse -Force | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) })) { throw '开始菜单包含链接目录，停止验证' }
    foreach ($taskFile in (Get-ChildItem -LiteralPath $taskPrograms -File -Recurse -Force)) {
        $taskBackupPath = Join-Path $taskTest ('shortcut-backup/start-menu/' + [IO.Path]::GetRelativePath($taskPrograms, $taskFile.FullName))
        New-Item -ItemType Directory -Path (Split-Path -Parent $taskBackupPath) -Force | Out-Null
        Copy-Item -LiteralPath $taskFile.FullName -Destination $taskBackupPath
        $taskShortcutBackups += [pscustomobject]@{ Original=$taskFile.FullName; Backup=$taskBackupPath; Hash=(Get-FileHash -LiteralPath $taskFile.FullName).Hash }
    }
}
$taskDesktopExisted = Test-Path -LiteralPath $taskDesktopShortcut
if ($taskDesktopExisted) {
    if ((Get-Item -LiteralPath $taskDesktopShortcut).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw '桌面快捷方式是链接，停止验证' }
    $taskBackupPath = Join-Path $taskTest 'shortcut-backup/desktop/SnapFlow.lnk'
    New-Item -ItemType Directory -Path (Split-Path -Parent $taskBackupPath) -Force | Out-Null
    Copy-Item -LiteralPath $taskDesktopShortcut -Destination $taskBackupPath
    $taskShortcutBackups += [pscustomobject]@{ Original=$taskDesktopShortcut; Backup=$taskBackupPath; Hash=(Get-FileHash -LiteralPath $taskDesktopShortcut).Hash }
}
$taskResults = [Collections.Generic.List[string]]::new()
function Assert-Installer([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw "FAIL: $Message" }
    $taskResults.Add($Message)
    Write-Output "PASS: $Message"
}
function Invoke-TestInstall([string]$LogName) {
    $taskSetup = Start-Process -FilePath $InstallerPath -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/LANG=chinesesimp',
        ('/DIR="' + $taskApp + '"'),
        ('/LOG="' + (Join-Path $taskTest $LogName) + '"')
    )
    Assert-Installer ($taskSetup.ExitCode -eq 0) "Installer exit code 0 ($LogName)"
}
try {
    Invoke-TestInstall 'install.log'
    $taskInstalled = Get-ItemProperty -LiteralPath $taskRegistry
    Assert-Installer ($taskInstalled.DisplayName -eq 'SnapFlow' -and $taskInstalled.DisplayVersion -eq $taskVersion) 'Windows uninstall record has the expected name and version'
    Assert-Installer ($taskInstalled.InstallLocation.TrimEnd('\') -eq $taskApp) 'Custom installation path containing spaces and Chinese is respected'
    foreach ($taskName in @('SnapFlow.settings.json','SnapFlow.preferences.json','ShareX - roll.settings.json')) {
        Assert-Installer (!(Test-Path -LiteralPath (Join-Path $taskApp $taskName))) "Fresh install contains no $taskName"
    }
    $taskMismatches = @($taskManifest.files | Where-Object {
        $taskFilePath = Join-Path $taskApp $_.path
        !(Test-Path -LiteralPath $taskFilePath) -or (Get-FileHash -LiteralPath $taskFilePath).Hash -ne $_.sha256
    })
    Assert-Installer ($taskMismatches.Count -eq 0) "All $($taskManifest.files.Count) payload files match the SHA-256 manifest"
    $taskShell = New-Object -ComObject WScript.Shell
    $taskShortcut = Join-Path $taskPrograms 'SnapFlow.lnk'
    Assert-Installer (Test-Path -LiteralPath $taskShortcut) 'Start menu application shortcut exists'
    Assert-Installer ($taskShell.CreateShortcut($taskShortcut).TargetPath -eq (Join-Path $taskApp 'SnapFlow.exe')) 'Start menu shortcut targets the installed application'
    Assert-Installer (Test-Path -LiteralPath (Join-Path $taskPrograms '卸载 SnapFlow.lnk')) 'Start menu contains an uninstall shortcut'
    Assert-Installer (Test-Path -LiteralPath $taskDesktopShortcut) 'Default installation creates a desktop shortcut without selecting a task explicitly'
    Assert-Installer ($taskShell.CreateShortcut($taskDesktopShortcut).TargetPath -eq (Join-Path $taskApp 'SnapFlow.exe')) 'Default desktop shortcut targets the installed application'

    # The application itself is interactive: open its real foreground window.
    $taskProcess = Start-Process -FilePath (Join-Path $taskApp 'SnapFlow.exe') -WorkingDirectory $taskApp -WindowStyle Normal -PassThru
    try {
        $taskReady = $false
        for ($taskAttempt = 0; $taskAttempt -lt 80; $taskAttempt++) {
            Start-Sleep -Milliseconds 200
            $taskProcess.Refresh()
            if ($taskProcess.HasExited) { break }
            if ($taskProcess.MainWindowHandle -ne 0) { $taskReady = $true; break }
        }
        Assert-Installer $taskReady 'Installed application starts and creates its main window'
        $taskCoreClr = @($taskProcess.Modules | Where-Object ModuleName -eq 'coreclr.dll')
        Assert-Installer ($taskCoreClr.Count -eq 1 -and $taskCoreClr[0].FileName -eq (Join-Path $taskApp 'coreclr.dll')) 'Application loads the bundled .NET runtime from its own installation'
    }
    finally {
        $taskProcess.Refresh()
        if (!$taskProcess.HasExited) {
            $taskCloseRequested = $taskProcess.CloseMainWindow()
            if (!$taskCloseRequested -or !$taskProcess.WaitForExit(10000)) { throw '测试程序未正常关闭，请手动关闭；没有强制结束进程' }
        }
    }
    Assert-Installer ($taskProcess.ExitCode -eq 0) 'Application exits cleanly'
    $taskSettings = Join-Path $taskApp 'SnapFlow.settings.json'
    Assert-Installer (Test-Path -LiteralPath $taskSettings) 'Application can save its settings in the installation directory'
    Get-Content -Raw -LiteralPath $taskSettings | ConvertFrom-Json | Out-Null
    $taskSettingsHash = (Get-FileHash -LiteralPath $taskSettings).Hash
    $taskPreferences = Join-Path $taskApp 'SnapFlow.preferences.json'
    [IO.File]::WriteAllText($taskPreferences, '{"AnimationsEnabled":false}')
    $taskPreferencesHash = (Get-FileHash -LiteralPath $taskPreferences).Hash
    Invoke-TestInstall 'upgrade.log'
    Assert-Installer ((Get-FileHash -LiteralPath $taskSettings).Hash -eq $taskSettingsHash) 'Reinstall preserves existing scrolling settings'
    Assert-Installer ((Get-FileHash -LiteralPath $taskPreferences).Hash -eq $taskPreferencesHash) 'Reinstall preserves existing user preferences'
}
finally {
    try {
        # Only uninstall the isolated instance created by this invocation.
        if (Test-Path -LiteralPath $taskRegistry) {
            $taskCurrent = Get-ItemProperty -LiteralPath $taskRegistry
            if ($taskCurrent.InstallLocation.TrimEnd('\') -ne $taskApp) { throw '安装记录路径发生变化，拒绝卸载' }
            $taskUninstaller = [IO.Path]::GetFullPath((Join-Path $taskApp 'unins000.exe'))
            if (!$taskUninstaller.StartsWith($taskRoot + '\artifacts\installer-validation-', [StringComparison]::OrdinalIgnoreCase)) { throw '卸载程序超出隔离测试目录' }
            if (Test-Path -LiteralPath $taskUninstaller) {
                $taskUninstall = Start-Process -FilePath $taskUninstaller -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
                    '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $taskTest 'uninstall.log') + '"')
                )
                Assert-Installer ($taskUninstall.ExitCode -eq 0) 'Uninstaller exit code 0'
            }
        }
    }
    finally {
        foreach ($taskBackup in $taskShortcutBackups) {
            New-Item -ItemType Directory -Path (Split-Path -Parent $taskBackup.Original) -Force | Out-Null
            Copy-Item -LiteralPath $taskBackup.Backup -Destination $taskBackup.Original -Force
            if ((Get-FileHash -LiteralPath $taskBackup.Original).Hash -ne $taskBackup.Hash) { throw '原快捷方式恢复校验失败' }
        }
        if ($taskProgramsExisted) { New-Item -ItemType Directory -Path $taskPrograms -Force | Out-Null }
    }
}
Assert-Installer (!(Test-Path -LiteralPath $taskRegistry)) 'Uninstall removes its Windows application record'
Assert-Installer (!(Test-Path -LiteralPath (Join-Path $taskApp 'SnapFlow.exe'))) 'Uninstall removes the application executable'
Assert-Installer (!(Test-Path -LiteralPath (Join-Path $taskApp 'coreclr.dll'))) 'Uninstall removes the bundled runtime'
Assert-Installer ((Test-Path -LiteralPath $taskPrograms) -eq $taskProgramsExisted) 'Start menu group returns to its original state'
Assert-Installer ((Test-Path -LiteralPath $taskDesktopShortcut) -eq $taskDesktopExisted) 'Desktop shortcut returns to its original state'
Assert-Installer (@($taskShortcutBackups | Where-Object { !(Test-Path -LiteralPath $_.Original) -or (Get-FileHash -LiteralPath $_.Original).Hash -ne $_.Hash }).Count -eq 0) 'Existing shortcut files are restored byte for byte'
Assert-Installer ((Test-Path -LiteralPath $taskSettings) -and (Test-Path -LiteralPath $taskPreferences)) 'Uninstall retains settings generated on the target computer'
[ordered]@{ version=$taskVersion; installer=$InstallerPath; checks=$taskResults.ToArray(); passed=$taskResults.Count; logs=$taskTest } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath 'artifacts/installer-validation-result.json' -Encoding utf8
