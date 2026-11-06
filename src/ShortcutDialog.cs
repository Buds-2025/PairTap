using System;
using System.Drawing;
using System.Windows.Forms;

namespace PairTap
{
    internal sealed class ShortcutDialog : Form
    {
        private readonly ListView list;
        private readonly KeyCapture capture;
        private readonly Label status;
        private readonly Button clear, save;
        private readonly Func<Keys[], PunctuationPreference, string> commit;
        private readonly ComboBox shape;
        private readonly Font interfaceFont = new Font("Microsoft YaHei UI", 9);
        private Keys[] draft;
        internal bool Committed { get; private set; }

        internal ShortcutDialog(Keys[] current, PunctuationPreference preference, Icon icon,
            Func<Keys[], PunctuationPreference, string> commit)
        {
            draft = (Keys[])current.Clone();
            this.commit = commit;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = Product.ChineseName + " · 快捷键设置";
            Icon = icon;
            Font = interfaceFont;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(520, 490);
            ClientSize = new Size(602, 542);
            MinimizeBox = false;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 6 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);
            layout.Controls.Add(new Label {
                AutoSize = true, Margin = new Padding(3, 2, 3, 12),
                Text = "选择一项，在快捷键框内按下组合键。保存后立即生效。"
            }, 0, 0);

            var style = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            style.Controls.Add(new Label { Text = "跟随类快捷键的标点样式", AutoSize = true, Margin = new Padding(3, 6, 12, 6) });
            shape = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 158, AccessibleName = "标点样式" };
            shape.Items.AddRange(new object[] { "自动识别输入法", "固定中文标点", "固定半角标点", "固定全角标点" });
            shape.SelectedIndex = (int)preference;
            style.Controls.Add(shape);
            layout.Controls.Add(style, 0, 1);

            list = new ListView {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                HideSelection = false, MultiSelect = false, UseCompatibleStateImageBehavior = false,
                AccessibleName = "符号和快捷键"
            };
            list.Columns.Add("操作", 196);
            list.Columns.Add("符号", 94);
            list.Columns.Add("全局快捷键", 175);
            list.Resize += (sender, args) => list.Columns[2].Width = Math.Max(145, list.ClientSize.Width - 291);
            for (int i = 0; i < Shortcut.All.Length; i++)
            {
                Shortcut entry = Shortcut.All[i];
                var item = new ListViewItem(entry.Name);
                item.SubItems.Add(entry.Pair != null ? entry.Pair.Left + " " + entry.Pair.Right :
                    entry.OriginalKey != '\0' ? entry.Resolve(PunctuationShape.Half).Left + " " +
                    entry.Resolve(PunctuationShape.Half).Right : "—");
                item.SubItems.Add(Display(draft[i]));
                list.Items.Add(item);
            }
            list.SelectedIndexChanged += (sender, args) => RefreshCapture();
            list.DoubleClick += (sender, args) => capture.Focus();
            layout.Controls.Add(list, 0, 2);

