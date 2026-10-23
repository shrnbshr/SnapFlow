param()
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskCatalogs = Join-Path $taskRoot 'src/ShareX.Roll/Localization'
$taskSource = [xml][IO.File]::ReadAllText((Join-Path $taskCatalogs 'Strings.resx'))
$taskEnglish = @{}
foreach ($taskEntry in $taskSource.root.data) { $taskEnglish[$taskEntry.name] = [string]$taskEntry.value }
if ($taskEnglish.Count -ne $taskSource.root.data.Count) { throw 'Duplicate English keys' }
$taskOptions = [IO.File]::ReadAllText((Join-Path $taskRoot 'src/ShareX.Roll/UiLanguage.cs'))
$taskCultures = @([regex]::Matches($taskOptions, 'new\(LanguageMode\.\w+,\s*"([\w-]+)"\)') | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -ne 'en' })
if (($taskCultures | Sort-Object) -join ',' -cne 'zh-CN,zh-HK') { throw 'Expected only Simplified Chinese and Hong Kong Traditional Chinese satellites' }
foreach ($taskFile in Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Recurse -File -Filter '*.resx' | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) {
    if ($taskFile.Name -match '\.([a-z]{2}(?:-[A-Za-z0-9]+)*)\.resx$' -and $Matches[1] -notin @('zh-CN','zh-HK')) {
        throw "Unrelated language resource remains: $($taskFile.FullName)"
    }
}
$taskRetired = @('ar-YE','de','es','es-MX','fa-IR','fr','he-IL','hu','id-ID','it-IT','ja-JP','ko-KR','nl-NL','pl','pt-BR','pt-PT','ro','ru','tr','uk','vi-VN','zh-TW')
foreach ($taskDirectory in Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Recurse -Directory) {
    if ($taskDirectory.FullName -match '\\(bin|obj)\\' -and $taskDirectory.Name -in $taskRetired) {
        throw "Retired build language directory remains: $($taskDirectory.FullName)"
    }
}
if (Test-Path -LiteralPath (Join-Path $taskRoot 'artifacts/localization-v1.0.2')) {
    throw 'Retired translation drafts or generator remain in artifacts/localization-v1.0.2'
}
$taskScreenshots = Join-Path $taskRoot 'artifacts/language-settings'
if (Test-Path -LiteralPath $taskScreenshots) {
    foreach ($taskFile in Get-ChildItem -LiteralPath $taskScreenshots -File -Filter '*.png') {
        if ($taskFile.Name -match '^(?:main|settings|about-minimum)-([A-Za-z]+)-\d+\.png$' -and
            $Matches[1] -notin @('English','SimplifiedChinese','TraditionalChinese')) {
            throw "Retired language screenshot remains: $($taskFile.Name)"
        }
    }
}
foreach ($taskCulture in $taskCultures) {
    $taskPath = Join-Path $taskCatalogs "Strings.$taskCulture.resx"
    $taskXml = [xml][IO.File]::ReadAllText($taskPath)
    $taskKeys = @{}
    foreach ($taskEntry in $taskXml.root.data) {
        $taskKey = [string]$taskEntry.name
        $taskValue = [string]$taskEntry.value
        if ($taskKeys.ContainsKey($taskKey)) { throw "Duplicate key: $taskCulture/$taskKey" }
        $taskKeys[$taskKey] = $true
        if (!$taskEnglish.ContainsKey($taskKey) -or [string]::IsNullOrWhiteSpace($taskValue)) { throw "Unknown or empty key: $taskCulture/$taskKey" }
        $taskExpected = @([regex]::Matches($taskEnglish[$taskKey], '\{\d+(?:[^}]*)\}') | ForEach-Object Value | Sort-Object)
        $taskActual = @([regex]::Matches($taskValue, '\{\d+(?:[^}]*)\}') | ForEach-Object Value | Sort-Object)
        if (($taskExpected -join '|') -cne ($taskActual -join '|')) { throw "Placeholder mismatch: $taskCulture/$taskKey" }
        if ($taskValue -ceq $taskEnglish[$taskKey] -and $taskKey -ne 'ListSeparator') { throw "Unreviewed English text: $taskCulture/$taskKey" }
    }
    if ($taskKeys.Count -ne $taskEnglish.Count) { throw "Missing keys: $taskCulture" }
    Write-Output "PASS: $taskCulture - $($taskKeys.Count) complete translations, valid placeholders, no unreviewed English text"
}
Write-Output "PASS: only 3 languages; $($taskEnglish.Count) keys per language; no unrelated source catalogs"
Write-Output 'PASS: no retired translation drafts, screenshots or build language directories'
