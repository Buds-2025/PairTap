param([string]$Snapshots)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$temp = Join-Path $env:TEMP ('PairTap-layout-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$fixture = Join-Path $temp 'DpiLayout.cs'
$exe = Join-Path $temp 'DpiLayout.exe'
@'
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PairTap;

internal static class DpiLayout
{
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    private static T Field<T>(object instance, string name)
    {
        return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
    }

    private static void Fits(Control child)
    {
        Check(child.Width > 0 && child.Height > 0 && child.Parent.ClientRectangle.Contains(child.Bounds),
            child.Text + " falls outside " + child.Parent.GetType().Name + " " + child.Bounds + " / " + child.Parent.ClientRectangle);
        if (child is Label || child is Button)
        {
            Size wanted = child.GetPreferredSize(new Size(child.Width, 0));
            Check(child.Height >= wanted.Height && child.Width >= wanted.Width,
                child.Text + " is clipped " + child.Size + " / " + wanted);
        }
    }

    private static void PaletteFits(Palette palette)
    {
        foreach (string name in new[] { "title", "hint", "footerHint", "settings", "viewport" })
            Fits(Field<Control>(palette, name));
        Control settings = Field<Control>(palette, "settings");
        Size settingsSize = settings.Size;
        Size windowSize = palette.Size;
        palette.Width -= 1;
        palette.Size = windowSize;
        Check(settings.Size == settingsSize, "Settings button grows when the palette is resized");
        var tiles = Field<Control[]>(palette, "tiles");
        var scrollbar = Field<VScrollBar>(palette, "scrollbar");
        if (scrollbar.Visible) scrollbar.Value = 0;
        using (Graphics graphics = palette.CreateGraphics())
        {
            foreach (Control tile in tiles)
            {
                Check(tile.Left >= 0 && tile.Right <= tile.Parent.Width, "Card falls outside the grid");
                if (!(tile is Button)) continue;
                int requiredWidth = (int)tile.GetType().GetMethod("MeasureContent", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(tile, new object[] { graphics });
                int content = (int)tile.GetType().GetMethod("ContentHeight", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(tile, new object[] { tile.Width });
                Check(tile.Width >= requiredWidth && tile.Height > content, "Card text does not fit: " + tile.Text);
            }
        }
        if (scrollbar.Visible)
        {
            scrollbar.Value = scrollbar.Maximum - scrollbar.LargeChange + 1;
            Control last = tiles[tiles.Length - 1];
            Check(last.Bottom > 0 && last.Bottom <= last.Parent.Height, "End of the grid cannot be reached");
            if (last.Height <= last.Parent.Height)
            {
                scrollbar.Value = Math.Max(0, scrollbar.Value + Math.Min(0, last.Top));
                Fits(last);
            }
            scrollbar.Value = 0;
        }
        else Fits(tiles[tiles.Length - 1]);
    }

    private static void SettingsFit(ShortcutDialog settings)
    {
        foreach (string name in new[] { "instructions", "styleLabel", "shape", "list", "capture", "clear", "status", "save" })
            Fits(Field<Control>(settings, name));
        Fits((Button)settings.CancelButton);
        var list = Field<ListView>(settings, "list");
        int columnWidth = 0;
        foreach (ColumnHeader column in list.Columns) columnWidth += column.Width;
        Check(columnWidth <= list.Width && list.Height > 2 * list.Font.Height,
            "Settings table does not fit: columns " + columnWidth + " in " + list.ClientSize + " at " + list.Font.Height);
        Check(list.Items[0].Bounds.Height >= list.Font.Height, "Settings row clips its font");
    }

    private static void Save(Form form, string directory, string name)
    {
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(directory, name + ".png"));
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().FullName);
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine(error.StackTrace);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string snapshots = args.Length == 0 ? null : args[0];
        int[] dpis = { 96, 120, 144, 168, 192, 240, 288, 336, 384, 480 };
        Size[] sizes = { new Size(640, 440), new Size(1024, 560), new Size(1366, 728),
            new Size(1920, 1040), new Size(900, 1550), new Size(3840, 2080) };
        using (var icon = new Icon(typeof(Palette).Assembly.GetManifestResourceStream("brand")))
        foreach (Size size in sizes)
        foreach (int dpi in dpis)
        {
            var area = new Rectangle(0, 0, size.Width, size.Height);
            Keys[] bindings = ShortcutSettings.Defaults();
            bindings[2] = Keys.Control | Keys.Alt | Keys.Shift | Keys.OemBackslash;
            bool snapshot = (size.Width == 1366 && dpi == 144) || (size.Width == 640 && dpi == 336) ||
                (size.Width == 3840 && dpi == 288);
            using (var palette = new Palette(bindings, PunctuationShape.Chinese, true, icon))
            {
                palette.OpenOn(Screen.PrimaryScreen);
                palette.Show();
                Check(AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(palette.Handle), (IntPtr)(-4)),
                    "PairTap manifest does not establish per-monitor DPI v2");
                palette.ApplyDisplay(area, dpi, true);
                Check(area.Contains(palette.Bounds), "Palette outside work area");
                PaletteFits(palette);
                if (snapshot) Save(palette, snapshots, "palette-" + size.Width + "-" + dpi);
                palette.Size = palette.MinimumSize;
                PaletteFits(palette);
                palette.ApplyDisplay(area, dpi == 96 ? 192 : 96, false);
                Check(area.Contains(palette.Bounds), "Palette outside work area after a DPI change");
                PaletteFits(palette);
            }
            using (var settings = new ShortcutDialog(bindings, PunctuationPreference.Auto, icon, (keys, style) => null))
            {
                settings.OpenOn(Screen.PrimaryScreen);
                settings.Show();
                Field<Label>(settings, "status").Text = "无法注册 Ctrl+Alt+Shift+Backslash（半角直双引号）。请改用其他组合键。";
                settings.ApplyDisplay(area, dpi, true);
                Check(area.Contains(settings.Bounds), "Settings outside work area");
                SettingsFit(settings);
                if (snapshot) Save(settings, snapshots, "settings-" + size.Width + "-" + dpi);
                settings.Size = settings.MinimumSize;
                SettingsFit(settings);
                settings.ApplyDisplay(area, dpi == 96 ? 192 : 96, false);
                Check(area.Contains(settings.Bounds), "Settings outside work area after a DPI change");
                SettingsFit(settings);
            }
            Console.WriteLine("PASS work area {0}x{1}, {2}% DPI, resize and DPI change", size.Width, size.Height, dpi * 100 / 96);
        }
        return 0;
    }
}
'@ | Set-Content -LiteralPath $fixture -Encoding utf8
try {
    $source = Get-ChildItem -Path (Join-Path $root 'src\*.cs') | ForEach-Object FullName
    $manifest = Join-Path $root 'assets\PairTap.manifest'
    $icon = Join-Path $root 'assets\PairTap.ico'
    $qr = Join-Path $root 'assets\bilibili-qr.png'
    & "$framework\csc.exe" /nologo /warnaserror /codepage:65001 /main:DpiLayout "/out:$exe" "/win32manifest:$manifest" "/resource:$icon,brand" "/resource:$qr,bilibili-qr" "/r:$framework\Accessibility.dll" "/r:$framework\WPF\UIAutomationClient.dll" "/r:$framework\WPF\UIAutomationTypes.dll" "/r:$framework\WPF\WindowsBase.dll" $source $fixture
    if ($LASTEXITCODE) { throw 'DPI fixture could not be compiled.' }
    & $exe $Snapshots
    if ($LASTEXITCODE) { throw 'DPI layout checks failed.' }
}
finally {
    Remove-Item -LiteralPath $fixture, $exe -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temp -ErrorAction SilentlyContinue
}
