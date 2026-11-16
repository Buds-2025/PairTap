# Called by Integration.ps1 with a native test editor and an isolated shortcut value.
function Assert-CustomHotkey($message) {
    $edit.Text = '新版功能'
    for ($attempt = 0; $attempt -lt 3 -and $edit.Text -ceq '新版功能'; $attempt++) {
        [void][TestKeys]::FocusFixture($form.Handle)
        [void]$edit.Focus()
        $edit.SelectAll()
        Pump 100
        if ([TestKeys]::GetForegroundWindow() -eq $form.Handle) { [TestKeys]::Hotkey(0x79) }
        if (Wait-Condition { $edit.Text -cne '新版功能' } 650) { break }
    }
    Assert ($edit.Text -ceq '〔新版功能〕') "$message (actual $($edit.Text))"
}

if ($env:PAIRTAP_TEST_SETTINGS -eq 'restart') {
    Assert-CustomHotkey 'custom hotkey still wraps the selection after restart'
    return
}

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
$window = Open-FixturePalette
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
Assert ($null -ne (FindUI $settings '选择一项，在录入框按下组合键。清除可禁用，保存后生效。')) 'settings describes editing, disabling and saving a shortcut'
Assert ($null -ne (FindUI $settings '组合键须包含 Ctrl 或 Alt，可另加 Shift。')) 'settings explains the allowed modifiers'
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
Assert-CustomHotkey 'custom hotkey immediately wraps the selection'
$stored = (Get-ItemProperty -LiteralPath 'HKCU:\Software\PairTap' -Name Shortcuts).Shortcuts
Assert ($stored -contains 'tortoise=393337') 'custom hotkey is persisted'
