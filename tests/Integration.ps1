$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TestKeys
{
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int x, int y, int data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out Rect bounds);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr handle, uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int id, out Rect bounds, int size);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    public static Rect Bounds(IntPtr handle)
    {
        Rect bounds;
        if (DwmGetWindowAttribute(handle, 9, out bounds, Marshal.SizeOf(typeof(Rect))) != 0)
            throw new InvalidOperationException("Cannot capture palette bounds.");
        return bounds;
    }
    public static void Focus(IntPtr handle)
    {
        uint ignored;
        uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), out ignored);
        uint current = GetCurrentThreadId();
        bool attached = current != foreground && AttachThreadInput(current, foreground, true);
        try { SetForegroundWindow(handle); }
        finally { if (attached) AttachThreadInput(current, foreground, false); }
    }
    public static void Drag(int x, int y, int endX, int endY)
    {
        SetCursorPos(x,y);
        System.Threading.Thread.Sleep(100);
        mouse_event(2,0,0,0,UIntPtr.Zero);
        System.Threading.Thread.Sleep(50);
        for (int i=1; i<=8; i++)
        {
            SetCursorPos(x+(endX-x)*i/8,y+(endY-y)*i/8);
            System.Threading.Thread.Sleep(20);
        }
        mouse_event(4,0,0,0,UIntPtr.Zero);
    }
    public static bool DragCorner(IntPtr handle, int dx, int dy)
    {
        Rect box;
        GetWindowRect(handle, out box);
        for (int y=box.Bottom-3; y>=box.Bottom-20; y--)
        for (int x=box.Right-3; x>=box.Right-20; x--)
        {
            int coordinates = (y << 16) | (x & 0xffff);
            if (GetAncestor(WindowFromPoint(new Point { X=x, Y=y }), 2) != handle) continue;
            if (SendMessage(handle, 0x84, IntPtr.Zero, (IntPtr)coordinates).ToInt64() != 17) continue;
            Drag(x, y, x+dx, y+dy);
            return true;
        }
        return false;
    }

    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);

    public static void Symbol(byte key)
    {
        keybd_event(0x11,0,0,UIntPtr.Zero);
        keybd_event(0x10,0,0,UIntPtr.Zero);
        Stroke(key);
        keybd_event(0x10,0,2,UIntPtr.Zero);
        keybd_event(0x11,0,2,UIntPtr.Zero);
    }

    public static void Hotkey(byte number)
    {
        keybd_event(0x11,0,0,UIntPtr.Zero);
        keybd_event(0x12,0,0,UIntPtr.Zero);
        keybd_event(number,0,0,UIntPtr.Zero);
        keybd_event(number,0,2,UIntPtr.Zero);
        keybd_event(0x12,0,2,UIntPtr.Zero);
        keybd_event(0x11,0,2,UIntPtr.Zero);
    }
    public static void Click(int x, int y)
    {
        SetCursorPos(x,y);
        mouse_event(2,0,0,0,UIntPtr.Zero);
        mouse_event(4,0,0,0,UIntPtr.Zero);
    }
    public static void Wheel(int x, int y, int delta)
    {
        SetCursorPos(x, y);
        mouse_event(0x800, 0, 0, delta, UIntPtr.Zero);
    }
    public static uint Owner(IntPtr handle)
    {
        uint owner;
        GetWindowThreadProcessId(handle, out owner);
        return owner;
    }
    public static void Enter()
    {
        keybd_event(0x0D,0,0,UIntPtr.Zero);
        keybd_event(0x0D,0,2,UIntPtr.Zero);
    }
    public static void Stroke(byte key)
    {
        keybd_event(key,0,0,UIntPtr.Zero);
        keybd_event(key,0,2,UIntPtr.Zero);
    }
    public static int SelectedRow(IntPtr list)
    {
        return SendMessage(list, 0x100c, (IntPtr)(-1), (IntPtr)2).ToInt32();
    }
    public static void ClipboardUpdate(IntPtr window)
    {
        SendMessage(window, 0x031d, IntPtr.Zero, IntPtr.Zero);
    }
}
'@
function Pump($ms) {
    $until = [DateTime]::UtcNow.AddMilliseconds($ms)
    do {
        [Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 15
    } while ([DateTime]::UtcNow -lt $until)
}
function Wait-Condition([scriptblock]$condition, [int]$timeout) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        [Windows.Forms.Application]::DoEvents()
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 5
    } while ($watch.ElapsedMilliseconds -lt $timeout)
    return $false
}
function Assert($ok, $message) { if (!$ok) { throw $message }; Write-Output "PASS $message" }
if ($env:PAIRTAP_TEST_TRAY) {
    & (Join-Path $PSScriptRoot 'Tray.ps1')
    return
}
$assembly = [Reflection.Assembly]::LoadFile($env:PAIRTAP_TEST_EXE)
$bufferType = $assembly.GetType('PairTap.ClipboardBuffer')
$sequenceMethod = $bufferType.GetMethod('GetClipboardSequenceNumber',[Reflection.BindingFlags]'Static,NonPublic')
if ($env:PAIRTAP_TEST_WORD) {
    & (Join-Path $PSScriptRoot 'Word.ps1')
    return
}
$form = New-Object Windows.Forms.Form
$form.Text = 'PairTap integration fixture'
$form.Width = 500
$form.Height = 240
$edit = New-Object Windows.Forms.TextBox
$edit.Multiline = $true
$edit.Dock = 'Fill'
$form.Controls.Add($edit)
$form.Show()
Pump 100
$form.Activate()
[TestKeys]::Focus($form.Handle)
[void]$edit.Focus()
Pump 100
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$backup = $bufferType.GetConstructors($flags)[0].Invoke(@($form.Handle))
$task = $bufferType.GetMethod('Capture',$flags).Invoke($backup,@())
while (!$task.IsCompleted) { Pump 30 }
[void]$task.GetAwaiter().GetResult()
try {
    $data = New-Object Windows.Forms.DataObject
    $data.SetData([Windows.Forms.DataFormats]::UnicodeText, 'Clipboard fixture')
    $data.SetData([Windows.Forms.DataFormats]::Rtf, '{\rtf1 Clipboard fixture}')
    [Windows.Forms.Clipboard]::SetDataObject($data, $true)
    $edit.Text = '前缀人工智能后缀'
    $edit.Select(2,4)
    Assert ([TestKeys]::GetForegroundWindow() -eq $form.Handle) 'fixture focused'
    [TestKeys]::Symbol(0xDE)
    Pump 1500
    $doubleQuotes = @('前缀“人工智能”后缀', '前缀"人工智能"后缀', '前缀＂人工智能＂后缀')
    Assert ($doubleQuotes -ccontains $edit.Text) "global hotkey wraps selected substring (actual $($edit.Text))"
    Assert ([Windows.Forms.Clipboard]::GetText() -ceq 'Clipboard fixture') 'plain clipboard restored'
    Assert ([Windows.Forms.Clipboard]::GetText([Windows.Forms.TextDataFormat]::Rtf) -ceq '{\rtf1 Clipboard fixture}') 'RTF clipboard restored'
    $edit.Select(2,0)
    [TestKeys]::Symbol(0x39)
    Pump 1500
    Assert ($doubleQuotes -ccontains $edit.Text) 'empty selection unchanged'
    $edit.Text = "甲"+[Environment]::NewLine+"乙"
    [TestKeys]::Focus($form.Handle)
    [void]$edit.Focus()
    $edit.SelectAll()
    Assert ($edit.SelectedText -ceq $edit.Text -and [TestKeys]::GetForegroundWindow() -eq $form.Handle) 'multiline selection focused'
    [TestKeys]::Symbol(0xBC)
    Pump 1500
    $multiline = "甲"+[Environment]::NewLine+"乙"
    Assert (@(("《"+$multiline+"》"), ("<"+$multiline+">"), ("＜"+$multiline+"＞")) -ccontains $edit.Text) "multiline text wrapped (actual $($edit.Text))"
    [TestKeys]::Focus($form.Handle)
    [void]$edit.Focus()
    $edit.Text = 'AbC'
    $edit.SelectAll()
    [TestKeys]::Symbol(0x30)
    Pump 700
    Assert (@('（AbC）', '(AbC)') -ccontains $edit.Text) "right parenthesis key wraps text and preserves its case (actual $($edit.Text))"
    if ($env:PAIRTAP_TEST_ADVERSARIAL) {
        [TestKeys]::Focus($form.Handle)
        [void]$edit.Focus()
        $edit.MaxLength = 4
        $edit.Text = 'ABcd'
        $edit.SelectAll()
        [TestKeys]::Symbol(0xDE)
        Pump 350
        Assert ($edit.Text -ceq 'ABcd') 'length-limited control preserves the original selection'
        $edit.Multiline = $false
        $edit.PasswordChar = '*'
        [TestKeys]::Focus($form.Handle)
        [void]$edit.Focus()
        $edit.SelectAll()
        [TestKeys]::Symbol(0xDE)
        Pump 350
        Assert ($edit.Text -ceq 'ABcd' -and [Windows.Forms.Clipboard]::GetText() -ceq 'Clipboard fixture') 'masked edit never changes text or clipboard'
        $edit.PasswordChar = [char]0
        $edit.Multiline = $true

        $edit.MaxLength = 150000
        $prefix = 'x' * 70000
        $edit.Text = $prefix + 'AbC'
        [TestKeys]::Focus($form.Handle)
        [void]$edit.Focus()
        $edit.Select(70000, 3)
        Assert ($edit.SelectedText -ceq 'AbC') 'fixture selected past the 16-bit offset'
        $watch = [Diagnostics.Stopwatch]::StartNew()
        [TestKeys]::Symbol(0x39)
        Assert (Wait-Condition { $edit.Text.Length -eq 70005 } 1100) 'selection after position 65,535 stays on the native path'
        Assert ($edit.Text -ceq ($prefix + '（AbC）') -or $edit.Text -ceq ($prefix + '(AbC)')) "long-document native wrap in $($watch.ElapsedMilliseconds) ms"

        $probe = $bufferType.GetConstructors($flags)[0].Invoke(@($form.Handle))
        try {
            $copy = $bufferType.GetMethod('Capture',$flags).Invoke($probe,@())
            Assert (Wait-Condition { $copy.IsCompleted } 1500) 'adversarial snapshot ready'
            [void]$copy.GetAwaiter().GetResult()
            $expected = $sequenceMethod.Invoke($null,@())
            [Windows.Forms.Clipboard]::SetText('Concurrent clipboard fixture')
            Assert (!$bufferType.GetMethod('Restore',$flags).Invoke($probe,@($expected))) 'stale clipboard snapshot cannot overwrite a newer copy'
            Assert ([Windows.Forms.Clipboard]::GetText() -ceq 'Concurrent clipboard fixture') 'concurrent clipboard value preserved'
        }
        finally { ([IDisposable]$probe).Dispose() }
        $channelType = $assembly.GetType('PairTap.ClipboardChannel')
        $channel = $channelType.GetConstructors($flags)[0].Invoke(@())
        try {
            $stampType = $assembly.GetType('PairTap.ClipboardStamp')
            $stamp = $stampType.GetProperty('Current',[Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
            $copy = $channelType.GetMethod('WatchCopy',$flags).Invoke($channel,@($stamp))
            [TestKeys]::ClipboardUpdate($channel.Handle)
            Assert (!$copy.IsCompleted) 'stale clipboard notification is ignored'
            [Windows.Forms.Clipboard]::SetText('Fresh clipboard notification fixture')
            Assert (Wait-Condition { $copy.IsCompleted } 1500) 'a real clipboard change completes the listener'
            Assert ($null -ne $copy.GetAwaiter().GetResult()) 'the listener retains the new clipboard stamp'
        }
        finally { ([IDisposable]$channel).Dispose() }
        [Windows.Forms.Clipboard]::SetDataObject($data, $true)
    }
    $edit.Text = '面板'
    [TestKeys]::Focus($form.Handle)
    [void]$edit.Focus()
    $edit.SelectAll()
    Assert ([TestKeys]::GetForegroundWindow() -eq $form.Handle) 'palette target focused'
    [TestKeys]::Hotkey(0x75)
    Pump 400
    if ($env:PAIRTAP_TEST_SCREEN) {
        $paletteWindow = [TestKeys]::GetForegroundWindow()
        $ownerId = [TestKeys]::Owner($paletteWindow)
        Assert ((Get-Process -Id $ownerId).Path -eq $env:PAIRTAP_TEST_EXE) 'palette receives keyboard focus'
        $bounds = [TestKeys]::Bounds($paletteWindow)
        $bitmap = New-Object Drawing.Bitmap ($bounds.Right - $bounds.Left), ($bounds.Bottom - $bounds.Top)
        try {
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try { $graphics.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, $bitmap.Size) }
            finally { $graphics.Dispose() }
            $bitmap.Save($env:PAIRTAP_TEST_SCREEN, [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $bitmap.Dispose() }
    }
    [TestKeys]::Enter()
    Pump 1500
    Assert ($edit.Text -ceq '“面板”') "palette selection restores focus and applies (actual $($edit.Text))"
    $edit.Text = '点击'
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        [TestKeys]::Focus($form.Handle)
        [void]$edit.Focus()
        $edit.SelectAll()
        [TestKeys]::Hotkey(0x75)
        Pump 350
        $clickWindow = [TestKeys]::GetForegroundWindow()
        if ((Get-Process -Id ([TestKeys]::Owner($clickWindow))).Path -eq $env:PAIRTAP_TEST_EXE) { break }
    }
    Assert ((Get-Process -Id ([TestKeys]::Owner($clickWindow))).Path -eq $env:PAIRTAP_TEST_EXE) 'mouse test has the palette focused'
    $bounds = [TestKeys]::Bounds($clickWindow)
    [TestKeys]::Click($bounds.Left + 70, $bounds.Top + 182)
    Pump 1500
    Assert ($edit.Text -ceq '“点击”') 'palette applies after a mouse click'
    $edit.Text = '只读'
    [TestKeys]::Focus($form.Handle)
    [void]$edit.Focus()
    $edit.ReadOnly = $true
    $edit.SelectAll()
    [TestKeys]::Symbol(0xDE)
    Pump 1050
    Assert ($edit.Text -ceq '只读') 'read-only control unchanged'

    if ($env:PAIRTAP_TEST_SETTINGS) {
        Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
        function FindUI($root, $name) {
            $property = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::NameProperty), $name
            return $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $property)
        }
        function InvokeUI($root, $name) {
            $element = FindUI $root $name
            Assert ($null -ne $element) "found $name"
            $box = $element.Current.BoundingRectangle
            [TestKeys]::Click([int]($box.Left + $box.Width / 2), [int]($box.Top + $box.Height / 2))
            Pump 240
        }
        function SaveShot($window, $path) {
            if (!$path) { return }
            $box = [TestKeys]::Bounds($window)
            $bitmap = New-Object Drawing.Bitmap ($box.Right - $box.Left), ($box.Bottom - $box.Top)
            try {
                $graphics = [Drawing.Graphics]::FromImage($bitmap)
                try { $graphics.CopyFromScreen($box.Left, $box.Top, 0, 0, $bitmap.Size) }
                finally { $graphics.Dispose() }
                $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
            }
            finally { $bitmap.Dispose() }
        }
        $edit.ReadOnly = $false
        $edit.Text = '新版功能'
        for ($attempt = 0; $attempt -lt 5; $attempt++) {
            [TestKeys]::Focus($form.Handle)
            [void]$edit.Focus()
            $edit.SelectAll()
            Pump 100
            [TestKeys]::Hotkey(0x75)
            Pump 420
            $window = [TestKeys]::GetForegroundWindow()
            if ((Get-Process -Id ([TestKeys]::Owner($window))).Path -eq $env:PAIRTAP_TEST_EXE) { break }
        }
        Assert ((Get-Process -Id ([TestKeys]::Owner($window))).Path -eq $env:PAIRTAP_TEST_EXE) 'settings test has the palette focused'
        SaveShot $window $env:PAIRTAP_TEST_SCREEN
        if ($env:PAIRTAP_TEST_COMPACT) {
            $before = [TestKeys]::Bounds($window)
            Assert ([TestKeys]::DragCorner($window, -352, -266)) 'the palette exposes a native sizing corner'
            Pump 450
            $after = [TestKeys]::Bounds($window)
            Assert (($after.Right - $after.Left) -lt ($before.Right - $before.Left)) "dragging the frame shrinks the palette from $($before.Right - $before.Left) to $($after.Right - $after.Left)"
            Assert (($after.Bottom - $after.Top) -lt ($before.Bottom - $before.Top)) 'dragging the frame shrinks the palette height'
            SaveShot $window $env:PAIRTAP_TEST_COMPACT
            $compact = [Windows.Automation.AutomationElement]::FromHandle($window)
            $tile = FindUI $compact '双引号'
            $top = $tile.Current.BoundingRectangle.Top
            [TestKeys]::Wheel($after.Left + 80, $after.Top + 195, -120)
            Pump 250
            Assert ($tile.Current.BoundingRectangle.Top -lt $top) 'mouse wheel scrolls the compact palette'
        }
        $root = [Windows.Automation.AutomationElement]::FromHandle($window)
        InvokeUI $root '快捷键设置'
        $settingsWindow = [TestKeys]::GetForegroundWindow()
        $settings = [Windows.Automation.AutomationElement]::FromHandle($settingsWindow)
        Assert ($settings.Current.Name -eq '对符输入 · 快捷键设置') 'shortcut settings opens'
        $controls = $settings.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
        $list = $controls | Where-Object { $_.Current.ClassName -like '*SysListView32*' } | Select-Object -First 1
        $listBox = $list.Current.BoundingRectangle
        [TestKeys]::Click([int]($listBox.Left + 45), [int]($listBox.Top + 34))
        Pump 100
        [TestKeys]::Stroke(0x24)
        for ($row = 0; $row -lt 12; $row++) { [TestKeys]::Stroke(0x28); Pump 35 }
        Pump 200
        Assert ([TestKeys]::SelectedRow([IntPtr]$list.Current.NativeWindowHandle) -eq 12) 'new punctuation has a settings row'
        $field = $controls | Where-Object { $_.Current.ClassName -like '*EDIT*' } | Select-Object -First 1
        Assert ($null -ne $field) 'shortcut capture is keyboard accessible'
        $fieldBox = $field.Current.BoundingRectangle
        [TestKeys]::Click([int]($fieldBox.Left + 30), [int]($fieldBox.Top + $fieldBox.Height / 2))
        Pump 120
        [TestKeys]::Symbol(0xDE)
        Pump 300
        Assert ($null -ne (FindUI $settings '该组合键已分配给「跟随样式 · 双引号」。')) 'duplicate shortcut is rejected'
        [TestKeys]::Hotkey(0x79)
        Pump 300
        Assert ([TestKeys]::RegisterHotKey([IntPtr]::Zero, 999, 3, 0x79)) 'test process can simulate a system hotkey conflict'
        try {
            InvokeUI $settings '保存'
            Assert ($null -ne (FindUI $settings '无法注册 Ctrl+Alt+F10（六角括号）。请改用其他组合键。')) 'occupied shortcut is rejected'
        }
        finally { [void][TestKeys]::UnregisterHotKey([IntPtr]::Zero, 999) }
        SaveShot $settingsWindow $env:PAIRTAP_TEST_SETTINGS_SCREEN
        InvokeUI $settings '保存'
        for ($attempt = 0; $attempt -lt 3 -and $edit.Text -ceq '新版功能'; $attempt++) {
            [TestKeys]::Focus($form.Handle)
            [void]$edit.Focus()
            $edit.SelectAll()
            Pump 100
            if ([TestKeys]::GetForegroundWindow() -eq $form.Handle) { [TestKeys]::Hotkey(0x79) }
            Pump 500
        }
        Assert ($edit.Text -ceq '〔新版功能〕') "custom hotkey immediately wraps the selection (actual $($edit.Text))"
        $stored = (Get-ItemProperty -LiteralPath 'HKCU:\Software\PairTap' -Name Shortcuts).Shortcuts
        Assert ($stored -contains 'tortoise=393337') 'custom hotkey is persisted'
    }

    $chrome = Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'
    if ($env:PAIRTAP_TEST_BROWSER -and (Test-Path -LiteralPath $chrome)) {
        $fixture = Join-Path $env:TEMP ('PairTap-Chrome-' + [Guid]::NewGuid().ToString('N'))
        [void](New-Item -ItemType Directory -Path $fixture)
        $page = Join-Path $fixture 'editor.html'
        @'
<!doctype html><meta charset="utf-8"><title>PairTap browser fixture ready</title>
<style>textarea { width: calc(100% - 32px); height: 320px; margin: 12px; font-size: 26px; }</style>
<textarea id="editor">甲中英混排乙</textarea>
<script>
const editor = document.getElementById('editor');
editor.addEventListener('mouseup', () => {
    editor.setSelectionRange(1, 5);
    document.title = 'PairTap browser fixture selected';
});
editor.addEventListener('copy', (event) => {
    if (editor.selectionStart !== editor.selectionEnd) return;
    event.clipboardData.setData('text/plain', 'unsafe implicit line copy');
    event.preventDefault();
    document.title = 'PairTap browser fixture unsafe copy';
});
editor.addEventListener('input', () => {
    document.title = ['甲《中英混排》乙', '甲<中英混排>乙', '甲＜中英混排＞乙'].includes(editor.value)
        ? 'PairTap browser fixture passed'
        : 'PairTap browser fixture mismatch: ' + editor.value;
});
</script>
'@ | Set-Content -LiteralPath $page -Encoding UTF8
        $browser = Start-Process -FilePath $chrome -PassThru -ArgumentList @(
            ('--user-data-dir="' + (Join-Path $fixture 'profile') + '"'),
            '--no-first-run', '--disable-sync', '--disable-background-networking',
            '--new-window', ('"' + ([Uri]$page).AbsoluteUri + '"')
        )
        try {
            for ($i = 0; $i -lt 32; $i++) {
                Pump 250
                $browser.Refresh()
                if ($browser.MainWindowTitle -like 'PairTap browser fixture ready*') { break }
            }
            Assert ($browser.MainWindowTitle -like 'PairTap browser fixture ready*') 'isolated Chrome text editor loaded'
            for ($i = 0; $i -lt 8; $i++) {
                [TestKeys]::Focus($browser.MainWindowHandle)
                Pump 100
                $browserBounds = [TestKeys]::Bounds($browser.MainWindowHandle)
                if ([TestKeys]::GetForegroundWindow() -eq $browser.MainWindowHandle) {
                    [TestKeys]::Click($browserBounds.Left + 120, $browserBounds.Top + 225)
                }
                Pump 180
                $browser.Refresh()
                if ($browser.MainWindowTitle -like 'PairTap browser fixture selected*') { break }
            }
            Assert ($browser.MainWindowTitle -like 'PairTap browser fixture selected*') 'Chrome textarea selection receives focus'
            $watch = [Diagnostics.Stopwatch]::StartNew()
            [TestKeys]::Symbol(0xBC)
            Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'PairTap browser fixture passed*' } 1500) "browser selection wrapped (actual $($browser.MainWindowTitle))"
            $pasteTime = $watch.ElapsedMilliseconds
            # Query the clipboard only after the browser has actually applied the paste.
            Assert (Wait-Condition { [Windows.Forms.Clipboard]::GetText() -ceq 'Clipboard fixture' } 1300) 'browser clipboard recovered'
            Write-Output "TIMING browser paste $pasteTime ms, clipboard restored $($watch.ElapsedMilliseconds) ms from keypress"
            Assert ([Windows.Forms.Clipboard]::GetText() -ceq 'Clipboard fixture') 'browser operation restores clipboard text'
            Assert ([Windows.Forms.Clipboard]::GetText([Windows.Forms.TextDataFormat]::Rtf) -ceq '{\rtf1 Clipboard fixture}') 'browser operation restores clipboard RTF'
            if ($env:PAIRTAP_TEST_ADVERSARIAL) {
                [TestKeys]::Stroke(0x23)
                Pump 100
                [TestKeys]::Symbol(0xBC)
                Pump 800
                $browser.Refresh()
                Assert ($browser.MainWindowTitle -like 'PairTap browser fixture passed*') 'empty browser selection does not trigger copy-line behavior'
                Assert ([Windows.Forms.Clipboard]::GetText() -ceq 'Clipboard fixture') 'empty browser selection never replaces the clipboard'
            }
        }
        finally {
            if (!$browser.HasExited) {
                [void]$browser.CloseMainWindow()
                [void]$browser.WaitForExit(4000)
            }
            $browser.Dispose()
            $resolved = (Resolve-Path -LiteralPath $fixture).ProviderPath
            $tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
            if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
finally {
    if ([Windows.Forms.Clipboard]::GetText() -ceq 'Clipboard fixture') {
        $sequence = $sequenceMethod.Invoke($null,@())
        [void]$bufferType.GetMethod('Restore',$flags).Invoke($backup,@($sequence))
    }
    ([IDisposable]$backup).Dispose()
    $form.Close()
    $form.Dispose()
}