            var editor = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 12, 0, 0) };
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            editor.Controls.Add(new Label { Text = "快捷键", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            capture = new KeyCapture { Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = "按下新的快捷键" };
            capture.KeyCaptured += SetShortcut;
            editor.Controls.Add(capture, 1, 0);
            clear = new Button { Text = "清除", AutoSize = true, Anchor = AnchorStyles.Right };
            clear.Click += (sender, args) => SetShortcut(Keys.None);
            editor.Controls.Add(clear, 2, 0);
            layout.Controls.Add(editor, 0, 3);
            status = new Label { AutoSize = true, Margin = new Padding(3, 5, 3, 9), Dock = DockStyle.Fill, AccessibleName = "快捷键设置状态" };
            layout.Controls.Add(status, 0, 4);

            var actions = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            save = new Button { Text = "保存", AutoSize = true };
            save.Click += (sender, args) => SaveChanges();
            var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
            var reset = new Button { Text = "恢复默认", AutoSize = true };
            reset.Click += (sender, args) => {
                draft = ShortcutSettings.Defaults();
                shape.SelectedIndex = (int)PunctuationPreference.Auto;
                for (int i = 0; i < draft.Length; i++) list.Items[i].SubItems[2].Text = Display(draft[i]);
                RefreshCapture();
                status.Text = "已恢复默认值，保存后生效。";
            };
            actions.Controls.Add(save);
            actions.Controls.Add(cancel);
            actions.Controls.Add(reset);
            layout.Controls.Add(actions, 0, 5);
            CancelButton = cancel;
            if (!SystemInformation.HighContrast && Palette.DarkTheme) ApplyDarkTheme(clear, save, cancel, reset);
            Shown += (sender, args) => { list.Items[0].Selected = true; list.Focus(); };
            RefreshCapture();
        }

        private void ApplyDarkTheme(params Button[] buttons)
        {
            BackColor = Color.FromArgb(32, 32, 32);
            ForeColor = Color.FromArgb(245, 245, 245);
            Color surface = Color.FromArgb(43, 43, 43);
            Color border = Color.FromArgb(74, 74, 80);
            capture.BackColor = list.BackColor = surface;
            capture.ForeColor = list.ForeColor = ForeColor;
            shape.BackColor = surface;
            shape.ForeColor = ForeColor;
            Palette.DarkExplorer(list);
            list.OwnerDraw = true;
            list.DrawSubItem += (sender, args) =>
            {
                Color row = args.Item.Selected ? Color.FromArgb(43, 70, 108) : surface;
                using (var fill = new SolidBrush(row)) args.Graphics.FillRectangle(fill, args.Bounds);
                var textBounds = Rectangle.Inflate(args.Bounds, -7, 0);
                TextRenderer.DrawText(args.Graphics, args.SubItem.Text, Font, textBounds, ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            };
            list.DrawColumnHeader += (sender, args) =>
            {
                using (var fill = new SolidBrush(surface)) args.Graphics.FillRectangle(fill, args.Bounds);
                using (var rule = new Pen(border))
                    args.Graphics.DrawLine(rule, args.Bounds.Left, args.Bounds.Bottom - 1, args.Bounds.Right, args.Bounds.Bottom - 1);
                var textBounds = Rectangle.Inflate(args.Bounds, -7, 0);
                TextRenderer.DrawText(args.Graphics, args.Header.Text, Font, textBounds, ForeColor, TextFormatFlags.VerticalCenter);
            };
            foreach (Button button in buttons)
            {
                button.BackColor = surface;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = border;
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) interfaceFont.Dispose();
        }

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            Palette.SetCaption(this, Palette.DarkTheme);
        }

        private int Selection { get { return list.SelectedIndices.Count == 0 ? -1 : list.SelectedIndices[0]; } }

        private void RefreshCapture()
        {
            int index = Selection;
            capture.Enabled = index >= 0;
            clear.Enabled = index >= 0 && draft[index] != Keys.None;
            capture.Text = index < 0 ? "" : Display(draft[index]);
            status.Text = "组合键须包含 Ctrl 或 Alt，可另加 Shift。";
        }

        private void SetShortcut(Keys keys)
        {
            int index = Selection;
            if (index < 0) return;
            if (!ShortcutSettings.IsValid(keys))
            {
                status.Text = "请换一组快捷键。Ctrl+C、Ctrl+V 和 F12 保留给系统或编辑操作。";
                return;
            }
            if (keys != Keys.None)
            {
                int other = Array.IndexOf(draft, keys);
                if (other >= 0 && other != index)
                {
                    status.Text = "该组合键已分配给「" + Shortcut.All[other].Name + "」。";
                    return;
                }
            }
            draft[index] = keys;
            list.Items[index].SubItems[2].Text = Display(keys);
            RefreshCapture();
        }

        private void SaveChanges()
        {
            string error = commit(draft, (PunctuationPreference)shape.SelectedIndex);
            if (error != null)
            {
                status.Text = error;
                return;
            }
            Committed = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string Display(Keys keys)
        {
            return keys == Keys.None ? "未设置" : ShortcutSettings.Describe(keys);
        }

        private sealed class KeyCapture : TextBox
        {
            internal event Action<Keys> KeyCaptured;

            protected override bool ProcessCmdKey(ref Message message, Keys keys)
            {
                if (keys == Keys.Escape || keys == Keys.Tab || keys == (Keys.Shift | Keys.Tab))
                    return base.ProcessCmdKey(ref message, keys);
                Keys key = keys & Keys.KeyCode;
                if (key != Keys.ControlKey && key != Keys.Menu && key != Keys.ShiftKey && KeyCaptured != null)
                    KeyCaptured(keys);
                return true;
            }
        }
    }
}
