param([switch]$DesktopSmoke)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'

function Remove-GeneratedDirectory([string]$relative) {
    $taskExpected = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $relative))
    if (!$taskExpected.StartsWith($PSScriptRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '输出目录超出项目范围' }
    if (!(Test-Path -LiteralPath $taskExpected)) { return }
    $taskResolved = (Resolve-Path -LiteralPath $taskExpected).Path
    if ($taskResolved -ne $taskExpected -or (Get-Item -LiteralPath $taskResolved).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw '输出目录路径异常' }
    if (Get-ChildItem -LiteralPath $taskResolved -Recurse -Force | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) { throw '输出目录包含链接，停止清理' }
    Remove-Item -LiteralPath $taskResolved -Recurse -Force
}

if ($DesktopSmoke) {
    dotnet build src/ShareX.Roll.Tests/ShareX.Roll.Tests.csproj -c Release -p:Platform=x64 -p:UseSharedCompilation=false --nologo -v minimal -m:1
    if ($LASTEXITCODE -ne 0) { throw '测试工程构建失败' }
    & './src/ShareX.Roll.Tests/bin/Release/win-x64/ShareX.Roll.Tests.exe' --stitch-regression
    if ($LASTEXITCODE -ne 0) { throw '拼接像素回归失败' }
    & './src/ShareX.Roll.Tests/bin/Release/win-x64/ShareX.Roll.Tests.exe'
    if ($LASTEXITCODE -ne 0) { throw '普通截图测试失败' }
}

$taskExistingExe = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'dist/SnapFlow/SnapFlow.exe'))
if (Get-Process | Where-Object { $_.Path -eq $taskExistingExe }) { throw '程序正在运行，请正常退出后再构建；发布目录尚未修改。' }
$taskSettingsBytes = $null
$taskPreferencesBytes = $null
$taskPreferencesFile = Join-Path $PSScriptRoot 'dist/SnapFlow/SnapFlow.preferences.json'
if (Test-Path -LiteralPath $taskPreferencesFile -PathType Leaf) {
    if ((Get-Item -LiteralPath $taskPreferencesFile).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw '应用设置文件是链接，停止迁移' }
    $taskPreferencesBytes = [IO.File]::ReadAllBytes($taskPreferencesFile)
}
foreach ($taskSettingsSource in @('dist/SnapFlow/SnapFlow.settings.json', 'dist/ShareX - roll/ShareX - roll.settings.json')) {
    $taskSettingsFile = Join-Path $PSScriptRoot $taskSettingsSource
    if (Test-Path -LiteralPath $taskSettingsFile -PathType Leaf) {
        if ((Get-Item -LiteralPath $taskSettingsFile).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw '设置文件是链接，停止迁移' }
        $taskSettingsBytes = [IO.File]::ReadAllBytes($taskSettingsFile)
        break
    }
}
Remove-GeneratedDirectory 'dist/SnapFlow'
dotnet publish src/ShareX.Roll/ShareX.Roll.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained false -p:UseSharedCompilation=false -p:DebugType=None -p:DebugSymbols=false -o 'dist/SnapFlow' --nologo -v minimal -m:1
if ($LASTEXITCODE -ne 0) { throw '正式版截图程序发布失败' }
$taskRelease = Join-Path $PSScriptRoot 'dist/SnapFlow'
Copy-Item -LiteralPath README.md,NOTICE.md,LICENSE.txt,VALIDATION.md -Destination $taskRelease -Force
Copy-Item -LiteralPath Licenses -Destination $taskRelease -Recurse -Force
Copy-Item -LiteralPath src/UPSTREAM.json -Destination $taskRelease -Force

$taskDeps = Get-Content -Raw -LiteralPath (Join-Path $taskRelease 'SnapFlow.deps.json') | ConvertFrom-Json
$taskForbidden = @('ShareX.UploadersLib','ShareX.Tools','ShareX.HistoryLib','ShareX.MediaLib','ShareX.ScreenRecordingLib','ShareX.NativeMessagingHost','Avalonia','SkiaSharp','FluentFTP','SSH.NET','Microsoft.Data.Sqlite','Microsoft.ML.OnnxRuntime.DirectML','ZXing.Net')
foreach ($taskLibrary in $taskDeps.libraries.PSObject.Properties.Name) {
    if ($taskLibrary.Split('/')[0] -in $taskForbidden) { throw ('发布产物仍包含已删除模块：' + $taskLibrary) }
}
if (Get-ChildItem -LiteralPath $taskRelease -File -Recurse -Filter '*.pdb') { throw '发布目录含调试符号' }

Remove-GeneratedDirectory 'dist/.source-stage'
$taskStage = Join-Path $PSScriptRoot 'dist/.source-stage'
foreach ($taskFile in (Get-ChildItem -LiteralPath src,tests,Licenses -File -Recurse | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })) {
    if ($taskFile.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw '源码含链接，停止打包' }
    $taskRelative = [IO.Path]::GetRelativePath($PSScriptRoot, $taskFile.FullName)
    $taskDestination = Join-Path $taskStage $taskRelative
    New-Item -ItemType Directory -Path (Split-Path -Parent $taskDestination) -Force | Out-Null
    Copy-Item -LiteralPath $taskFile.FullName -Destination $taskDestination -Force
}
Copy-Item -LiteralPath ShareX.slnx,build-capture.ps1,README.md,NOTICE.md,LICENSE.txt,VALIDATION.md -Destination $taskStage -Force
Compress-Archive -Path (Join-Path $taskStage '*') -DestinationPath 'dist/SnapFlow-source.zip' -Force
Compress-Archive -Path (Join-Path $taskRelease '*') -DestinationPath 'dist/SnapFlow-win-x64.zip' -Force
Remove-GeneratedDirectory 'dist/.source-stage'
if ($null -ne $taskSettingsBytes) { [IO.File]::WriteAllBytes((Join-Path $taskRelease 'SnapFlow.settings.json'), $taskSettingsBytes) }
if ($null -ne $taskPreferencesBytes) { [IO.File]::WriteAllBytes((Join-Path $taskRelease 'SnapFlow.preferences.json'), $taskPreferencesBytes) }
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
    # Invalidate this executable's Shell item after replacing it; no Explorer restart is needed.
    if (-not ('SnapFlowShellNotification' -as [type])) {
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SnapFlowShellNotification {
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(uint change, uint flags, IntPtr first, IntPtr second);
}
'@
    }
    $taskIconPath = [Runtime.InteropServices.Marshal]::StringToHGlobalUni((Join-Path $taskRelease 'SnapFlow.exe'))
    try { [SnapFlowShellNotification]::SHChangeNotify(0x2000, 0x1005, $taskIconPath, [IntPtr]::Zero) }
    finally { [Runtime.InteropServices.Marshal]::FreeHGlobal($taskIconPath) }
}
Write-Output '完成：dist/SnapFlow/SnapFlow.exe、便携压缩包和对应源码包（ShareX 21.0.0 正式版基线）。'
