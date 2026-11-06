using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PairTap
{
    internal enum TrayCommand { Palette = 1, Settings, Exit }

    internal sealed class TrayIconWindow : NativeWindow, IDisposable
    {
        private const int Callback = 0x8001, DispatchCommand = 0x8002;
        private const string OwnerCaption = Product.Name + " Notification Area";
        private const uint IconId = 1;
        private const uint Add = 0, Modify = 1, Delete = 2, ReturnFocus = 3, SetVersion = 4;
        private const uint MessageFlag = 1, IconFlag = 2, TipFlag = 4, InfoFlag = 16, ShowTipFlag = 128;
        private const uint ContextMenu = 0x007B, LeftUp = 0x0202, RightUp = 0x0205;
        private const uint Select = 0x0400, KeySelect = 0x0401;
        private const uint MenuDisabled = 0x0001, MenuSeparator = 0x0800;
        private const uint MenuRightButton = 0x0002, MenuBottomAlign = 0x0020, MenuReturnCommand = 0x0100;
        private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        private readonly Action<TrayCommand> command;
        private readonly Icon icon;
        private bool registered, version4;
        internal bool MenuOpen { get; private set; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct IconData
        {
            public uint Size;
            public IntPtr Window;
            public uint Id, Flags, Callback;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
            public uint State, StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
            public uint InfoFlags;
            public Guid Guid;
            public IntPtr BalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IconIdentifier
        {
            public uint Size;
            public IntPtr Window;
            public uint Id;
            public Guid Guid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Bounds { public int Left, Top, Right, Bottom; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint operation, ref IconData data);
        [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref IconIdentifier identifier, out Bounds bounds);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
        [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);
        [DllImport("user32.dll")] private static extern bool EndMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string caption);
        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        internal TrayIconWindow(Icon icon, Action<TrayCommand> command)
        {
            this.icon = icon;
            this.command = command;
            // A top-level owner receives the Explorer restart broadcast and can own the popup menu.
            CreateHandle(new CreateParams { Caption = OwnerCaption });
            EnsureRegistered();
        }

        internal static void AllowResidentActivation()
        {
            uint processId = Native.ProcessId(FindWindow(null, OwnerCaption));
            if (processId != 0) AllowSetForegroundWindow(processId);
        }

        private IconData Data()
        {
            return new IconData { Size = (uint)Marshal.SizeOf(typeof(IconData)), Window = Handle, Id = IconId };
        }

        internal void EnsureRegistered()
        {
            if (registered) return;
            var data = Data();
            data.Flags = MessageFlag | IconFlag | TipFlag | ShowTipFlag;
            data.Callback = Callback;
            data.Icon = icon.Handle;
            data.Tip = Product.DisplayName;
            registered = Shell_NotifyIcon(Add, ref data);
            if (!registered) return;

            data = Data();
            data.Version = 4;
            version4 = Shell_NotifyIcon(SetVersion, ref data);
        }

        internal void Notify(string message)
        {
            var data = Data();
            data.Flags = InfoFlag;
            data.Info = message.Length <= 255 ? message : message.Substring(0, 255);
            data.InfoTitle = Product.ChineseName;
            data.InfoFlags = 1;
            Shell_NotifyIcon(Modify, ref data);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == taskbarCreated)
            {
                registered = false;
                EnsureRegistered();
            }
            else if (message.Msg == DispatchCommand)
            {
                command((TrayCommand)message.WParam.ToInt32());
                return;
            }
            else if (message.Msg == Callback)
            {
                uint value = unchecked((uint)message.LParam.ToInt64());
                uint id = version4 ? value >> 16 : unchecked((uint)message.WParam.ToInt64());
                if (!registered || id != IconId || MenuOpen) return;
                uint action = value & 0xffff;
                if (action == LeftUp || action == Select || action == KeySelect)
                    PostCommand(TrayCommand.Palette);
                else if (action == ContextMenu || (!version4 && action == RightUp))
                    ShowMenu();
                return;
            }
            base.WndProc(ref message);
        }

        private void ShowMenu()
        {
            IntPtr menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) return;
            uint selected = 0;
            MenuOpen = true;
            try
            {
                AppendMenu(menu, MenuDisabled, UIntPtr.Zero, "先选中文字，再按快捷键");
                AppendMenu(menu, MenuSeparator, UIntPtr.Zero, null);
                AppendMenu(menu, 0, (UIntPtr)TrayCommand.Settings, "快捷键设置(&S)");
                AppendMenu(menu, 0, (UIntPtr)TrayCommand.Palette, "查看符号面板(&P)");
                AppendMenu(menu, 0, (UIntPtr)TrayCommand.Exit, "退出(&X)");
                Point anchor = Cursor.Position;
                var identifier = new IconIdentifier {
                    Size = (uint)Marshal.SizeOf(typeof(IconIdentifier)), Window = Handle, Id = IconId
                };
                Bounds bounds;
                if (Shell_NotifyIconGetRect(ref identifier, out bounds) == 0)
                    anchor = new Point(bounds.Left, bounds.Top);

                bool foreground = Native.SetForegroundWindow(Handle);
                selected = TrackPopupMenuEx(menu, MenuRightButton | MenuBottomAlign | MenuReturnCommand,
                    anchor.X, anchor.Y, Handle, IntPtr.Zero);
                if (!foreground && selected == 0) Diagnostics.Write("tray_menu closed_without_foreground");
            }
            finally
            {
                MenuOpen = false;
                DestroyMenu(menu);
                // Let the shell complete the popup's foreground transition before the next click.
                PostMessage(Handle, 0, IntPtr.Zero, IntPtr.Zero);
            }

            if (selected == 0)
            {
                var data = Data();
                Shell_NotifyIcon(ReturnFocus, ref data);
            }
            else PostCommand((TrayCommand)selected);
        }

        private void PostCommand(TrayCommand action)
        {
            PostMessage(Handle, DispatchCommand, (IntPtr)(int)action, IntPtr.Zero);
        }

        internal void DismissMenu()
        {
            if (MenuOpen) EndMenu();
        }

        public void Dispose()
        {
            if (registered)
            {
                var data = Data();
                Shell_NotifyIcon(Delete, ref data);
                registered = false;
            }
            DestroyHandle();
        }
    }
}
