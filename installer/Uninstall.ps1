$ErrorActionPreference = 'Stop'
$lock = [Threading.Mutex]::new($false, 'Local\PairTap.Setup.63BA74EF')
$entered = $false

try {
    try { $entered = $lock.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $entered = $true }
    if (!$entered) { throw '安装或卸载正在进行。' }
    $installDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\PairTap'
    if ((Test-Path -LiteralPath $installDir) -and
        ([IO.File]::GetAttributes($installDir) -band [IO.FileAttributes]::ReparsePoint)) {
        throw '安装目录是重解析点。请先恢复原安装目录。'
    }

    $signal = $null
    $signalled = $false
    for ($i = 0; $i -lt 100; $i++) {
        $instance = $null
        if (![Threading.Mutex]::TryOpenExisting('Local\PairTap.63BA74EF', [ref]$instance)) { break }
        $instance.Dispose()
        if (!$signalled -and [Threading.EventWaitHandle]::TryOpenExisting('Local\PairTap.Exit.63BA74EF', [ref]$signal)) {
            try { $signalled = $signal.Set() } finally { $signal.Dispose() }
        }
        Start-Sleep -Milliseconds 50
    }
    if ($i -eq 100) { throw '请先从系统托盘退出对符输入，再卸载。' }

    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) '对符输入.lnk'
    $registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PairTap'
    foreach ($target in @((Join-Path $installDir 'PairTap.exe'), $shortcut, $registry, (Join-Path $installDir 'Uninstall.ps1'))) {
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
    }
    if ((Test-Path -LiteralPath $installDir) -and !(Get-ChildItem -LiteralPath $installDir -Force | Select-Object -First 1)) {
        [IO.Directory]::Delete($installDir)
    }
}
catch {
    Add-Type -AssemblyName System.Windows.Forms
    [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, '无法卸载对符输入')
    exit 1
}
finally {
    if ($entered) { $lock.ReleaseMutex() }
    $lock.Dispose()
}
