# Called by Integration.ps1 with PAIRTAP_TEST_TRAY=1 and the test binary running.
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class TrayTest
{
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder value, int length);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder value, int length);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr first, IntPtr second);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(uint process);
    [DllImport("user32.dll")] private static extern bool GetMenuBarInfo(IntPtr window, int kind, int item, ref MenuInfo info);
    [DllImport("user32.dll")] private static extern bool GetMenuItemRect(IntPtr window, IntPtr menu, uint index, out Rect bounds);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MenuInfo { public uint Size; public Rect Bounds; public IntPtr Menu, Window; public int Flags; }
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref Identifier id, out Rect bounds);
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint operation, ref IconData data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int RegisterWindowMessage(string name);
    [StructLayout(LayoutKind.Sequential)] private struct Identifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct IconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=64)] public string Title;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }

    public static IntPtr Find(uint process, string caption, bool visible)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((window, parameter) =>
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != process || (visible && !IsWindowVisible(window))) return true;
            var name = new StringBuilder(128);
            if (caption == "#32768") GetClassName(window, name, name.Capacity);
            else GetWindowText(window, name, name.Capacity);
            if (name.ToString() != caption) return true;
            result = window;
            return false;
        }, IntPtr.Zero);
        return result;
    }

    public static bool Bounds(IntPtr window, out Rect rect)
    {
        var id = new Identifier { Size = (uint)Marshal.SizeOf(typeof(Identifier)), Window = window, Id = 1 };
        return Shell_NotifyIconGetRect(ref id, out rect) == 0;
    }

    public static void Notify(IntPtr window, int action) { PostMessage(window, 0x8001, IntPtr.Zero, (IntPtr)(0x10000 | action)); }
    public static void Close(IntPtr window) { PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); }
    public static void CancelMenu(IntPtr window) { PostMessage(window, 0x001f, IntPtr.Zero, IntPtr.Zero); }
    public static bool RemoveIcon(IntPtr window)
    {
        var data = new IconData { Size = (uint)Marshal.SizeOf(typeof(IconData)), Window = window, Id = 1 };
        return Shell_NotifyIcon(2, ref data);
    }
    public static void TaskbarCreated(IntPtr window)
    {
        PostMessage(window, RegisterWindowMessage("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero);
    }
    public static bool MenuItem(IntPtr window, uint index, out Rect bounds)
    {
        bounds = new Rect();
        var info = new MenuInfo { Size = (uint)Marshal.SizeOf(typeof(MenuInfo)) };
        return GetMenuBarInfo(window, -4, 0, ref info) && GetMenuItemRect(IntPtr.Zero, info.Menu, index, out bounds);
    }
    public static bool Hit(IntPtr window, int x, int y)
    {
        return WindowFromPoint(new Point { X = x, Y = y }) == window;
    }
}
'@

$app = Get-Process PairTap | Where-Object Path -eq $env:PAIRTAP_TEST_EXE | Select-Object -First 1
Assert ($null -ne $app) 'tray test binary is running'
$owner = [TrayTest]::Find($app.Id, 'PairTap Notification Area', $false)
Assert ($owner -ne [IntPtr]::Zero) 'tray callback window exists'
$bounds = [TrayTest+Rect]::new()
Assert ([TrayTest]::Bounds($owner, [ref]$bounds)) "Explorer registered the tray icon ($($bounds.Left), $($bounds.Top))"
$panelName = '对符输入 · PairTap'
$settingsName = '对符输入 · 快捷键设置'
function Find-TrayWindow($caption) { [TrayTest]::Find($app.Id, $caption, $true) }
function Close-TrayWindow($caption) {
    $window = Find-TrayWindow $caption
    if ($window -ne [IntPtr]::Zero) { [TrayTest]::Close($window) }
    if (!(Wait-Condition { (Find-TrayWindow $caption) -eq [IntPtr]::Zero } 1200)) {
        throw "The window did not close: $caption"
    }
}

Close-TrayWindow $panelName
Close-TrayWindow $settingsName
$fixture = New-Object Windows.Forms.Form
$fixture.Text = 'PairTap tray foreground fixture'
$fixture.TopMost = $true
$fixture.Size = [Drawing.Size]::new(340, 120)
$fixture.StartPosition = [Windows.Forms.FormStartPosition]::Manual
$fixture.Location = [Drawing.Point]::new(160, 160)
$editor = New-Object Windows.Forms.TextBox
$editor.Dock = 'Fill'
$fixture.Controls.Add($editor)

function Invoke-TrayMenuItem([uint32]$index) {
    [TestKeys]::Focus($fixture.Handle)
    Assert (Wait-Condition { [TestKeys]::GetForegroundWindow() -eq $fixture.Handle } 1200) 'tray foreground fixture is active'
    [TestKeys]::Stroke(0x10)
    Pump 60
    Assert ([TrayTest]::AllowSetForegroundWindow($app.Id)) 'the fixture grants the simulated shell foreground permission'
    [TrayTest]::Notify($owner, 0x007b)
    Assert (Wait-Condition { (Find-TrayWindow '#32768') -ne [IntPtr]::Zero } 1400) 'native menu is visible for command navigation'
    Assert ([TestKeys]::Owner([TestKeys]::GetForegroundWindow()) -eq $app.Id) 'tray menu owns foreground before navigation'
    $popup = Find-TrayWindow '#32768'
    $item = [TrayTest+Rect]::new()
    Assert ([TrayTest]::MenuItem($popup, $index, [ref]$item)) 'menu item has a native hit target'
    $x = [int](($item.Left + $item.Right) / 2)
    $y = [int](($item.Top + $item.Bottom) / 2)
    Assert ([TrayTest]::Hit($popup, $x, $y)) 'menu click lands inside the popup'
    [TestKeys]::Click($x, $y)
}

try {
    [TrayTest]::Notify($owner, 0x0202)
    Assert (Wait-Condition { (Find-TrayWindow $panelName) -ne [IntPtr]::Zero } 1400) 'single click opens the palette'
    $first = Find-TrayWindow $panelName
    [TrayTest]::Notify($owner, 0x0202)
    [TrayTest]::Notify($owner, 0x0400)
    Pump 180
    Assert ((Find-TrayWindow $panelName) -eq $first) 'rapid click and shell select reuse the same palette'
    Close-TrayWindow $panelName

    [TrayTest]::Notify($owner, 0x0401)
    Assert (Wait-Condition { (Find-TrayWindow $panelName) -ne [IntPtr]::Zero } 1400) 'keyboard tray activation opens the palette'
    Close-TrayWindow $panelName

    for ($i = 0; $i -lt 30; $i++) {
        [TrayTest]::Notify($owner, 0x007b)
        if (!(Wait-Condition { (Find-TrayWindow '#32768') -ne [IntPtr]::Zero } 1400)) {
            throw "Tray menu did not open on cycle $i"
        }
        [TrayTest]::CancelMenu($owner)
        if (!(Wait-Condition { (Find-TrayWindow '#32768') -eq [IntPtr]::Zero } 1400)) {
            throw "Tray menu did not dismiss on cycle $i"
        }
    }
    Assert $true '30 repeated native menu opens and dismissals'

    $fixture.Show()
    Invoke-TrayMenuItem 2
    Assert (Wait-Condition { (Find-TrayWindow $settingsName) -ne [IntPtr]::Zero } 1400) 'menu command opens shortcut settings'
    $settings = Find-TrayWindow $settingsName
    [TrayTest]::Notify($owner, 0x0202)
    Pump 170
    Assert ((Find-TrayWindow $settingsName) -eq $settings -and (Find-TrayWindow $panelName) -eq [IntPtr]::Zero) 'single click reuses open settings'
    Close-TrayWindow $settingsName

    [TestKeys]::Focus($fixture.Handle)
    [void]$editor.Focus()
    Assert ([TestKeys]::GetForegroundWindow() -eq $fixture.Handle) 'fixture is active for the deferred request'
    [TestKeys]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
    [TestKeys]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    try {
        [TestKeys]::Stroke(0x75)
        Pump 120
        [TrayTest]::Notify($owner, 0x0202)
        Pump 120
        Assert ((Find-TrayWindow $panelName) -eq [IntPtr]::Zero) 'tray click waits while modifiers are held'
    }
    finally {
        [TestKeys]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
        [TestKeys]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)
    }
    Assert (Wait-Condition { (Find-TrayWindow $panelName) -ne [IntPtr]::Zero } 1400) 'queued tray click opens after the busy operation'
    $passive = Find-TrayWindow $panelName
    [TestKeys]::Focus($fixture.Handle)
    [void]$editor.Focus()
    Assert ([TestKeys]::GetForegroundWindow() -eq $fixture.Handle) 'fixture is active while the browse palette is open'
    [TestKeys]::Hotkey(0x75)
    Assert (Wait-Condition { $active = Find-TrayWindow $panelName; $active -ne [IntPtr]::Zero -and $active -ne $passive } 1400) 'shortcut replaces the background browse palette with a selection palette'
    Close-TrayWindow $panelName

    $duplicate = Start-Process -FilePath $env:PAIRTAP_TEST_EXE -WindowStyle Hidden -PassThru
    try {
        Assert ($duplicate.WaitForExit(3000)) 'relaunch leaves one resident process'
        Assert (Wait-Condition { (Find-TrayWindow $panelName) -ne [IntPtr]::Zero } 1400) 'relaunch opens the existing resident palette'
    }
    finally { $duplicate.Dispose() }

    Assert ([TrayTest]::RemoveIcon($owner)) 'simulate Explorer discarding the icon'
    Assert (![TrayTest]::Bounds($owner, [ref]$bounds)) 'the icon is absent before the taskbar notification'
    [TrayTest]::TaskbarCreated($owner)
    Assert (Wait-Condition { $rect = [TrayTest+Rect]::new(); [TrayTest]::Bounds($owner, [ref]$rect) } 1400) 'taskbar recreation registers the icon again'

    Close-TrayWindow $panelName
    Invoke-TrayMenuItem 4
    Assert ($app.WaitForExit(4000)) 'tray exit closes the resident process and menu'
}
finally {
    $fixture.Dispose()
    if ($app.HasExited) { Start-Process -FilePath $env:PAIRTAP_TEST_EXE -WindowStyle Hidden }
    else {
        [TrayTest]::CancelMenu($owner)
        if (![TrayTest]::Bounds($owner, [ref]$bounds)) { [TrayTest]::TaskbarCreated($owner) }
        Close-TrayWindow $panelName
        Close-TrayWindow $settingsName
    }
}
