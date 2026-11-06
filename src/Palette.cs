using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PairTap
{
    internal sealed class Palette : DpiForm
    {
        private const string InterfaceFont = "Microsoft YaHei UI";
        private Font heading, body, fullWidthSymbols, halfWidthSymbols, sample, keys;
        private readonly bool contrast = SystemInformation.HighContrast;
        private readonly bool dark = DarkTheme;
        internal static bool DarkTheme { get { return Convert.ToInt32(Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1)) == 0; } }
        private Color ink, muted, surface, outline, hover, accent, pressed;
        private readonly Panel header, brand, footer, rule;
        private readonly Label title, hint, footerHint;
        private readonly Button settings;
        private readonly Panel viewport, scrolling;
        private readonly VScrollBar scrollbar;
        private readonly Control[] tiles;
        private readonly Bitmap profileQr;
        private bool chosen;
        private int minimumTileWidth, scrollOffset;
        private Size settingsText;
        private bool reflowing, updatingDisplay;
        private double fontDensity;
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
            : base(new Size(744, 546), new Size(325, 310))
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
            ForeColor = ink;
            Icon = icon;
            Text = Product.DisplayName;
            FormBorderStyle = FormBorderStyle.Sizable;
            SizeGripStyle = SizeGripStyle.Show;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;

            header = new Panel { Dock = DockStyle.Top, TabIndex = 0 };
            brand = new Panel();
            brand.Paint += (sender, args) => args.Graphics.DrawIcon(icon, brand.ClientRectangle);
            header.Controls.Add(brand);
            title = MakeLabel(active ? "选择一对标点" : Product.ChineseName, ink);
            header.Controls.Add(title);
            string key = ShortcutSettings.Describe(bindings[0]);
            string instruction = active ? "点击符号，将它加到选区两侧。" :
                paletteAvailable ? "选中文字，再按 " + key + " 打开面板。" :
                "请在快捷键设置中启用面板快捷键。";
            hint = MakeLabel(instruction, muted);
            header.Controls.Add(hint);

            footer = new Panel { Dock = DockStyle.Bottom, TabIndex = 2 };
            rule = new Panel { BackColor = outline, Dock = DockStyle.Top };
            footer.Controls.Add(rule);
            footerHint = MakeLabel(active ? "Tab 切换 · Enter 应用 · Esc 关闭" : "拖动边框可调整面板大小", muted);
            footer.Controls.Add(footerHint);
            settings = new Button {
                Text = "快捷键设置", ForeColor = ink, BackColor = surface,
                FlatStyle = contrast ? FlatStyle.System : FlatStyle.Flat
            };
            settings.FlatAppearance.BorderColor = outline;
            settings.FlatAppearance.MouseOverBackColor = hover;
            settings.Click += (sender, args) => {
                chosen = true;
                if (Configure != null) Configure();
            };
            footer.Controls.Add(settings);

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
            scrollbar.ValueChanged += (sender, args) =>
            {
                if (reflowing) return;
                scrollOffset = scrollbar.Value;
                Reflow();
            };
            Deactivate += (sender, args) => { if (!chosen && active) Close(); };
        }

        protected override void LayoutForDisplay()
        {
            updatingDisplay = true;
            if (fontDensity != Density)
            {
                Font[] previous = { heading, body, fullWidthSymbols, halfWidthSymbols, sample, keys };
                heading = PixelFont(InterfaceFont, 17, FontStyle.Bold);
                body = PixelFont(InterfaceFont, 9);
                fullWidthSymbols = PixelFont("SimSun", 17);
                halfWidthSymbols = PixelFont("Times New Roman", 17);
                sample = PixelFont(InterfaceFont, 10);
                keys = PixelFont("Segoe UI", 8);
                Font = body;
                title.Font = heading;
                hint.Font = footerHint.Font = settings.Font = body;
                foreach (Font font in previous) if (font != null) font.Dispose();
                fontDensity = Density;
            }
            scrollbar.Width = Math.Max(1, Px(17));
            minimumTileWidth = Px(106);
            using (Graphics graphics = CreateGraphics())
            {
                settingsText = TextRenderer.MeasureText(graphics, settings.Text, body, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                foreach (Control tile in tiles)
                {
                    var button = tile as PairButton;
                    if (button != null) minimumTileWidth = Math.Max(minimumTileWidth, button.MeasureContent(graphics));
                }
            }
            updatingDisplay = false;
            LayoutChrome();
            Reflow();
            Invalidate(true);
        }

        protected override void OnClientSizeChanged(EventArgs args)
        {
            base.OnClientSizeChanged(args);
            if (body != null && !updatingDisplay) LayoutChrome();
        }

        private void LayoutChrome()
        {
            int inset = Px(24), top = Px(14), iconSize = Px(44), titleLeft = inset + iconSize + Px(12);
            int headerWidth = Math.Max(1, ClientSize.Width - titleLeft - inset);
            int titleHeight = title.GetPreferredSize(new Size(headerWidth, 0)).Height;
            int hintHeight = hint.GetPreferredSize(new Size(headerWidth, 0)).Height;
            title.SetBounds(titleLeft, top, headerWidth, titleHeight);
            hint.SetBounds(titleLeft, title.Bottom + Px(4), headerWidth, hintHeight);
            header.Height = Math.Max(Px(86), Math.Max(top + iconSize, hint.Bottom) + Px(10));
            brand.SetBounds(inset, (header.Height - iconSize) / 2, iconSize, iconSize);

            rule.Height = Math.Max(1, Px(1));
            int buttonWidth = Math.Max(Px(106), settingsText.Width + Px(20));
            int buttonHeight = Math.Max(Px(30), settingsText.Height + Px(10));
            int hintWidth = Math.Max(1, ClientSize.Width - 2 * inset - Px(14) - buttonWidth);
            int footerHintHeight = footerHint.GetPreferredSize(new Size(hintWidth, 0)).Height;
            footer.Height = Math.Max(Px(54), Math.Max(buttonHeight, footerHintHeight) + Px(18) + rule.Height);
            settings.SetBounds(ClientSize.Width - inset - buttonWidth, (footer.Height - buttonHeight) / 2, buttonWidth, buttonHeight);
            footerHint.SetBounds(inset, (footer.Height - footerHintHeight) / 2, hintWidth, footerHintHeight);
        }

        private void Reflow()
        {
            if (body == null || updatingDisplay || reflowing || viewport.ClientSize.Width <= 0) return;
            reflowing = true;
            try
            {
                int width = viewport.ClientSize.Width;
                int height = Arrange(width, false, 0);
                bool needsScroll = height > viewport.ClientSize.Height;
                scrollbar.Visible = needsScroll;
                if (needsScroll)
                {
                    width -= scrollbar.Width;
                    height = Arrange(width, false, 0);
                }
                scrollbar.Maximum = Math.Max(0, height - 1);
                scrollbar.LargeChange = Math.Max(1, viewport.ClientSize.Height);
                scrollbar.SmallChange = Math.Max(1, Px(26));
                int offset = needsScroll ? Math.Min(scrollOffset, ScrollLimit) : 0;
                scrollbar.Value = offset;
                scrollOffset = offset;
                Arrange(width, true, offset);
            }
            finally { reflowing = false; }
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
            int padding = Px(24);
            int gap = Px(8);
            int verticalPadding = Px(16);
            int available = Math.Max(width - padding * 2, 1);
            int columns = Math.Max(1, Math.Min(6, (available + gap) / Math.Max(1, minimumTileWidth + gap)));
            int tileWidth = (available + gap) / columns - gap;
            int tileHeight = Math.Max(Px(86), profileQr.Width + Px(12));
            foreach (Control tile in tiles)
            {
                var button = tile as PairButton;
                if (button != null) tileHeight = Math.Max(tileHeight, button.ContentHeight(tileWidth) + 2 * Px(8));
            }
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

        private static Label MakeLabel(string text, Color color)
        {
            return new Label {
                Text = text, ForeColor = color, TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false
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
            foreach (Font font in new[] { heading, body, fullWidthSymbols, halfWidthSymbols, sample, keys })
                if (font != null) font.Dispose();
            profileQr.Dispose();
        }

        private static Color Hex(int rgb) { return Color.FromArgb(255, Color.FromArgb(rgb)); }

        private void DrawQr(Graphics g, Size size)
        {
            if (size.Width < 3 || size.Height < 3) return;
            float scale = (float)Density;
            DrawCard(g, size, scale, surface, outline, false);
            int padding = (int)Math.Round(8 * scale);
            int unit = (Math.Min(size.Width, Math.Min(size.Height, scrolling.ClientSize.Height)) - padding) / profileQr.Width;
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
            private readonly int separator;
            private Font symbol;
            private Size left, right, sample, keySize;
            private int previewWidth, previewHeight, labelHeight;
            private bool hovering, pressing;

            internal PairButton(Palette palette, Pair pair, string key)
            {
                this.palette = palette;
                this.pair = pair;
                this.key = key;
                separator = key.LastIndexOf('+');
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
                if (symbol == null || Width < 3 || Height < 3) return;
                float scale = (float)palette.Density;
                Color fill = !Enabled ? palette.BackColor :
                    pressing ? palette.pressed : hovering ? palette.hover : palette.surface;
                Color border = Focused ? palette.accent : palette.outline;
                DrawCard(g, ClientSize, scale, fill, border, Focused);
                DrawContent(g);
            }

            internal int MeasureContent(Graphics g)
            {
                symbol = pair.Left[0] <= '\u00ff' ? palette.halfWidthSymbols : palette.fullWidthSymbols;
                left = Measure(g, pair.Left, symbol);
                right = Measure(g, pair.Right, symbol);
                sample = Measure(g, "文", palette.sample);
                previewWidth = left.Width + sample.Width + right.Width + 2 * palette.Px(2);
                previewHeight = Math.Max(sample.Height, Math.Max(left.Height, right.Height));
                Size label = Measure(g, pair.Name, palette.body);
                labelHeight = label.Height;
                keySize = key.Length == 0 ? Size.Empty : Measure(g, key, palette.keys);
                int keyWidth = separator > 0 && separator < key.Length - 1
                    ? Math.Max(Measure(g, key.Substring(0, separator + 1), palette.keys).Width,
                        Measure(g, key.Substring(separator + 1), palette.keys).Width)
                    : keySize.Width;
                return Math.Max(previewWidth, Math.Max(label.Width, keyWidth)) + 2 * palette.Px(6);
            }

            private bool Wraps(int width) { return separator > 0 && separator < key.Length - 1 && keySize.Width > width - 2 * palette.Px(6); }

            internal int ContentHeight(int width)
            {
                int gap = palette.Px(4);
                return previewHeight + gap + labelHeight +
                    (keySize.IsEmpty ? 0 : gap + keySize.Height * (Wraps(width) ? 2 : 1));
            }

            private void DrawContent(Graphics g)
            {
                int glyphGap = palette.Px(2);
                int gap = palette.Px(4);
                int inset = palette.Px(6);
                int textWidth = Width - 2 * inset;
                int y = (Height - ContentHeight(Width)) / 2;
                int x = (Width - previewWidth) / 2;

                Draw(g, pair.Left, symbol, palette.accent, x, y, left.Width, previewHeight);
                x += left.Width + glyphGap;
                Draw(g, "文", palette.sample, palette.muted, x, y, sample.Width, previewHeight);
                x += sample.Width + glyphGap;
                Draw(g, pair.Right, symbol, palette.accent, x, y, right.Width, previewHeight);

                y += previewHeight + gap;
                Draw(g, pair.Name, palette.body, palette.ink, inset, y, textWidth, labelHeight);
                if (keySize.IsEmpty) return;
                y += labelHeight + gap;
                if (Wraps(Width))
                {
                    Draw(g, key.Substring(0, separator + 1), palette.keys, palette.muted, inset, y, textWidth, keySize.Height);
                    Draw(g, key.Substring(separator + 1), palette.keys, palette.muted, inset, y + keySize.Height, textWidth, keySize.Height);
                }
                else
                    Draw(g, key, palette.keys, palette.muted, inset, y, textWidth, keySize.Height);
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
            float d = Math.Max(1, Math.Min(Math.Min(width, height), radius * 2));
            path.AddArc(1, 1, d, d, 180, 90);
            path.AddArc(width - d, 1, d, d, 270, 90);
            path.AddArc(width - d, height - d, d, d, 0, 90);
            path.AddArc(1, height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

    }
}
