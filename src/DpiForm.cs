using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PairTap
{
    internal abstract class DpiForm : Form
    {
        private readonly Size preferredClient, minimumClient;
        private Rectangle workArea;
        private int monitorDpi;
        private bool arranging, moving;
        protected double Density { get; private set; }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);

        protected DpiForm(Size preferredClient, Size minimumClient)
        {
            this.preferredClient = preferredClient;
            this.minimumClient = minimumClient;
            Density = 1;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
        }

        internal void OpenOn(Screen screen)
        {
            Rectangle area = screen.WorkingArea;
            // Position a small window on the chosen monitor before Windows assigns its DPI.
            Bounds = new Rectangle(area.Left + area.Width / 2 - 60, area.Top + area.Height / 2 - 40, 120, 80);
        }

        protected int Px(double value) { return (int)Math.Round(value * Density, MidpointRounding.AwayFromZero); }

        protected Font PixelFont(string family, float points, FontStyle style = FontStyle.Regular)
        {
            return new Font(family, (float)(points * (96.0 / 72) * Density), style, GraphicsUnit.Pixel);
        }

        protected abstract void LayoutForDisplay();

        protected override void OnLoad(EventArgs e)
        {
            RefreshDisplay(true);
            base.OnLoad(e);
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (monitorDpi != 0 && !moving && !arranging && WindowState == FormWindowState.Normal &&
                Screen.FromControl(this).WorkingArea != workArea)
                RefreshDisplay(false);
        }

        protected override void OnResizeBegin(EventArgs e)
        {
            moving = true;
            base.OnResizeBegin(e);
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            moving = false;
            RefreshDisplay(false);
            base.OnResizeEnd(e);
        }

        protected override void WndProc(ref Message message)
        {
            const int DpiChanged = 0x02E0, DisplayChanged = 0x007E, SettingChanged = 0x001A;
            if (message.Msg == DpiChanged && monitorDpi != 0 && !arranging)
            {
                var rect = (WindowRect)Marshal.PtrToStructure(message.LParam, typeof(WindowRect));
                Rectangle suggested = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                Rectangle area = Screen.FromRectangle(suggested).WorkingArea;
                int dpi = (int)(message.WParam.ToInt64() & 0xffff);
                bool normal = WindowState == FormWindowState.Normal;
                arranging = true;
                try
                {
                    base.WndProc(ref message);
                    if (normal)
                    {
                        MinimumSize = Size.Empty;
                        Bounds = suggested;
                    }
                }
                finally { arranging = false; }
                ApplyDisplay(area, dpi, false);
                return;
            }
            base.WndProc(ref message);
            if ((message.Msg == DisplayChanged || message.Msg == SettingChanged) && monitorDpi != 0 && !arranging)
                RefreshDisplay(false);
        }

        private void RefreshDisplay(bool opening)
        {
            if (!IsHandleCreated || IsDisposed || arranging) return;
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int dpi = (int)GetDpiForWindow(Handle);
            if (opening || area != workArea || dpi != monitorDpi)
                ApplyDisplay(area, dpi, opening);
        }

        internal void ApplyDisplay(Rectangle area, int dpi, bool opening)
        {
            if (area.Width <= 0 || area.Height <= 0) return;
            arranging = true;
            SuspendLayout();
            try
            {
                workArea = area;
                monitorDpi = dpi > 0 ? dpi : 96;
                Size frame = Size - ClientSize;
                int availableWidth = Math.Max(1, area.Width - frame.Width);
                int availableHeight = Math.Max(1, area.Height - frame.Height);
                // Use a more compact layout when the work area cannot fit the full-size controls.
                Density = Math.Min(monitorDpi / 96.0,
                    Math.Min(availableWidth / (double)minimumClient.Width, availableHeight / (double)minimumClient.Height));

                var minimum = new Size(Px(minimumClient.Width) + frame.Width, Px(minimumClient.Height) + frame.Height);
                MinimumSize = minimum;
                if (WindowState == FormWindowState.Normal)
                {
                    int width = Math.Min(area.Width, Math.Max(minimum.Width, opening ? Px(preferredClient.Width) + frame.Width : Width));
                    int height = Math.Min(area.Height, Math.Max(minimum.Height, opening ? Px(preferredClient.Height) + frame.Height : Height));
                    int left = opening ? area.Left + (area.Width - width) / 2 : Math.Max(area.Left, Math.Min(Left, area.Right - width));
                    int top = opening ? area.Top + (area.Height - height) / 2 : Math.Max(area.Top, Math.Min(Top, area.Bottom - height));
                    Bounds = new Rectangle(left, top, width, height);
                }
                // Windows can impose a smaller tracking size than the monitor's reported work area.
                double fitted = Math.Min(ClientSize.Width / (double)minimumClient.Width,
                    ClientSize.Height / (double)minimumClient.Height);
                if (fitted > 0 && fitted < Density)
                {
                    Density = fitted;
                    MinimumSize = new Size(Px(minimumClient.Width) + frame.Width, Px(minimumClient.Height) + frame.Height);
                }
                LayoutForDisplay();
            }
            finally
            {
                ResumeLayout();
                arranging = false;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowRect
        {
            internal int Left, Top, Right, Bottom;
        }
    }
}
