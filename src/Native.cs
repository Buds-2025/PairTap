using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PairTap
{
    internal static class Native
    {
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll")] private static extern short GetKeyState(int key);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern short VkKeyScanEx(char value, IntPtr layout);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(
            uint key, uint scan, byte[] state, StringBuilder output, int count, uint flags, IntPtr layout);
        [DllImport("user32.dll")] private static extern uint MapVirtualKeyEx(uint key, uint type, IntPtr layout);
        [DllImport("imm32.dll")] private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder name, int length);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr h, uint message,
            IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetTextMessage(IntPtr h, uint message, IntPtr wParam,
            StringBuilder text, uint flags, uint timeout, out UIntPtr result);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
        private static extern IntPtr SetTextMessage(IntPtr h, uint message, IntPtr wParam,
            string text, uint flags, uint timeout, out UIntPtr result);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
        private static extern IntPtr GetSelectionMessage(IntPtr h, uint message, out uint start, out uint end,
            uint flags, uint timeout, out UIntPtr result);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowStyle(IntPtr h, int index);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
        [StructLayout(LayoutKind.Sequential)]
        private struct GuiInfo
        {
            public int Size, Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Input { public uint Type; public InputData Data; }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputData
        {
            [FieldOffset(0)] public Keyboard Keyboard;
            [FieldOffset(0)] public Mouse Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Keyboard
        {
            public ushort Key, Scan;
            public uint Flags, Time;
            public IntPtr Extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Mouse
        {
            public int X, Y;
            public uint Data, Flags, Time;
            public IntPtr Extra;
        }

        internal static IntPtr Focus(IntPtr window)
        {
            uint pid;
            uint thread = GetWindowThreadProcessId(window, out pid);
            var info = new GuiInfo { Size = Marshal.SizeOf(typeof(GuiInfo)) };
            const int menuOrMove = 0x02 | 0x04 | 0x08 | 0x10;
            return thread != 0 && GetGUIThreadInfo(thread, ref info) && (info.Flags & menuOrMove) == 0
                ? info.Focus : IntPtr.Zero;
        }

        internal static uint ProcessId(IntPtr window)
        {
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            return pid;
        }

        internal static IntPtr KeyboardLayout(IntPtr window)
        {
            uint ignored;
            uint thread = GetWindowThreadProcessId(window, out ignored);
            return GetKeyboardLayout(thread);
        }

        internal static Keys PunctuationHotkey(char symbol, IntPtr layout)
        {
            short translated = VkKeyScanEx(symbol, layout);
            if (translated == -1 || (translated >> 8 & ~1) != 0) return Keys.None;
            Keys key = Keys.Control | (Keys)(translated & 0xff);
            return (translated & 0x100) == 0 ? key : key | Keys.Shift;
        }

        internal static char ReadPunctuationKey(Keys shortcut, IntPtr layout)
        {
            uint key = (uint)(shortcut & Keys.KeyCode);
            var state = new byte[256];
            state[(int)Keys.CapsLock] = (byte)(GetKeyState((int)Keys.CapsLock) & 1);
            if ((shortcut & Keys.Shift) != 0) state[(int)Keys.ShiftKey] = 0x80;
            var text = new StringBuilder(8);
            // Do not change the active keyboard layout's dead-key state.
            return ToUnicodeEx(key, MapVirtualKeyEx(key, 0, layout), state, text, text.Capacity, 4, layout) == 1
                ? text[0] : '\0';
        }

        internal static PunctuationShape InputShape(IntPtr focus, IntPtr layout)
        {
            IntPtr ime = ImmGetDefaultIMEWnd(focus);
            UIntPtr open, mode;
            if (ime == IntPtr.Zero ||
                SendMessageTimeout(ime, 0x0283, (IntPtr)5, IntPtr.Zero, 2, 70, out open) == IntPtr.Zero ||
                SendMessageTimeout(ime, 0x0283, (IntPtr)1, IntPtr.Zero, 2, 70, out mode) == IntPtr.Zero)
                return PunctuationShape.Half;
            ulong flags = mode.ToUInt64();
            bool chinese = (layout.ToInt64() & 0x03ff) == 0x04;
            if (chinese && open.ToUInt64() != 0 && (flags & (1 | 0x0400)) != 0)
                return PunctuationShape.Chinese;
            if ((flags & 8) != 0) return PunctuationShape.Full;
            return PunctuationShape.Half;
        }

        internal static bool ActivatePalette(IntPtr palette, Target source)
        {
            if (GetForegroundWindow() == palette) return true;
            if (!source.IsCurrent) return false;
            if (SetForegroundWindow(palette)) return true;

            uint ignored;
            uint targetThread = GetWindowThreadProcessId(source.Window, out ignored);
            uint current = GetCurrentThreadId();
            if (current == targetThread || !AttachThreadInput(current, targetThread, true)) return false;
            try { return GetForegroundWindow() == source.Window && SetForegroundWindow(palette); }
            finally { AttachThreadInput(current, targetThread, false); }
        }

        internal static string ClassName(IntPtr window)
        {
            var name = new StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            return name.ToString();
        }

        internal static bool IsPlainEdit(string className)
        {
            return className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
                className.StartsWith("WindowsForms10.EDIT.", StringComparison.OrdinalIgnoreCase);
        }

        internal static ulong? EditSelection(IntPtr window)
        {
            UIntPtr result;
            uint start, end;
            if (GetSelectionMessage(window, 0x00B0, out start, out end, 0x22, 150, out result) == IntPtr.Zero ||
                start > end || end > int.MaxValue) return null;
            return ((ulong)end << 32) | start;
        }

        internal static bool CanEdit(IntPtr window)
        {
            const int readOnlyOrPassword = 0x800 | 0x20;
            UIntPtr passwordChar;
            return IsWindowEnabled(window) && (GetWindowStyle(window, -16) & readOnlyOrPassword) == 0 &&
                SendMessageTimeout(window, 0x00D2, IntPtr.Zero, IntPtr.Zero, 0x22, 100, out passwordChar) != IntPtr.Zero &&
                passwordChar == UIntPtr.Zero;
        }

        internal static bool EditAllowsLength(IntPtr window, int length)
        {
            UIntPtr limit;
            return SendMessageTimeout(window, 0x00D5, IntPtr.Zero, IntPtr.Zero, 0x22, 150, out limit) != IntPtr.Zero &&
                limit.ToUInt64() >= (uint)length;
        }

        internal static string ReadEditText(IntPtr window)
        {
            UIntPtr length;
            if (SendMessageTimeout(window, 0x000E, IntPtr.Zero, IntPtr.Zero, 2, 150, out length) == IntPtr.Zero ||
                length.ToUInt64() >= 4 * 1024 * 1024) return null;
            var text = new StringBuilder((int)length.ToUInt64() + 1);
            UIntPtr count;
            return GetTextMessage(window, 0x000D, (IntPtr)text.Capacity, text, 2, 150, out count) == IntPtr.Zero
                ? null : text.ToString();
        }

        internal static bool ReplaceEditSelection(IntPtr window, string text)
        {
            UIntPtr ignored;
            return SetTextMessage(window, 0x00C2, (IntPtr)1, text, 0x22, 200, out ignored) != IntPtr.Zero;
        }

        internal static bool ModifiersDown
        {
            get
            {
                return Down(Keys.ControlKey) || Down(Keys.ShiftKey) || Down(Keys.Menu)
                    || Down(Keys.LWin) || Down(Keys.RWin);
            }
        }

        private static bool Down(Keys key) { return GetAsyncKeyState((int)key) < 0; }

        internal static bool ControlKey(Keys key)
        {
            Input[] inputs = { Key(Keys.ControlKey, false), Key(key, false), Key(key, true), Key(Keys.ControlKey, true) };
            return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input))) == inputs.Length;
        }

        private static Input Key(Keys key, bool up)
        {
            return new Input { Type = 1, Data = new InputData { Keyboard = new Keyboard { Key = (ushort)key, Flags = up ? 2u : 0u } } };
        }
    }

    internal sealed class Target
    {
        internal readonly IntPtr Window = Native.GetForegroundWindow();
        internal readonly IntPtr Focus;
        internal readonly uint ProcessId;

        internal Target()
        {
            Focus = Native.Focus(Window);
            ProcessId = Native.ProcessId(Window);
        }

        internal bool IsCurrent
        {
            get { return Window != IntPtr.Zero && Focus != IntPtr.Zero && ProcessId != 0 &&
                Native.GetForegroundWindow() == Window && Native.ProcessId(Window) == ProcessId && Native.Focus(Window) == Focus; }
        }
    }
}
