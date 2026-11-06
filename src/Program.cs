using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle(PairTap.Product.DisplayName)]
[assembly: AssemblyProduct(PairTap.Product.Name)]
[assembly: AssemblyVersion(PairTap.Product.Version)]
[assembly: AssemblyFileVersion(PairTap.Product.Version)]

namespace PairTap
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            bool first;
            using (var instance = new Mutex(true, Product.InstanceName, out first))
            {
                if (!first) return;
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var context = new TrayApp()) Application.Run(context);
            }
        }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly HotkeyWindow hotkeys;
        private readonly ClipboardChannel clipboard;
        private readonly HashSet<int> available = new HashSet<int>();
        private readonly Icon icon, trayIcon;
        private readonly EventWaitHandle exitEvent;
        private readonly System.Windows.Forms.Timer exitTimer;
        private Palette palette;
        private ShortcutDialog shortcutDialog;
        private Keys[] bindings;
        private PunctuationPreference preference;
        private IntPtr keyboardLayout;
        private bool busy, exitRequested;

        internal TrayApp()
        {
            Diagnostics.Write("startup version=" + Product.Version);
            hotkeys = new HotkeyWindow(HandleHotkey);
            clipboard = new ClipboardChannel();
            using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("brand"))
            {
                icon = new Icon(source, 48, 48);
                source.Position = 0;
                trayIcon = new Icon(source, SystemInformation.SmallIconSize);
            }
            menu = new ContextMenuStrip();
            menu.Items.Add("先选中文字，再按快捷键").Enabled = false;
            tray = new NotifyIcon {
                Text = Product.DisplayName,
                Icon = trayIcon,
                ContextMenuStrip = menu,
                Visible = true
            };
            tray.DoubleClick += (sender, args) => ShowPalette(null);

            bindings = ShortcutSettings.Load();
            preference = ShortcutSettings.Preference();
            keyboardLayout = Native.KeyboardLayout(Native.GetForegroundWindow());
            RegisterShortcuts(bindings);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("快捷键设置", null, (sender, args) => ShowShortcutSettings());
            menu.Items.Add("查看符号面板", null, (sender, args) => ShowPalette(null));
            menu.Items.Add("退出", null, (sender, args) => RequestExit());

            exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Product.ExitEventName);
            exitTimer = new System.Windows.Forms.Timer { Interval = 200 };
            exitTimer.Tick += (sender, args) =>
            {
                if (exitEvent.WaitOne(0)) RequestExit();
                else UpdateKeyboardLayout();
            };
            exitTimer.Start();

            NotifyUnavailable();
        }

        private void UpdateKeyboardLayout()
        {
            if (busy || palette != null || shortcutDialog != null) return;
            IntPtr current = Native.KeyboardLayout(Native.GetForegroundWindow());
            if (current == keyboardLayout) return;
            keyboardLayout = current;
            UnregisterShortcuts();
            bindings = ShortcutSettings.Load();
            RegisterShortcuts(bindings);
            NotifyUnavailable();
        }

        private void RegisterShortcuts(Keys[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                Keys binding = keys[i];
                if (binding == Keys.None) continue;
                if (Native.RegisterHotKey(hotkeys.Handle, i + 1, ShortcutSettings.NativeModifiers(binding),
                    (uint)(binding & Keys.KeyCode))) available.Add(i + 1);
                else Diagnostics.Write("hotkey_registration_failed id=" + Shortcut.All[i].Id);
            }
        }

        private void UnregisterShortcuts()
        {
            foreach (int id in available) Native.UnregisterHotKey(hotkeys.Handle, id);
            available.Clear();
        }

        private void NotifyUnavailable()
        {
            int expected = 0;
            foreach (Keys binding in bindings)
                if (binding != Keys.None) expected++;
            if (expected != available.Count)
                Notify("部分全局快捷键无法注册。可在托盘菜单中打开快捷键设置。");
        }

        private void ShowShortcutSettings()
        {
            if (busy || exitRequested) return;
            if (shortcutDialog != null)
            {
                shortcutDialog.Activate();
                return;
            }
            Palette previous = palette;
            UnregisterShortcuts();
            var dialog = new ShortcutDialog(bindings, preference, icon, SaveShortcuts);
            shortcutDialog = dialog;
            dialog.FormClosed += (sender, args) =>
            {
                shortcutDialog = null;
                if (!dialog.Committed && !exitRequested)
                {
                    RegisterShortcuts(bindings);
                    NotifyUnavailable();
                }
            };
            dialog.Show();
            dialog.Activate();
            if (previous != null && !previous.IsDisposed) previous.Close();
        }

        private string SaveShortcuts(Keys[] candidate, PunctuationPreference style)
        {
            RegisterShortcuts(candidate);
            for (int i = 0; i < candidate.Length; i++)
            {
                if (candidate[i] == Keys.None || available.Contains(i + 1)) continue;
                UnregisterShortcuts();
                return "无法注册 " + ShortcutSettings.Describe(candidate[i]) + "（" +
                    Shortcut.All[i].Name + "）。请改用其他组合键。";
            }

            try
            {
                ShortcutSettings.Save(candidate, style);
                bindings = (Keys[])candidate.Clone();
                preference = style;
                return null;
            }
            catch (System.Security.SecurityException) { }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
            UnregisterShortcuts();
            return "无法保存快捷键。请确认当前 Windows 用户可以保存应用设置。";
        }

        private async void HandleHotkey(int id, uint chord)
        {
            if (busy || exitRequested || shortcutDialog != null || !available.Contains(id)) return;
            Keys expected = bindings[id - 1];
            uint registeredChord = ((uint)(expected & Keys.KeyCode) << 16) |
                (ShortcutSettings.NativeModifiers(expected) & 7);
            if (registeredChord != chord) return;
            if (palette != null)
            {
                if (id == 1) palette.Close();
                return;
            }
            var target = new Target();
            if (!target.IsCurrent) return;
            if (id == 1)
            {
                busy = true;
                for (int i = 0; Native.ModifiersDown && i < 60; i++) await Task.Delay(25);
                busy = false;
                if (exitRequested) ExitThread();
                else if (!Native.ModifiersDown && target.IsCurrent) ShowPalette(target);
            }
            else
            {
                Shortcut shortcut = Shortcut.All[id - 1];
                Pair pair = shortcut.Pair;
                if (pair == null)
                {
                    IntPtr layout = Native.KeyboardLayout(target.Focus);
                    Keys binding = bindings[id - 1];
                    if (binding == shortcut.Preset(keyboardLayout) && Native.ReadPunctuationKey(binding, layout) != shortcut.OriginalKey)
                    {
                        Notify("当前键盘布局或大小写状态下，该按键不对应这个标点。");
                        return;
                    }
                    pair = shortcut.Resolve(CurrentShape(target, layout));
                }
                Apply(target, pair);
            }
        }

        private PunctuationShape CurrentShape(Target target, IntPtr layout)
        {
            switch (preference)
            {
                case PunctuationPreference.Chinese: return PunctuationShape.Chinese;
                case PunctuationPreference.Half: return PunctuationShape.Half;
                case PunctuationPreference.Full: return PunctuationShape.Full;
                default: return Native.InputShape(target.Focus, layout);
            }
        }

        private void ShowPalette(Target target)
        {
            if (busy || exitRequested) return;
            if (shortcutDialog != null)
            {
                shortcutDialog.Activate();
                return;
            }
            if (palette != null)
            {
                palette.Activate();
                return;
            }
            PunctuationShape? shape = target == null ? (PunctuationShape?)null :
                CurrentShape(target, Native.KeyboardLayout(target.Focus));
            palette = new Palette(bindings, shape, available.Contains(1), icon);
            palette.Configure += ShowShortcutSettings;
            palette.Chosen += pair =>
            {
                palette.Hide();
                Native.SetForegroundWindow(target.Window);
                palette.Close();
                Apply(target, pair, true);
            };
            palette.FormClosed += (sender, args) => palette = null;
            palette.Show();
            if (palette == null) return;
            if (target == null) palette.Activate();
            else if (!Native.ActivatePalette(palette.Handle, target))
            {
                palette.Close();
                Notify("原输入窗口焦点已变化，请重新选中文字。");
            }
        }

        private async void Apply(Target target, Pair pair, bool returningFromPalette = false)
        {
            busy = true;
            var elapsed = Stopwatch.StartNew();
            string control = Native.ClassName(target.Focus);
            string context = "wrap pid=" + target.ProcessId + " control=" + control + " pair=" + pair.Name;
            try
            {
                if (returningFromPalette)
                {
                    for (int i = 0; i < 12 && !target.IsCurrent; i++)
                        await Task.Delay(20);
                }
                string message = await Wrap.Apply(target, pair, clipboard);
                Diagnostics.Write(context + " ms=" + elapsed.ElapsedMilliseconds + " result=" + (message ?? "ok"));
                if (message != null && message != Wrap.NoSelection) Notify(message);
            }
            catch (InvalidOperationException error) { Diagnostics.Failure(context, error); Notify(error.Message); }
            catch (ExternalException error) { Diagnostics.Failure(context, error); Notify("当前窗口或剪贴板暂时不可用，请重新选中后重试。"); }
            catch (Exception error) { Diagnostics.Failure(context, error); Notify("这个选区暂时无法处理。请选中普通文字后重试。"); }
            finally
            {
                busy = false;
                if (exitRequested) ExitThread();
            }
        }

        private void RequestExit()
        {
            exitRequested = true;
            if (!busy) ExitThread();
        }

        private void Notify(string text) { tray.ShowBalloonTip(3500, Product.ChineseName, text, ToolTipIcon.Info); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                exitTimer.Dispose();
                exitEvent.Dispose();
                if (palette != null) palette.Close();
                tray.Visible = false;
                tray.Dispose();
                menu.Dispose();
                if (shortcutDialog != null) shortcutDialog.Close();
                UnregisterShortcuts();
                clipboard.Dispose();
                hotkeys.DestroyHandle();
                icon.Dispose();
                trayIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class HotkeyWindow : NativeWindow
        {
            private readonly Action<int, uint> handle;
            internal HotkeyWindow(Action<int, uint> handle)
            {
                this.handle = handle;
                CreateHandle(new CreateParams { Caption = Product.Name + " Hotkeys" });
            }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0312) handle(message.WParam.ToInt32(), unchecked((uint)message.LParam.ToInt64()));
                base.WndProc(ref message);
            }
        }
    }
}
