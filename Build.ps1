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
