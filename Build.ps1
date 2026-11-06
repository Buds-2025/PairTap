param([switch]$IncludeInstaller)

$ErrorActionPreference = 'Stop'

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$app = Join-Path $dist 'PairTap.exe'
$icon = Join-Path $PSScriptRoot 'assets\PairTap.ico'
$source = Get-ChildItem -Path (Join-Path $PSScriptRoot 'src\*.cs') | ForEach-Object FullName
& $compiler /nologo /warnaserror /target:winexe /optimize+ /codepage:65001 "/out:$app" "/win32icon:$icon" "/resource:$icon,brand" "/r:$framework\WPF\UIAutomationClient.dll" "/r:$framework\WPF\UIAutomationTypes.dll" "/r:$framework\WPF\WindowsBase.dll" $source
if ($LASTEXITCODE -ne 0) { throw '程序编译失败。' }
Get-Item -LiteralPath $app

if (!$IncludeInstaller) { return }

$installer = Join-Path $dist 'PairTap-Setup.exe'
$uninstaller = Join-Path $env:TEMP ('PairTap-Uninstall-' + [Guid]::NewGuid().ToString('N') + '.ps1')
try {
    # Windows PowerShell 5.1 uses the BOM to identify UTF-8 scripts.
    $script = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'installer\Uninstall.ps1'))
    [IO.File]::WriteAllText($uninstaller, $script, [Text.UTF8Encoding]::new($true))
    & $compiler /nologo /warnaserror /target:winexe /optimize+ /codepage:65001 "/out:$installer" "/win32icon:$icon" "/resource:$app,app" "/resource:$uninstaller,uninstaller" (Join-Path $PSScriptRoot 'installer\Setup.cs') (Join-Path $PSScriptRoot 'src\Product.cs')
    if ($LASTEXITCODE -ne 0) { throw '安装程序编译失败。' }
}
finally {
    if (Test-Path -LiteralPath $uninstaller) { Remove-Item -LiteralPath $uninstaller }
}
Get-Item -LiteralPath $installer
