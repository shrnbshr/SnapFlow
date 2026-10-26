param([string]$IsccPath)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'

if (!$IsccPath) {
    $taskCandidates = @(
        (Join-Path $PSScriptRoot 'artifacts/installer-tools/InnoSetup/ISCC.exe'),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $IsccPath = $taskCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (!$IsccPath) {
        $taskCompiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($taskCompiler) { $IsccPath = $taskCompiler.Source }
    }
}
if (!$IsccPath -or !(Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw '需要 Inno Setup 6.5 或更新版本；请使用 -IsccPath 指定 ISCC.exe。'
}
$IsccPath = (Resolve-Path -LiteralPath $IsccPath).Path
& (Join-Path $PSScriptRoot 'tests/Validate-Localization.ps1')

# A new directory on each run prevents local settings and stale publish files
# from entering the installer. The existing portable release is never staged.
$taskBuild = Join-Path $PSScriptRoot ('artifacts/installer-build-' + [Guid]::NewGuid().ToString('N'))
$taskPayload = Join-Path $taskBuild 'payload'
$taskStage = Join-Path $taskBuild 'source'
$taskDist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $taskPayload,$taskStage,$taskDist -Force | Out-Null
dotnet publish src/ShareX.Roll/ShareX.Roll.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:UseSharedCompilation=false -p:DebugType=None -p:DebugSymbols=false -o $taskPayload --nologo -v minimal -m:1
if ($LASTEXITCODE -ne 0) { throw '安装版发布失败' }
Copy-Item -LiteralPath README.md,NOTICE.md,LICENSE.txt,VALIDATION.md -Destination $taskPayload
Copy-Item -LiteralPath Licenses -Destination $taskPayload -Recurse
Copy-Item -LiteralPath src/UPSTREAM.json -Destination $taskPayload

$taskPersonalNames = @('SnapFlow.settings.json','SnapFlow.preferences.json','ShareX - roll.settings.json')
function Test-DistributionFiles([string]$Directory) {
    $taskFiles = @(Get-ChildItem -LiteralPath $Directory -File -Recurse -Force)
    if ($taskFiles | Where-Object { $_.Name -in $taskPersonalNames -or $_.Extension -in @('.pdb','.log') }) {
        throw '分发目录含个人设置、日志或调试符号'
    }
    if (Get-ChildItem -LiteralPath $Directory -Recurse -Force | Where-Object { $_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint) }) {
        throw '分发目录包含链接'
    }
}
Test-DistributionFiles $taskPayload
$taskVersion = ([xml](Get-Content -Raw src/ShareX.Roll/ShareX.Roll.csproj)).Project.PropertyGroup.Version
$taskActualVersion = (Get-Item (Join-Path $taskPayload 'SnapFlow.exe')).VersionInfo.FileVersion
if ($taskActualVersion -ne "$taskVersion.0") { throw '发布文件版本与项目版本不符' }
$taskRuntime = Get-Content -Raw (Join-Path $taskPayload 'SnapFlow.runtimeconfig.json') | ConvertFrom-Json
if (!$taskRuntime.runtimeOptions.includedFrameworks -or $taskRuntime.runtimeOptions.frameworks) { throw '安装版没有内置运行时' }
foreach ($taskRequired in @('coreclr.dll','hostfxr.dll','System.Windows.Forms.dll','SnapFlow.exe')) {
    if (!(Test-Path -LiteralPath (Join-Path $taskPayload $taskRequired))) { throw "缺少运行时或主程序：$taskRequired" }
}
# Self-contained runtime redistributions must keep their own license notices.
$taskAssets = Get-Content -Raw src/ShareX.Roll/obj/project.assets.json | ConvertFrom-Json
foreach ($taskFramework in $taskRuntime.runtimeOptions.includedFrameworks) {
    $taskPackageName = $taskFramework.name.ToLowerInvariant() + '.runtime.win-x64'
    $taskPackageDirectory = $taskAssets.packageFolders.PSObject.Properties.Name | ForEach-Object {
        Join-Path $_ "$taskPackageName/$($taskFramework.version)"
    } | Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
    if (!$taskPackageDirectory) { throw "找不到运行时许可来源：$taskPackageName" }
    $taskNotices = @(Get-ChildItem -LiteralPath $taskPackageDirectory -File | Where-Object { $_.Name -like 'LICENSE*' -or $_.Name -eq 'THIRD-PARTY-NOTICES.TXT' })
    if (!$taskNotices) { throw "缺少运行时许可：$taskPackageName" }
    foreach ($taskNotice in $taskNotices) {
        Copy-Item -LiteralPath $taskNotice.FullName -Destination (Join-Path $taskPayload "Licenses/$taskPackageName-$($taskNotice.Name)")
    }
}
Test-DistributionFiles $taskPayload

& $IsccPath "/DPayloadDir=$taskPayload" "/DOutputDir=$taskDist" (Join-Path $PSScriptRoot 'installer/SnapFlow.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup 编译失败' }
$taskInstaller = Join-Path $taskDist "SnapFlow-$taskVersion-setup-x64.exe"
if (!(Test-Path -LiteralPath $taskInstaller)) { throw '未生成预期安装包' }

# Ship matching source, including these installer scripts and language files.
foreach ($taskFile in (Get-ChildItem -LiteralPath src,tests,Licenses,installer -File -Recurse -Force | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })) {
    if ($taskFile.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw '源码含链接' }
    if ($taskFile.Name -in $taskPersonalNames -or $taskFile.Extension -in @('.pdb','.log')) { continue }
    $taskRelative = [IO.Path]::GetRelativePath($PSScriptRoot, $taskFile.FullName)
    $taskDestination = Join-Path $taskStage $taskRelative
    New-Item -ItemType Directory -Path (Split-Path -Parent $taskDestination) -Force | Out-Null
    Copy-Item -LiteralPath $taskFile.FullName -Destination $taskDestination
}
Copy-Item -LiteralPath ShareX.slnx,build-capture.ps1,build-installer.ps1,README.md,NOTICE.md,LICENSE.txt,VALIDATION.md -Destination $taskStage
Test-DistributionFiles $taskStage
$taskSourceZip = Join-Path $taskDist "SnapFlow-$taskVersion-source.zip"
Compress-Archive -Path (Join-Path $taskStage '*') -DestinationPath $taskSourceZip -Force

$taskManifest = @(Get-ChildItem -LiteralPath $taskPayload -File -Recurse | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($taskPayload, $_.FullName); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
[ordered]@{
    version = $taskVersion
    selfContained = $true
    personalConfigurationFiles = 0
    payloadDirectory = [IO.Path]::GetRelativePath($PSScriptRoot, $taskPayload)
    installer = (Get-FileHash -LiteralPath $taskInstaller -Algorithm SHA256).Hash
    sourceArchive = (Get-FileHash -LiteralPath $taskSourceZip -Algorithm SHA256).Hash
    files = $taskManifest
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskDist "SnapFlow-$taskVersion-installer-manifest.json") -Encoding utf8
Copy-Item -LiteralPath $taskInstaller -Destination (Join-Path $taskDist 'SnapFlow-setup-x64.exe') -Force
Write-Output "完成：$taskInstaller"
Write-Output "对应源码：$taskSourceZip"
