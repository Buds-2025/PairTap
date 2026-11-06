using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PairTap
{
    internal sealed class Palette : Form
    {
        private static readonly FontFamily InterfaceFont = new FontFamily("Microsoft YaHei UI");
        private static readonly FontFamily SymbolFont = new FontFamily("Segoe UI");
        private readonly Font heading = new Font(InterfaceFont, 17, FontStyle.Bold);
        private readonly Font body = new Font(InterfaceFont, 9);
        private readonly Font fullWidthSymbols = new Font("SimSun", 17);
        private readonly Font halfWidthSymbols = new Font("Times New Roman", 17);
        private readonly Font sample = new Font(InterfaceFont, 10);
        private readonly Font keys = new Font(SymbolFont, 8);
        private readonly bool contrast = SystemInformation.HighContrast;
        private readonly bool dark = DarkTheme;
        internal static bool DarkTheme { get { return Convert.ToInt32(Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1)) == 0; } }
        private Color ink, muted, surface, outline, hover, accent, pressed;
        private readonly Panel viewport, scrolling;
        private readonly VScrollBar scrollbar;
        private readonly Control[] tiles;
        private readonly Bitmap profileQr;
        private bool chosen;
        internal event Action Configure;
        internal event Action<Pair> Chosen;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr handle, string application, string idList);

        internal static void DarkExplorer(Control control)
        {
            control.HandleCreated += (sender, args) => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        }

        internal Palette(Keys[] bindings, PunctuationShape? shape, bool paletteAvailable, Icon icon)
        {
            bool active = shape.HasValue;
            BackColor = contrast ? SystemColors.Window : dark ? Hex(0x202020) : Hex(0xF5F6F8);
            ink = contrast ? SystemColors.WindowText : dark ? Hex(0xF5F5F5) : Hex(0x1A2333);
            muted = contrast ? SystemColors.WindowText : dark ? Hex(0xBCBFC7) : Hex(0x576477);
            surface = contrast ? SystemColors.Window : dark ? Hex(0x2B2B2B) : Color.White;
            outline = contrast ? SystemColors.WindowText : dark ? Hex(0x4A4A50) : Hex(0xD9DFE8);
            hover = contrast ? SystemColors.Control : dark ? Hex(0x28394F) : Hex(0xEAF2FF);
            pressed = contrast ? SystemColors.Control : dark ? Hex(0x324B6B) : Hex(0xD8E9FF);
            accent = contrast ? SystemColors.Highlight : dark ? Hex(0x91C4FF) : Hex(0x165EC5);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = body;
            ForeColor = ink;
            Icon = icon;
            Text = Product.DisplayName;
            FormBorderStyle = FormBorderStyle.Sizable;
            SizeGripStyle = SizeGripStyle.Show;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(386, 320);
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(744, 546);

            var header = new Panel { Dock = DockStyle.Top, Height = 86, TabIndex = 0 };
            var brand = new Panel { Bounds = new Rectangle(22, 21, 44, 44) };
            brand.Paint += (sender, args) => args.Graphics.DrawIcon(icon, brand.ClientRectangle);
            header.Controls.Add(brand);
            header.Controls.Add(MakeLabel(active ? "选择一对标点" : Product.ChineseName, heading, ink,
                new Rectangle(80, 14, 640, 34)));
            string key = ShortcutSettings.Describe(bindings[0]);
            string instruction = active ? "点击符号，将它加到选区两侧。" :
                paletteAvailable ? "选中文字，再按 " + key + " 打开面板。" :
                "请在快捷键设置中启用面板快捷键。";
            var hint = MakeLabel(instruction, body, muted, new Rectangle(81, 49, 637, 34));
            hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            header.Controls.Add(hint);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, TabIndex = 2 };
            footer.Controls.Add(new Panel { BackColor = outline, Dock = DockStyle.Top, Height = 1 });
            footer.Controls.Add(MakeLabel(active ? "Tab 切换 · Enter 应用 · Esc 关闭" : "拖动边框可调整面板大小", body, muted,
                new Rectangle(24, 11, 205, 30)));
            var settings = new Button {
                Text = "快捷键设置", Size = new Size(106, 30), Location = new Point(612, 11),
                Anchor = AnchorStyles.Top | AnchorStyles.Right, ForeColor = ink, BackColor = surface,
                FlatStyle = contrast ? FlatStyle.System : FlatStyle.Flat
            };
            settings.FlatAppearance.BorderColor = outline;
            settings.FlatAppearance.MouseOverBackColor = hover;
            settings.Click += (sender, args) => {
                chosen = true;
                if (Configure != null) Configure();
            };
            footer.Controls.Add(settings);
            footer.Width = ClientSize.Width;
            settings.Left = footer.ClientSize.Width - 132;

            viewport = new Panel { Dock = DockStyle.Fill, TabIndex = 1 };
            scrolling = new Panel { Dock = DockStyle.Fill };
            scrollbar = new VScrollBar { Dock = DockStyle.Right, TabStop = false, Visible = false };
            viewport.Controls.Add(scrolling);
            viewport.Controls.Add(scrollbar);
            if (dark && !contrast)
                DarkExplorer(scrollbar);
            tiles = new Control[Array.FindAll(Shortcut.All, entry => entry.Pair != null).Length + 1];
            for (int i = 1, position = 0; i < Shortcut.All.Length; i++)
            {
                Pair pair = Shortcut.All[i].Pair;
                if (pair == null) continue;
                Keys binding = bindings[i];
                if (binding == Keys.None && shape.HasValue)
                {
                    for (int j = 1; j < bindings.Length; j++)
                    {
                        if (Shortcut.All[j].Pair != null || bindings[j] == Keys.None ||
                            Shortcut.All[j].Resolve(shape.Value) != pair) continue;
                        binding = bindings[j];
                        break;
                    }
                }
                var button = new PairButton(this, pair, ShortcutSettings.Describe(binding)) { Enabled = active, TabIndex = position + 1 };
                button.Click += (sender, args) => {
                    chosen = true;
                    if (Chosen != null) Chosen(pair);
                };
                button.GotFocus += (sender, args) => BringIntoView(button);
                button.MouseWheel += ScrollTiles;
                scrolling.Controls.Add(button);
                tiles[position++] = button;
            }
            using (var stream = typeof(Palette).Assembly.GetManifestResourceStream("bilibili-qr"))
            using (var image = Image.FromStream(stream))
                profileQr = new Bitmap(image);
            var qr = new Panel {
                TabStop = false, AccessibleRole = AccessibleRole.Graphic,
                AccessibleName = "哔哩哔哩个人主页二维码", BackColor = BackColor
            };
            qr.Paint += (sender, args) => DrawQr(args.Graphics, qr.ClientSize);
            qr.MouseWheel += ScrollTiles;
            scrolling.Controls.Add(qr);
            tiles[tiles.Length - 1] = qr;
            Controls.Add(viewport);
            Controls.Add(footer);
            Controls.Add(header);
            viewport.Resize += (sender, args) => Reflow();
            scrolling.MouseWheel += ScrollTiles;
            scrollbar.ValueChanged += (sender, args) => Arrange(scrolling.Width, true, scrollbar.Value);
            ResizeEnd += (sender, args) => Reflow();
            Load += (sender, args) =>
            {
                Rectangle area = Screen.FromControl(this).WorkingArea;
                Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
                Reflow();
            };
            Deactivate += (sender, args) => { if (!chosen && active) Close(); };
            Reflow();
        }

        private void Reflow()
        {
            if (viewport.ClientSize.Width < 80) return;
            int width = viewport.ClientSize.Width;
            int height = Arrange(width, false, 0);
            bool needsScroll = height > viewport.ClientSize.Height;
            scrollbar.Visible = needsScroll;
            if (needsScroll) width -= scrollbar.Width;
            height = Arrange(width, false, 0);
            scrollbar.Maximum = Math.Max(0, height - 1);
            scrollbar.LargeChange = Math.Max(1, viewport.ClientSize.Height);
            scrollbar.SmallChange = Math.Max(1, DeviceDpi / 4);
            int offset = Math.Min(scrollbar.Value, ScrollLimit);
            scrolling.SuspendLayout();
            scrollbar.Value = offset;
            Arrange(width, true, offset);
            scrolling.ResumeLayout();
        }

        private int ScrollLimit { get { return Math.Max(0, scrollbar.Maximum - scrollbar.LargeChange + 1); } }

        private void ScrollTiles(object sender, MouseEventArgs args)
        {
            if (!scrollbar.Visible || args.Delta == 0) return;
            int lines = SystemInformation.MouseWheelScrollLines;
            int distance = args.Delta * (lines < 0 ? scrolling.Height : lines * scrollbar.SmallChange) / 120;
            scrollbar.Value = Math.Max(0, Math.Min(ScrollLimit, scrollbar.Value - distance));
            var handled = args as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
        }

        private void BringIntoView(Control button)
        {
            if (!scrollbar.Visible) return;
            if (button.Top < 0) scrollbar.Value = Math.Max(0, scrollbar.Value + button.Top);
            else if (button.Bottom > scrolling.Height)
                scrollbar.Value = Math.Min(ScrollLimit, scrollbar.Value + button.Bottom - scrolling.Height);
        }

        private int Arrange(int width, bool place, int scroll)
        {
            double scale = DeviceDpi / 96.0;
            int padding = (int)Math.Round(24 * scale);
            int gap = (int)Math.Round(8 * scale);
            int verticalPadding = (int)Math.Round(16 * scale);
            int tileHeight = (int)Math.Round(86 * scale);
            int available = Math.Max(width - padding * 2, 1);
            int columns = Math.Max(1, Math.Min(6, (available + gap) / (int)Math.Round(114 * scale)));
            int rows = (tiles.Length + columns - 1) / columns;
            if (place)
            {
                for (int item = 0; item < tiles.Length; item++)
                {
                    int column = item % columns;
                    int row = item / columns;
                    int left = column * (available + gap) / columns;
                    int right = (column + 1) * (available + gap) / columns - gap;
                    tiles[item].Bounds = new Rectangle(padding + left,
                        verticalPadding + row * (tileHeight + gap) - scroll, right - left, tileHeight);
                }
            }
            return 2 * verticalPadding + rows * tileHeight + Math.Max(0, rows - 1) * gap;
        }

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            SetCaption(this, dark);
        }

        internal static void SetCaption(Form form, bool dark)
        {
            if (SystemInformation.HighContrast) return;
            int rounded = 2;
            int darkMode = dark ? 1 : 0;
            int caption = ColorTranslator.ToWin32(form.BackColor);
            DwmSetWindowAttribute(form.Handle, 33, ref rounded, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 20, ref darkMode, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
        }

        private static Label MakeLabel(string text, Font font, Color color, Rectangle bounds)
        {
            return new Label {
                Text = text, Font = font, ForeColor = color, TextAlign = ContentAlignment.MiddleLeft,
                Bounds = bounds
            };
        }

        protected override bool ProcessDialogKey(Keys key)
        {
            if (key == Keys.Escape)
            {
                Close();
                return true;
            }
            var selected = ActiveControl as PairButton;
            if (key == Keys.Enter && selected != null && selected.Enabled)
            {
                selected.PerformClick();
                return true;
            }
            return base.ProcessDialogKey(key);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            heading.Dispose();
            body.Dispose();
            fullWidthSymbols.Dispose();
            halfWidthSymbols.Dispose();
            sample.Dispose();
            keys.Dispose();
            profileQr.Dispose();
        }

        private static Color Hex(int rgb) { return Color.FromArgb(255, Color.FromArgb(rgb)); }

        private void DrawQr(Graphics g, Size size)
        {
            float scale = g.DpiX / 96;
            DrawCard(g, size, scale, surface, outline, false);
            int padding = (int)Math.Round(8 * scale);
            int unit = (Math.Min(size.Width, size.Height) - padding) / profileQr.Width;
            if (unit < 1) return;
            int side = unit * profileQr.Width;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(profileQr, new Rectangle((size.Width - side) / 2, (size.Height - side) / 2, side, side));
        }

        private static void DrawCard(Graphics g, Size size, float scale, Color fill, Color border, bool focused)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedRectangle(size.Width - 2, size.Height - 2, 8 * scale))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border, (focused ? 1.8f : 1f) * scale))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
        }

        private sealed class PairButton : Button
        {
            private const TextFormatFlags TextStyle = TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            private readonly Palette palette;
            private readonly Pair pair;
            private readonly string key;
            private bool hovering, pressing;

            internal PairButton(Palette palette, Pair pair, string key)
            {
                this.palette = palette;
                this.pair = pair;
                this.key = key;
                Text = pair.Name;
                AccessibleName = pair.Name;
                AccessibleDescription = key.Length == 0 ? pair.Preview : pair.Preview + "，快捷键 " + key;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovering = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = false; Invalidate(); }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressing = e.Button == MouseButtons.Left; Invalidate(); }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressing = false; Invalidate(); }
            protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); pressing = false; Invalidate(); }
            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); pressing = false; Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(palette.BackColor);
                float scale = g.DpiX / 96;
                Color fill = !Enabled ? palette.BackColor :
                    pressing ? palette.pressed : hovering ? palette.hover : palette.surface;
                Color border = Focused ? palette.accent : palette.outline;
                DrawCard(g, ClientSize, scale, fill, border, Focused);
                DrawContent(g, scale);
            }

            private void DrawContent(Graphics g, float scale)
            {
                Font symbol = pair.Left[0] <= '\u00ff' ? palette.halfWidthSymbols : palette.fullWidthSymbols;
                Size left = Measure(g, pair.Left, symbol);
                Size right = Measure(g, pair.Right, symbol);
                Size sample = Measure(g, "文", palette.sample);
                int glyphGap = (int)Math.Round(2 * scale);
                int previewWidth = left.Width + sample.Width + right.Width + 2 * glyphGap;
                int previewHeight = Math.Max(sample.Height, Math.Max(left.Height, right.Height));
                int labelHeight = Measure(g, pair.Name, palette.body).Height;
                int gap = (int)Math.Round(4 * scale);
                int inset = (int)Math.Round(6 * scale);
                int textWidth = Width - 2 * inset;
                Size keySize = key.Length == 0 ? Size.Empty : Measure(g, key, palette.keys);
                int separator = key.LastIndexOf('+');
                bool wraps = separator > 0 && separator < key.Length - 1 && keySize.Width > textWidth;
                int keyHeight = keySize.Height * (wraps ? 2 : 1);
                int contentHeight = previewHeight + gap + labelHeight + (keyHeight == 0 ? 0 : gap + keyHeight);
                int y = (Height - contentHeight) / 2;
                int x = (Width - previewWidth) / 2;

                Draw(g, pair.Left, symbol, palette.accent, x, y, left.Width, previewHeight);
                x += left.Width + glyphGap;
                Draw(g, "文", palette.sample, palette.muted, x, y, sample.Width, previewHeight);
                x += sample.Width + glyphGap;
                Draw(g, pair.Right, symbol, palette.accent, x, y, right.Width, previewHeight);

                y += previewHeight + gap;
                Draw(g, pair.Name, palette.body, palette.ink, inset, y, textWidth, labelHeight);
                if (keyHeight == 0) return;
                y += labelHeight + gap;
                if (wraps)
                {
                    Draw(g, key.Substring(0, separator + 1), palette.keys, palette.muted, inset, y, textWidth, keySize.Height);
                    Draw(g, key.Substring(separator + 1), palette.keys, palette.muted, inset, y + keySize.Height, textWidth, keySize.Height);
                }
                else
                    Draw(g, key, palette.keys, palette.muted, inset, y, textWidth, keyHeight);
            }

            private static Size Measure(Graphics g, string text, Font font)
            {
                return TextRenderer.MeasureText(g, text, font, Size.Empty, TextStyle);
            }

            private static void Draw(Graphics g, string text, Font font, Color color, int x, int y, int width, int height)
            {
                TextRenderer.DrawText(g, text, font, new Rectangle(x, y, width, height), color,
                    TextStyle | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
        private static GraphicsPath RoundedRectangle(float width, float height, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(1, 1, d, d, 180, 90);
            path.AddArc(width - d, 1, d, d, 270, 90);
            path.AddArc(width - d, height - d, d, d, 0, 90);
            path.AddArc(1, height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

    }
}
