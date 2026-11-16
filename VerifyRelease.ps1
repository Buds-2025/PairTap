param(
    [string]$Artifact,
    [switch]$Quick,
    [switch]$SettingsOnly,
    [switch]$RequireMixedDpi,
    [switch]$RequireSignature
)
$ErrorActionPreference = 'Stop'
if (($Quick -or $SettingsOnly) -and $RequireMixedDpi) { throw 'Mixed-DPI verification requires the full interactive test suite.' }
if ($Quick -and $SettingsOnly) { throw 'The quick checks and settings-only checks are separate modes.' }
if (!$Quick -and (Get-Process PairTap -ErrorAction SilentlyContinue)) {
    throw 'Exit the running PairTap instance before testing the release executable.'
}
if (!$Quick -and !$SettingsOnly -and (Get-Process WINWORD -ErrorAction SilentlyContinue)) {
    throw 'Close Word before running the interactive checks. The Word fixture requires an isolated session.'
}
if (!$Quick -and !$SettingsOnly -and !(Test-Path -LiteralPath (Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'))) {
    throw 'The interactive release checks require Chrome.'
}
if (!$Artifact) {
    $Artifact = (& (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory (Join-Path $PSScriptRoot 'dist\release-check')).FullName
}
$Artifact = (Resolve-Path -LiteralPath $Artifact).Path
if ([IO.Path]::GetFileName($Artifact) -cne 'PairTap.exe') { throw 'Expected PairTap.exe.' }
$versionSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\Product.cs') -Raw
if ($versionSource -notmatch 'const string Version = "([0-9.]+)";') { throw 'Source version could not be read.' }
$version = $Matches[1]
$metadata = (Get-Item -LiteralPath $Artifact).VersionInfo
if ($metadata.FileVersion -ne $version -or $metadata.ProductVersion -ne $version) { throw 'Executable version differs from source.' }
$signature = (Get-AuthenticodeSignature -LiteralPath $Artifact).Status
if ($RequireSignature -and $signature -ne 'Valid') { throw "Executable signature is $signature." }
"PASS binary version $version, Authenticode $signature"
& (Join-Path $PSScriptRoot 'tests\ProviderWork.ps1') -Artifact $Artifact

function Stop-TestResident {
    $current = Get-Process PairTap -ErrorAction SilentlyContinue | Where-Object Path -eq $Artifact
    if (!$current) { return }
    $stop = [Threading.EventWaitHandle]::OpenExisting('Local\PairTap.Exit.63BA74EF')
    try { [void]$stop.Set(); $current | Wait-Process -Timeout 8 } finally { $stop.Dispose() }
}

if (!$Quick) {
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $integration = Join-Path $PSScriptRoot 'tests\Integration.ps1'
    $names = @('PAIRTAP_TEST_EXE', 'PAIRTAP_TEST_ADVERSARIAL', 'PAIRTAP_TEST_BROWSER',
        'PAIRTAP_TEST_BROWSER_IA2', 'PAIRTAP_TEST_WORD', 'PAIRTAP_TEST_TRAY', 'PAIRTAP_TEST_SETTINGS', 'PAIRTAP_TEST_SCREEN')
    $registryPath = 'Software\PairTap'
    $registry = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath, $true)
    $created = $null -eq $registry
    if ($created) { $registry = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath) }
    $hasShortcuts = $registry.GetValueNames() -contains 'Shortcuts'
    if ($hasShortcuts) {
        $oldValue = $registry.GetValue('Shortcuts', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        $oldKind = $registry.GetValueKind('Shortcuts')
    }
    $testDefaults = [string[]]@('version=2', 'style=0')
    $testSaved = [string[]]@('version=2', 'style=0', 'tortoise=393337')
    $saved = @{}
    foreach ($name in $names) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $null)
    }
    $failed = @()
    try {
        $registry.SetValue('Shortcuts', $testDefaults, [Microsoft.Win32.RegistryValueKind]::MultiString)
        Start-Process -FilePath $Artifact -WindowStyle Hidden
        $env:PAIRTAP_TEST_EXE = $Artifact
        if (!$SettingsOnly) {
            $env:PAIRTAP_TEST_WORD = '1'
            & $pwsh -NoProfile -STA -File $integration
            if ($LASTEXITCODE -ne 0) { $failed += 'Word' }
            Remove-Item Env:PAIRTAP_TEST_WORD
            $env:PAIRTAP_TEST_ADVERSARIAL = '1'
            $env:PAIRTAP_TEST_BROWSER = '1'
            & $pwsh -NoProfile -STA -File $integration
            if ($LASTEXITCODE -ne 0) { $failed += 'Browser and native editors' }
            $env:PAIRTAP_TEST_TRAY = '1'
            & $pwsh -NoProfile -STA -File $integration
            if ($LASTEXITCODE -ne 0) { $failed += 'Tray' }
            Remove-Item Env:PAIRTAP_TEST_TRAY
            try { & (Join-Path $PSScriptRoot 'tests\DpiLayout.ps1') -RequireMixedDpi:$RequireMixedDpi }
            catch { Write-Warning $_.Exception.Message; $failed += 'DPI' }
        }
        $env:PAIRTAP_TEST_SETTINGS = '1'
        & $pwsh -NoProfile -STA -File $integration
        if ($LASTEXITCODE -ne 0) { $failed += 'Shortcut settings' }
        else {
            Stop-TestResident
            Start-Process -FilePath $Artifact -WindowStyle Hidden
            $env:PAIRTAP_TEST_SETTINGS = 'restart'
            & $pwsh -NoProfile -STA -File $integration
            if ($LASTEXITCODE -ne 0) { $failed += 'Shortcut persistence after restart' }
        }
        if ($failed.Count) { throw "Release checks failed for $($failed -join ', ')." }
    }
    finally {
        foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
        try { Stop-TestResident }
        finally {
            $removeEmpty = $false
            try {
                $currentValue = $registry.GetValue('Shortcuts')
                if ($currentValue -is [string[]] -and
                    (($currentValue -join [char]0) -ceq ($testDefaults -join [char]0) -or
                    ($currentValue -join [char]0) -ceq ($testSaved -join [char]0))) {
                    if ($hasShortcuts) { $registry.SetValue('Shortcuts', $oldValue, $oldKind) }
                    else { $registry.DeleteValue('Shortcuts', $false) }
                    $removeEmpty = $created -and $registry.ValueCount -eq 0 -and $registry.SubKeyCount -eq 0
                }
                else { Write-Warning 'Shortcuts changed outside the fixture. The current value was preserved.' }
            }
            finally { $registry.Dispose() }
            if ($removeEmpty) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($registryPath, $false) }
        }
    }
}

$checksum = (Get-FileHash -LiteralPath $Artifact -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path (Split-Path $Artifact) 'SHA256SUMS.txt'), "$checksum  PairTap.exe`n", [Text.Encoding]::ASCII)
if ($Quick) { 'PASS build checks. Interactive release checks were not run.' }
elseif ($SettingsOnly) { 'PASS shortcut settings and restart checks.' }
else { 'PASS interactive release checks.' }
