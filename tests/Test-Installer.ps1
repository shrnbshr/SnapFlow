param([string]$InstallerPath = 'dist/SnapFlow-setup-x64.exe')
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
if (Test-Path -LiteralPath $taskPrograms) { throw '开始菜单已存在 SnapFlow 目录，停止测试以免覆盖' }
New-Item -ItemType Directory -Path $taskTest -Force | Out-Null
$taskResults = [Collections.Generic.List[string]]::new()
function Assert-Installer([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw "FAIL: $Message" }
    $taskResults.Add($Message)
    Write-Output "PASS: $Message"
}
function Invoke-TestInstall([string]$LogName) {
    $taskSetup = Start-Process -FilePath $InstallerPath -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/LANG=chinesesimp', '/TASKS=',
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
    Assert-Installer (@(Get-ChildItem -LiteralPath $taskPrograms -Filter '*.lnk').Count -eq 2) 'Start menu contains application and uninstall shortcuts'

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
Assert-Installer (!(Test-Path -LiteralPath $taskRegistry)) 'Uninstall removes its Windows application record'
Assert-Installer (!(Test-Path -LiteralPath (Join-Path $taskApp 'SnapFlow.exe'))) 'Uninstall removes the application executable'
Assert-Installer (!(Test-Path -LiteralPath (Join-Path $taskApp 'coreclr.dll'))) 'Uninstall removes the bundled runtime'
Assert-Installer (!(Test-Path -LiteralPath $taskPrograms)) 'Uninstall removes the isolated Start menu group'
Assert-Installer ((Test-Path -LiteralPath $taskSettings) -and (Test-Path -LiteralPath $taskPreferences)) 'Uninstall retains settings generated on the target computer'
[ordered]@{ version=$taskVersion; installer=$InstallerPath; checks=$taskResults.ToArray(); passed=$taskResults.Count; logs=$taskTest } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath 'artifacts/installer-validation-result.json' -Encoding utf8
