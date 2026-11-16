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
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr handle, int command);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool OpenClipboard(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    public static int UnicodeRestoreAttempts;
    public static void UsePhysicalCoordinates()
    {
        if (SetThreadDpiAwarenessContext((IntPtr)(-4)) == IntPtr.Zero)
            throw new InvalidOperationException("Could not set the fixture DPI context.");
    }
    public static IntPtr PublishWithTransientFailure(uint format, IntPtr memory)
    {
        if (format == 13 && ++UnicodeRestoreAttempts <= 4) return IntPtr.Zero;
        return SetClipboardData(format, memory);
    }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out Rect bounds);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr handle, VisitWindow visit, IntPtr argument);
    [DllImport("user32.dll")] private static extern bool EnumWindows(VisitWindow visit, IntPtr argument);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, System.Text.StringBuilder name, int capacity);
    private delegate bool VisitWindow(IntPtr handle, IntPtr argument);
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
    public static bool FocusFixture(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;
        if (IsIconic(handle))
        {
            ShowWindowAsync(handle, 9);
            return false;
        }
        if (GetForegroundWindow() == handle) return true;
        Focus(handle);
        if (GetForegroundWindow() == handle) return true;
        Rect bounds;
        if (!GetWindowRect(handle, out bounds) || bounds.Left >= bounds.Right || bounds.Top >= bounds.Bottom) return false;
        int x = bounds.Left + Math.Min(100, (bounds.Right - bounds.Left) / 3);
        int y = bounds.Top + 16;
        if (RootAt(x, y) == handle) Click(x, y);
        return GetForegroundWindow() == handle;
    }
    public static void RaiseFixture(IntPtr handle, bool raise)
    {
        const uint keepBoundsAndActivation = 0x0001 | 0x0002 | 0x0010;
        SetWindowPos(handle, (IntPtr)(raise ? -1 : -2), 0, 0, 0, 0, keepBoundsAndActivation);
    }
    public static IntPtr FindEditorWindow(uint process, string className)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((root, unused) =>
        {
            if (Owner(root) != process || !IsWindowVisible(root)) return true;
            bool containsEditor = false;
            EnumChildWindows(root, (child, parameter) =>
            {
                var name = new System.Text.StringBuilder(128);
                GetClassName(child, name, name.Capacity);
                containsEditor = name.ToString() == className && IsWindowVisible(child);
                return !containsEditor;
            }, IntPtr.Zero);
            if (!containsEditor) return true;
            if (result != IntPtr.Zero) { result = IntPtr.Zero; return false; }
            result = root;
            return true;
        }, IntPtr.Zero);
        return result;
    }
    public static bool FocusEditor(IntPtr root, string className)
    {
        if (root == IntPtr.Zero || GetForegroundWindow() != root) return false;
        bool clicked = false;
        EnumChildWindows(root, (child, unused) =>
        {
            var name = new System.Text.StringBuilder(128);
            GetClassName(child, name, name.Capacity);
            Rect bounds;
            if (name.ToString() != className || !IsWindowVisible(child) || !GetWindowRect(child, out bounds)) return true;
            // Clicking the editing surface lets Word switch away from its navigation pane.
            // Setting Win32 focus alone can be undone when the input queues are detached.
            foreach (int divisor in new[] { 2, 4, 8 })
            {
                var point = new Point { X = bounds.Left + (bounds.Right - bounds.Left) / divisor,
                    Y = bounds.Top + (bounds.Bottom - bounds.Top) / divisor };
                if (WindowFromPoint(point) != child || GetForegroundWindow() != root) continue;
                Click(point.X, point.Y);
                clicked = true;
                return false;
            }
            uint ignored;
            uint editorThread = GetWindowThreadProcessId(child, out ignored);
            uint caller = GetCurrentThreadId();
            bool attached = editorThread != caller && AttachThreadInput(caller, editorThread, true);
            try
            {
                if ((attached || editorThread == caller) && GetForegroundWindow() == root)
                {
                    SetFocus(child);
                    if (GetFocus() == child) { clicked = true; return false; }
                }
            }
            finally { if (attached) AttachThreadInput(caller, editorThread, false); }
            return true;
        }, IntPtr.Zero);
        return clicked;
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
    public static IntPtr RootAt(int x, int y)
    {
        return GetAncestor(WindowFromPoint(new Point { X = x, Y = y }), 2);
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
[TestKeys]::UsePhysicalCoordinates()
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
$stampType = $assembly.GetType('PairTap.ClipboardStamp')
$currentStamp = $stampType.GetProperty('Current', [Reflection.BindingFlags]'Static,NonPublic')
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
Assert (Wait-Condition { [TestKeys]::FocusFixture($form.Handle) } 2500) 'native fixture can take focus'
[void]$edit.Focus()
Pump 100
function Open-FixturePalette {
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        [void][TestKeys]::FocusFixture($form.Handle)
        [void]$edit.Focus()
        $edit.SelectAll()
        if ([TestKeys]::GetForegroundWindow() -ne $form.Handle) { Pump 100; continue }
        [TestKeys]::Hotkey(0x75)
        if (Wait-Condition {
            $foreground = [TestKeys]::GetForegroundWindow()
            $process = Get-Process -Id ([TestKeys]::Owner($foreground)) -ErrorAction SilentlyContinue
            $process.Path -eq $env:PAIRTAP_TEST_EXE
        } 750) { return [TestKeys]::GetForegroundWindow() }
    }
    throw 'The test palette did not receive focus.'
}
if ($env:PAIRTAP_TEST_SETTINGS) {
    try { & (Join-Path $PSScriptRoot 'Settings.ps1') }
    finally { $form.Close(); $form.Dispose() }
    return
}
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$backup = $bufferType.GetConstructors($flags)[0].Invoke(@($form.Handle))
$task = $bufferType.GetMethod('Capture',$flags).Invoke($backup,@())
while (!$task.IsCompleted) { Pump 30 }
[void]$task.GetAwaiter().GetResult()
$fixtureRestoreStamp = $null
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
            $stamp = $currentStamp.GetValue($null)
            $copy = $channelType.GetMethod('WatchCopy',$flags).Invoke($channel,@($stamp))
            [TestKeys]::ClipboardUpdate($channel.Handle)
            Assert (!$copy.IsCompleted) 'stale clipboard notification is ignored'
            [Windows.Forms.Clipboard]::SetText('Fresh clipboard notification fixture')
            Assert (Wait-Condition { $copy.IsCompleted } 1500) 'a real clipboard change completes the listener'
            Assert ($null -ne $copy.GetAwaiter().GetResult()) 'the listener retains the new clipboard stamp'
        }
        finally { ([IDisposable]$channel).Dispose() }
        [Windows.Forms.Clipboard]::SetDataObject($data, $true)
        $recovery = $bufferType.GetConstructors($flags)[0].Invoke(@($form.Handle))
        try {
            $capture = $bufferType.GetMethod('Capture',$flags).Invoke($recovery,@())
            Assert (Wait-Condition { $capture.IsCompleted } 1500) 'multi-format recovery snapshot ready'
            [void]$capture.GetAwaiter().GetResult()
            [Windows.Forms.Clipboard]::SetText('Clipboard restore failure fixture')
            $stamp = $currentStamp.GetValue($null)
            $method = $bufferType.GetMethods($flags) | Where-Object {
                $_.Name -eq 'RestoreIfCurrent' -and $_.GetParameters().Length -eq 3
            }
            $publish = [Delegate]::CreateDelegate([Func[uint32,IntPtr,IntPtr]], [TestKeys].GetMethod('PublishWithTransientFailure'))
            $restoreResult = $method.Invoke($recovery,@($stamp,$false,$publish)).ToString()
            $fixtureRestoreStamp = $currentStamp.GetValue($null)
            Assert ($restoreResult -eq 'Restored') "a format that fails the first restore pass recovers after reopening the clipboard (result $restoreResult, attempts $([TestKeys]::UnicodeRestoreAttempts))"
            Assert ([TestKeys]::UnicodeRestoreAttempts -eq 5) 'the failed Unicode format is retried after four failures'
            Assert (Wait-Condition {
                try {
                    [Windows.Forms.Clipboard]::GetText() -ceq 'Clipboard fixture' -and
                    [Windows.Forms.Clipboard]::GetText([Windows.Forms.TextDataFormat]::Rtf) -ceq '{\rtf1 Clipboard fixture}'
                }
                catch [Runtime.InteropServices.ExternalException] { $false }
            } 900) 'format retry restores text and RTF'
        }
        finally { ([IDisposable]$recovery).Dispose() }
    }
    $edit.Text = '面板'
    $paletteWindow = Open-FixturePalette
    if ($env:PAIRTAP_TEST_SCREEN) {
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
    $clickWindow = Open-FixturePalette
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $paletteRoot = [Windows.Automation.AutomationElement]::FromHandle($clickWindow)
    $tileName = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::NameProperty), '双引号'
    $tile = $paletteRoot.FindFirst([Windows.Automation.TreeScope]::Descendants, $tileName)
    Assert ($null -ne $tile) 'palette exposes the double quote tile'
    $box = $tile.Current.BoundingRectangle
    $x = [int]($box.Left + $box.Width / 2)
    $y = [int]($box.Top + $box.Height / 2)
    Assert (Wait-Condition { [TestKeys]::RootAt($x, $y) -eq $clickWindow } 2400) 'the double quote tile is under the pointer'
    [TestKeys]::Click($x, $y)
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

    $chrome = Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'
    if ($env:PAIRTAP_TEST_BROWSER -and (Test-Path -LiteralPath $chrome)) {
        $fixture = Join-Path $env:TEMP ('PairTap-Chrome-' + [Guid]::NewGuid().ToString('N'))
        [void](New-Item -ItemType Directory -Path $fixture)
        $page = Join-Path $fixture 'editor.html'
        @'
<!doctype html><meta charset="utf-8"><title>PairTap browser fixture ready</title>
<style>textarea { width: calc(100% - 32px); height: 320px; margin: 12px; font-size: 26px; }</style>
<textarea id="editor">甲中英混排乙</textarea>
<input id="locked" value="只读选区" readonly>
<div id="rich" contenteditable="true"><p>甲<span>富文本AbC</span>乙</p></div>
<div id="multi" contenteditable="true"><p>甲文本</p><p>AbC乙</p></div>
<input id="different-copy" value="甲中🙂Ab乙">
<input id="no-copy" value="甲中🙂Ab乙">
<input id="no-paste" value="甲中🙂Ab乙">
<input id="locked-clipboard" value="甲中🙂Ab乙">
<input id="max-length" maxlength="7" value="甲中🙂Ab乙">
<input id="one-slot" maxlength="8" value="甲中🙂Ab乙">
<textarea id="no-copy-lines">甲中
Ab乙</textarea>
<input id="repeated" value="甲中乙中丙">
<input id="virtual-file" value="甲中🙂Ab乙">
<input id="switch-source" value="甲中🙂Ab乙">
<input id="switch-destination" value="other editor">
<div id="rich-direct" contenteditable="true"><p>甲<b>中🙂</b></p><p>Ab乙</p></div>
<div id="ia2-wrap" contenteditable="true" aria-label="IA2 wrap editor"><p>甲<strong>中🙂</strong></p><p><em>Ab</em>乙</p></div>
<div id="ia2-rollback" contenteditable="true" aria-label="IA2 rollback editor"><p>甲<strong>中🙂</strong></p><p><em>Ab</em>乙</p></div>
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
const locked = document.getElementById('locked');
locked.addEventListener('focus', () => {
    locked.select();
    document.title = 'PairTap readonly selected';
});
for (const event of ['copy', 'paste', 'input']) {
    locked.addEventListener(event, () => { document.title = 'PairTap readonly unexpectedly received ' + event; });
}
const rich = document.getElementById('rich');
rich.addEventListener('focus', () => {
    const text = rich.querySelector('span').firstChild;
    const range = document.createRange();
    range.setStart(text, 1);
    range.setEnd(text, 5);
    window.getSelection().removeAllRanges();
    window.getSelection().addRange(range);
    document.title = 'PairTap rich selection ready';
});
rich.addEventListener('input', () => {
    document.title = ['甲富《文本Ab》C乙', '甲富<文本Ab>C乙', '甲富＜文本Ab＞C乙'].includes(rich.textContent)
        ? 'PairTap rich selection passed'
        : 'PairTap rich selection mismatch: ' + rich.textContent;
});
const multi = document.getElementById('multi');
multi.addEventListener('focus', () => {
    const range = document.createRange();
    range.setStart(multi.firstChild.firstChild, 1);
    range.setEnd(multi.lastChild.firstChild, 2);
    window.getSelection().removeAllRanges();
    window.getSelection().addRange(range);
    document.title = 'PairTap multiline selection ready';
});
multi.addEventListener('input', () => {
    document.title = ['甲《文本Ab》C乙', '甲<文本Ab>C乙', '甲＜文本Ab＞C乙'].includes(multi.textContent)
        ? 'PairTap multiline selection passed'
        : 'PairTap multiline selection mismatch';
});
const differentCopy = document.getElementById('different-copy');
differentCopy.addEventListener('focus', () => {
    differentCopy.setSelectionRange(1, 6);
    document.title = 'different copy selected';
});
differentCopy.addEventListener('copy', event => {
    event.clipboardData.setData('text/plain', 'Concurrent in-app copy fixture');
    event.preventDefault();
    document.title = 'different copy published';
});
differentCopy.addEventListener('input', () => { document.title = 'different copy replaced editor text'; });
for (const id of ['no-copy', 'no-paste', 'locked-clipboard', 'max-length', 'virtual-file']) {
    const field = document.getElementById(id);
    field.addEventListener('focus', () => {
        field.setSelectionRange(1, 6);
        document.title = id + ' selected';
    });
    if (id !== 'locked-clipboard' && id !== 'virtual-file') {
        field.addEventListener(id === 'no-paste' ? 'paste' : 'copy', event => {
            event.preventDefault();
            document.title = id + ' blocked';
        });
    }
    field.addEventListener('input', () => {
        document.title = ['甲《中🙂Ab》乙', '甲<中🙂Ab>乙', '甲＜中🙂Ab＞乙'].includes(field.value)
            ? id + ' wrapped once'
            : id + ' pending input';
    });
}
const oneSlot = document.getElementById('one-slot');
oneSlot.addEventListener('focus', () => {
    oneSlot.setSelectionRange(1, 6);
    document.title = 'one slot selected';
});
oneSlot.addEventListener('copy', event => event.preventDefault());
let oneSlotEdits = 0;
oneSlot.addEventListener('input', () => {
    oneSlotEdits++;
    document.title = oneSlotEdits === 2 && oneSlot.value === '甲中🙂Ab乙'
        ? 'one slot rolled back' : 'one slot pending';
});
const noCopyLines = document.getElementById('no-copy-lines');
noCopyLines.addEventListener('focus', () => {
    noCopyLines.setSelectionRange(1, 5);
    document.title = 'no copy lines selected';
});
noCopyLines.addEventListener('copy', event => event.preventDefault());
noCopyLines.addEventListener('input', () => {
    document.title = ['甲《中\nAb》乙', '甲<中\nAb>乙', '甲＜中\nAb＞乙'].includes(noCopyLines.value)
        ? 'no copy lines wrapped' : 'no copy lines pending';
});
const repeated = document.getElementById('repeated');
repeated.addEventListener('focus', () => {
    repeated.setSelectionRange(3, 4);
    document.title = 'repeated selected';
});
repeated.addEventListener('input', () => {
    document.title = ['甲中乙《中》丙', '甲中乙<中>丙', '甲中乙＜中＞丙'].includes(repeated.value)
        ? 'repeated wrapped' : 'repeated wrong offset';
});
const switchSource = document.getElementById('switch-source');
const switchDestination = document.getElementById('switch-destination');
switchSource.addEventListener('focus', () => {
    switchSource.setSelectionRange(1, 6);
    document.title = 'switch source selected';
});
switchSource.addEventListener('copy', event => event.preventDefault());
switchSource.addEventListener('input', () => {
    switchDestination.focus();
    switchDestination.select();
    setTimeout(() => {
        if (document.activeElement !== switchDestination || switchDestination.value !== 'other editor') {
            document.title = 'switch destination changed';
        } else {
            document.title = switchSource.value === '甲中🙂Ab乙' ? 'switch source restored' : 'switch source still modified';
        }
    }, 900);
});
switchDestination.addEventListener('input', () => { document.title = 'switch destination modified'; });
const richDirect = document.getElementById('rich-direct');
richDirect.addEventListener('focus', () => {
    const range = document.createRange();
    range.setStart(richDirect.querySelector('b').firstChild, 0);
    range.setEnd(richDirect.lastChild.firstChild, 2);
    window.getSelection().removeAllRanges();
    window.getSelection().addRange(range);
    document.title = 'rich direct selected';
});
richDirect.addEventListener('copy', event => event.preventDefault());
richDirect.addEventListener('input', () => {
    const bold = richDirect.querySelector('b');
    document.title = bold && bold.textContent.includes('中🙂') &&
        ['甲《中🙂Ab》乙', '甲<中🙂Ab>乙', '甲＜中🙂Ab＞乙'].includes(richDirect.textContent)
        ? 'rich direct wrapped' : 'rich direct pending';
});
for (const operation of ['wrap', 'rollback']) {
    const editor = document.getElementById(`ia2-${operation}`);
    const original = editor.innerHTML;
    editor.addEventListener('focus', () => {
        const range = document.createRange();
        range.setStart(editor.querySelector('strong').firstChild, 0);
        range.setEnd(editor.querySelector('em').firstChild, 2);
        window.getSelection().removeAllRanges();
        window.getSelection().addRange(range);
        document.title = `IA2 ${operation} selected`;
    });
    editor.addEventListener('copy', event => event.preventDefault());
    editor.addEventListener('beforeinput', event => {
        if (operation === 'rollback' && event.data === '《') event.preventDefault();
    });
    editor.addEventListener('input', () => {
        const bold = editor.querySelector('strong');
        const italic = editor.querySelector('em');
        const wrapped = editor.textContent === '甲《中🙂Ab》乙' && editor.children.length === 2 &&
            bold && /^《?中🙂$/.test(bold.textContent) && italic && /^Ab》?$/.test(italic.textContent);
        const valid = operation === 'wrap' ? wrapped : editor.innerHTML === original;
        document.title = valid ? `IA2 ${operation} passed` : `IA2 ${operation} pending`;
    });
}
</script>
'@ | Set-Content -LiteralPath $page -Encoding UTF8
        $browserArguments = @(
            ('--user-data-dir="' + (Join-Path $fixture 'profile') + '"'),
            '--no-first-run', '--disable-sync', '--disable-background-networking',
            '--new-window', ('"' + ([Uri]$page).AbsoluteUri + '"')
        )
        if ($env:PAIRTAP_TEST_BROWSER_IA2) {
            $browserArguments = @('--disable-features=UiaProvider', '--force-renderer-accessibility=complete') + $browserArguments
        }
        function Send-BrowserKey([byte]$key, [switch]$Symbol) {
            $browser.Refresh()
            if ($browser.MainWindowHandle -eq [IntPtr]::Zero -or
                [TestKeys]::GetForegroundWindow() -ne $browser.MainWindowHandle) {
                throw 'The isolated Chrome fixture lost foreground focus.'
            }
            if ($Symbol) { [TestKeys]::Symbol($key) }
            else { [TestKeys]::Stroke($key) }
        }

        $browser = Start-Process -FilePath $chrome -PassThru -ArgumentList $browserArguments
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
            Send-BrowserKey 0xBC -Symbol
            Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'PairTap browser fixture passed*' } 1500) "browser selection wrapped (actual $($browser.MainWindowTitle))"
            $pasteTime = $watch.ElapsedMilliseconds
            # Query the clipboard only after the browser has actually applied the paste.
            if (!(Wait-Condition { [Windows.Forms.Clipboard]::GetText() -ceq 'Clipboard fixture' } 1300)) {
                $stamp = $currentStamp.GetValue($null)
                $read = $bufferType.GetMethod('ReadSelection', [Reflection.BindingFlags]'Static,NonPublic')
                $arguments = [object[]]@($stamp, $false, $stamp)
                $nativeMatch = $read.Invoke($null, $arguments) -ceq 'Clipboard fixture'
                throw "Browser clipboard did not recover in time (native Unicode matches fixture: $nativeMatch)."
            }
            Write-Output "TIMING browser paste $pasteTime ms, clipboard restored $($watch.ElapsedMilliseconds) ms from keypress"
            Assert ([Windows.Forms.Clipboard]::GetText() -ceq 'Clipboard fixture') 'browser operation restores clipboard text'
            Assert ([Windows.Forms.Clipboard]::GetText([Windows.Forms.TextDataFormat]::Rtf) -ceq '{\rtf1 Clipboard fixture}') 'browser operation restores clipboard RTF'
            if ($env:PAIRTAP_TEST_ADVERSARIAL) {
                Send-BrowserKey 0x23
                Pump 100
                Send-BrowserKey 0xBC -Symbol
                Pump 800
                $browser.Refresh()
                Assert ($browser.MainWindowTitle -like 'PairTap browser fixture passed*') 'empty browser selection does not trigger copy-line behavior'
                Assert ([Windows.Forms.Clipboard]::GetText() -ceq 'Clipboard fixture') 'empty browser selection never replaces the clipboard'

                Send-BrowserKey 0x09
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'PairTap readonly selected*' } 1000) 'browser read-only field has a real selection'
                $sequence = $sequenceMethod.Invoke($null, @())
                Send-BrowserKey 0xBC -Symbol
                Pump 950
                $browser.Refresh()
                Assert ($browser.MainWindowTitle -like 'PairTap readonly selected*') 'browser read-only selection receives no copy or paste'
                Assert ($sequenceMethod.Invoke($null, @()) -eq $sequence) 'browser read-only selection leaves the clipboard untouched'

                Send-BrowserKey 0x09
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'PairTap rich selection ready*' } 1000) 'browser rich editor selects a partial text node'
                Send-BrowserKey 0xBC -Symbol
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'PairTap rich selection passed*' } 2000) "browser rich editor wraps only the selected substring (actual $($browser.MainWindowTitle))"
                Assert (Wait-Condition { [Windows.Forms.Clipboard]::GetText() -ceq 'Clipboard fixture' } 1300) 'browser rich editor restores the clipboard'

                Send-BrowserKey 0x09
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'PairTap multiline selection ready*' } 1000) 'browser rich selection spans two paragraphs'
                Send-BrowserKey 0xBC -Symbol
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'PairTap multiline selection passed*' } 2000) "browser wraps across paragraphs (actual $($browser.MainWindowTitle))"
                Assert (Wait-Condition { [Windows.Forms.Clipboard]::GetText() -ceq 'Clipboard fixture' } 1300) 'browser multiline editor restores the clipboard'

                Send-BrowserKey 0x09
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'different copy selected*' } 1000) 'browser mismatch fixture selects text'
                $beforeMismatch = $bufferType.GetConstructors($flags)[0].Invoke(@($form.Handle))
                try {
                    $capture = $bufferType.GetMethod('Capture', $flags).Invoke($beforeMismatch, @())
                    Assert (Wait-Condition { $capture.IsCompleted } 1500) 'browser mismatch clipboard snapshot ready'
                    [void]$capture.GetAwaiter().GetResult()
                    Send-BrowserKey 0xBC -Symbol
                    Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'different copy published*' } 1300) 'the same browser publishes different clipboard data'
                    Pump 900
                    $browser.Refresh()
                    Assert ($browser.MainWindowTitle -like 'different copy published*') 'different clipboard data is never pasted into the selection'
                    Assert ([Windows.Forms.Clipboard]::GetText() -ceq 'Concurrent in-app copy fixture') 'different clipboard data from the same application is preserved'
                }
                finally {
                    $cleanup = $currentStamp.GetValue($null)
                    try {
                        if ([Windows.Forms.Clipboard]::GetText() -ceq 'Concurrent in-app copy fixture') {
                            $restore = $bufferType.GetMethod('RestoreIfCurrent', $flags, $null, @($stampType, [bool]), $null)
                            [void]$restore.Invoke($beforeMismatch, @($cleanup, $false))
                        }
                    }
                    finally { ([IDisposable]$beforeMismatch).Dispose() }
                }

                Send-BrowserKey 0x09
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'no-copy selected*' } 1000) 'copy-blocking editor has a verified selection'
                $sequence = $sequenceMethod.Invoke($null, @())
                Send-BrowserKey 0xBC -Symbol
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'no-copy wrapped once*' } 2600) "direct input wraps Chinese, emoji and Latin text (actual $($browser.MainWindowTitle))"
                Assert ($sequenceMethod.Invoke($null, @()) -eq $sequence) 'direct input leaves the clipboard untouched'

                Send-BrowserKey 0x09
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'no-paste selected*' } 1000) 'paste-blocking editor has a verified selection'
                Send-BrowserKey 0xBC -Symbol
                Pump 1300
                $browser.Refresh()
                Assert ($browser.MainWindowTitle -like 'no-paste blocked*') 'a paste attempt does not also send Unicode input'
                Send-BrowserKey 0xBC -Symbol
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'no-paste wrapped once*' } 2000) "the next shortcut types instead of pasting (actual $($browser.MainWindowTitle))"
                Pump 200
                $browser.Refresh()
                Assert ($browser.MainWindowTitle -like 'no-paste wrapped once*') 'paste fallback finishes with exactly one pair'
                Assert ([Windows.Forms.Clipboard]::GetText() -ceq 'Clipboard fixture') 'paste rejection and typing preserve the clipboard'

                Send-BrowserKey 0x09
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'locked-clipboard selected*' } 1000) 'another editor has a verified selection'
                $sequence = $sequenceMethod.Invoke($null, @())
                Assert ([TestKeys]::OpenClipboard($form.Handle)) 'fixture can keep the clipboard busy'
                try {
                    Send-BrowserKey 0xBC -Symbol
                    Assert (Wait-Condition {
                        $browser.Refresh()
                        $browser.MainWindowTitle -like 'locked-clipboard wrapped once*'
                    } 2200) 'direct input works when the clipboard cannot be opened'
                }
                finally { [void][TestKeys]::CloseClipboard() }
                Assert ($sequenceMethod.Invoke($null, @()) -eq $sequence) 'busy clipboard is left untouched'

                Send-BrowserKey 0x09
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'max-length selected*' } 1000) 'length-limited editor has a verified selection'
                Send-BrowserKey 0xBC -Symbol
                Pump 1600
                $browser.Refresh()
                Assert ($browser.MainWindowTitle -like 'max-length blocked*') 'a full field keeps the original text during direct input'

                Send-BrowserKey 0x09
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'one slot selected*' } 1000) 'field has room for exactly one delimiter'
                Send-BrowserKey 0xBC -Symbol
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'one slot rolled back*' } 3400) "a rejected left delimiter rolls back the exact right delimiter (actual $($browser.MainWindowTitle))"

                Send-BrowserKey 0x09
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'no copy lines selected*' } 1000) 'multiline field has a verified selection'
                Send-BrowserKey 0xBC -Symbol
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'no copy lines wrapped*' } 3000) "direct input preserves the selected line break (actual $($browser.MainWindowTitle))"

                Send-BrowserKey 0x09
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'repeated selected*' } 1000) 'second identical substring is selected'
                Send-BrowserKey 0xBC -Symbol
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'repeated wrapped*' } 2200) 'paste wraps only the selected occurrence'
                Assert (Wait-Condition { [Windows.Forms.Clipboard]::GetText() -ceq 'Clipboard fixture' } 1300) 'repeated selection restores the clipboard'

                Send-BrowserKey 0x09
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'virtual-file selected*' } 1000) 'virtual file test has a verified selection'
                $virtual = New-Object Windows.Forms.DataObject
                $virtual.SetData('FileContents', $false, 'Virtual clipboard fixture')
                [Windows.Forms.Clipboard]::SetDataObject($virtual, $true)
                $sequence = $sequenceMethod.Invoke($null, @())
                try {
                    Send-BrowserKey 0xBC -Symbol
                    Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'virtual-file wrapped once*' } 2200) 'virtual file data uses the clipboard-free path'
                    Assert ($sequenceMethod.Invoke($null, @()) -eq $sequence) 'virtual file clipboard sequence is preserved'
                }
                finally {
                    if ($sequenceMethod.Invoke($null, @()) -eq $sequence) {
                        [Windows.Forms.Clipboard]::SetDataObject($data, $true)
                    }
                }

                Send-BrowserKey 0x09
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'switch source selected*' } 1000) 'focus-switch fixture starts in the source editor'
                Send-BrowserKey 0xBC -Symbol
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'switch source restored*' } 3400) "focus switch restores the original field and preserves the destination and focus (actual $($browser.MainWindowTitle))"

                Send-BrowserKey 0x09
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'rich direct selected*' } 1000) 'copy-blocking rich editor selects across paragraphs'
                Send-BrowserKey 0xBC -Symbol
                Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like 'rich direct wrapped*' } 3000) "direct input preserves rich text formatting (actual $($browser.MainWindowTitle))"

                $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
                $ia2Fixture = Join-Path $fixture 'AccessibleWrap.exe'
                & "$framework\csc.exe" /nologo /warnaserror /codepage:65001 "/out:$ia2Fixture" (Join-Path $PSScriptRoot 'AccessibleWrap.cs')
                Assert ($LASTEXITCODE -eq 0) 'native IA2 regression fixture builds'
                foreach ($operation in 'wrap', 'rollback') {
                    Send-BrowserKey 0x09
                    Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like "IA2 $operation selected*" } 1400) "IA2 $operation selects text across both paragraphs"
                    & $ia2Fixture $env:PAIRTAP_TEST_EXE $browser.Id $operation
                    Assert ($LASTEXITCODE -eq 0) "native IA2 $operation leaves the clipboard intact"
                    Assert (Wait-Condition { $browser.Refresh(); $browser.MainWindowTitle -like "IA2 $operation passed*" } 1500) "native IA2 $operation preserves the rich markup"
                }
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
    try {
        $cleanup = $currentStamp.GetValue($null)
        $ownerField = $stampType.GetField('Owner', $flags)
        $ownRestore = $fixtureRestoreStamp -ne $null -and $cleanup.Equals($fixtureRestoreStamp) -and
            $ownerField.GetValue($cleanup) -eq $form.Handle
        if ($ownRestore -or [Windows.Forms.Clipboard]::GetText() -ceq 'Clipboard fixture') {
            $restore = $bufferType.GetMethod('RestoreIfCurrent', $flags, $null, @($stampType, [bool]), $null)
            [void]$restore.Invoke($backup, @($cleanup, $false))
        }
    }
    finally {
        ([IDisposable]$backup).Dispose()
        $form.Close()
        $form.Dispose()
    }
}
