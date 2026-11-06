using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

namespace PairTap
{
    internal sealed class Pair
    {
        internal readonly string Name, Left, Right;

        internal Pair(string name, string left, string right)
        {
            Name = name;
            Left = left;
            Right = right;
        }

        internal string Preview { get { return Left + "文字" + Right; } }
    }

    internal static class Wrap
    {
        private const string NoSelection = "请先选中要包裹的文字。";
        private static int probing;

        internal static async Task<string> Apply(Target target, Pair pair, ClipboardChannel channel)
        {
            for (int i = 0; Native.ModifiersDown && i < 60; i++) await Task.Delay(25);
            if (Native.ModifiersDown) return "松开修饰键后重试。";
            if (!target.IsCurrent) return "输入焦点已变化，请重新选中文字。";

            string className = Native.ClassName(target.Focus);
            string rootClass = Native.ClassName(target.Window);
            if (rootClass == "ConsoleWindowClass" || rootClass == "CASCADIA_HOSTING_WINDOW_CLASS")
                return "当前窗口是终端，请在文本编辑器中使用。";
            bool plainEdit = Native.IsPlainEdit(className);
            bool editControl = plainEdit || className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase);
            ulong? positions = editControl ? Native.EditSelection(target.Focus) : null;
            if (editControl && !Native.CanEdit(target.Focus)) return "当前输入框为只读或受保护。";
            if (positions.HasValue)
            {
                int start = unchecked((int)positions.Value);
                int end = (int)(positions.Value >> 32);
                if (start == end) return NoSelection;
                if (plainEdit)
                {
                    string text = Native.ReadEditText(target.Focus);
                    if (text != null && end <= text.Length && start < end)
                    {
                        if (!Native.EditAllowsLength(target.Focus, text.Length + pair.Left.Length + pair.Right.Length))
                            return "当前输入框没有足够空间加入两侧标点。";
                        if (!target.IsCurrent || !Native.CanEdit(target.Focus) || Native.EditSelection(target.Focus) != positions)
                            return "选区已变化，请重新选中文字。";
                        string replacement = pair.Left + text.Substring(start, end - start) + pair.Right;
                        return Native.ReplaceEditSelection(target.Focus, replacement) ? null : "当前输入框无法替换选区。";
                    }
                }
            }

            Selection verified = null;
            if (!positions.HasValue)
            {
                verified = await Probe(target, null);
                if (verified.Error != null) return verified.Error;
            }
            return await WithClipboard(target, pair, channel, positions, verified);
        }

        private sealed class Selection
        {
            internal string Error, Text;
            internal AutomationElement Element;
            internal TextPatternRange Range;
            internal ValuePattern Value;
            internal string OriginalValue;
        }

        private static async Task<Selection> Probe(Target target, Selection previous)
        {
            if (Interlocked.CompareExchange(ref probing, 1, 0) != 0)
                return new Selection { Error = "输入控件暂未响应，无法确认选区。" };
            Task<Selection> work = Task.Run(() =>
            {
                try { return CheckSelection(target, previous); }
                finally { Interlocked.Exchange(ref probing, 0); }
            });
            if (await Task.WhenAny(work, Task.Delay(450)) != work)
                return new Selection { Error = "输入控件暂未响应，无法确认选区。" };
            return await work;
        }

        private static Selection CheckSelection(Target target, Selection previous)
        {
            var result = new Selection { Error = "当前控件未提供可核验的选区。" };
            try
            {
                if (!target.IsCurrent) return result;
                AutomationElement element = AutomationElement.FocusedElement;
                if (element == null) return result;
                if (element.Current.IsPassword || !element.Current.IsEnabled)
                    return new Selection { Error = "当前输入框不支持包裹选区。" };
                object value;
                var valuePattern = element.TryGetCurrentPattern(ValuePattern.Pattern, out value) ? (ValuePattern)value : null;
                if (valuePattern != null && valuePattern.Current.IsReadOnly)
                    return new Selection { Error = "当前输入框为只读。" };
                object text;
                if (!element.TryGetCurrentPattern(TextPattern.Pattern, out text)) return result;

                var ranges = ((TextPattern)text).GetSelection();
                if (ranges.Length > 1) return new Selection { Error = "请选择一段连续文字。" };
                if (ranges.Length == 0 || ranges[0].CompareEndpoints(TextPatternRangeEndpoint.Start,
                    ranges[0], TextPatternRangeEndpoint.End) == 0)
                    return new Selection { Error = NoSelection };

                TextPatternRange range = ranges[0];
                string selected = range.GetText(2 * 1024 * 1024 + 1);
                if (string.IsNullOrEmpty(selected) || selected.Length > 2 * 1024 * 1024) return result;
                if (previous != null && (!element.Equals(previous.Element) || !range.Compare(previous.Range) ||
                    !SameText(selected, previous.Text)))
                    return new Selection { Error = "选区已变化，请重新选中文字。" };
                var verified = new Selection { Element = element, Range = range, Text = selected };
                if (previous == null && valuePattern != null)
                {
                    string field = valuePattern.Current.Value;
                    if (field != null && field.Length <= 64 * 1024)
                    {
                        verified.Value = valuePattern;
                        verified.OriginalValue = field;
                    }
                }
                return verified;
            }
            catch (System.Runtime.InteropServices.COMException) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            return result;
        }

        private static bool SameText(string left, string right)
        {
            return string.Equals(left, right, StringComparison.Ordinal) ||
                (left != null && right != null && string.Equals(left.Replace("\r\n", "\n").Replace('\r', '\n'),
                    right.Replace("\r\n", "\n").Replace('\r', '\n'), StringComparison.Ordinal));
        }

        private sealed class Transfer
        {
            internal ClipboardStamp? Stamp;
            internal bool OwnsPaste;
        }

        private static async Task<string> WithClipboard(Target target, Pair pair, ClipboardChannel channel,
            ulong? positions, Selection verified)
        {
            using (var backup = new ClipboardBuffer(channel.Handle))
            {
                await backup.Capture();
                var transfer = new Transfer();
                string result = null;
                ExceptionDispatchInfo error = null;
                try { result = await CopyAndPaste(target, pair, channel, backup, transfer, positions, verified); }
                catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }

                try
                {
                    if (transfer.Stamp.HasValue)
                    {
                        RestoreResult restored = await Task.Run(() => backup.RestoreIfCurrent(transfer.Stamp.Value, transfer.OwnsPaste));
                        if (restored == RestoreResult.Failed)
                            return "无法完整恢复原剪贴板。";
                    }
                    if (error != null) error.Throw();
                    return result;
                }
                finally { channel.EndPaste(); }
            }
        }

        private static async Task<string> CopyAndPaste(Target target, Pair pair, ClipboardChannel channel,
            ClipboardBuffer backup, Transfer transfer, ulong? positions, Selection verified)
        {
            Task<bool> changed = channel.WatchCopy();
            try
            {
                if (changed.IsCompleted || !target.IsCurrent || Native.ModifiersDown || !backup.Original.IsCurrent)
                    return "焦点或剪贴板已变化，请重试。";
                if (!Native.ControlKey(Keys.C)) return "无法读取当前窗口中的选区。";
                if (await Task.WhenAny(changed, Task.Delay(700)) != changed || !await changed)
                    return "未复制到文字，请在可编辑区域选中文字。";
            }
            finally { channel.StopWatching(); }

            ClipboardStamp copied = ClipboardStamp.Current;
            if (copied.Owner == IntPtr.Zero || Native.ProcessId(copied.Owner) != target.ProcessId)
                return "剪贴板已由其他应用更新，请重试。";
            if (backup.Original.IsCurrent) return "未复制到新的选区。";
            transfer.Stamp = copied;

            ClipboardStamp read = copied;
            string selected = await Task.Run(() => ClipboardBuffer.ReadSelection(copied, target, out read));
            transfer.Stamp = read;
            if (string.IsNullOrEmpty(selected)) return "当前选区没有可包裹的纯文本，或复制内容超过上限。";
            if (verified != null)
            {
                if (!SameText(selected, verified.Text)) return "复制内容与选区不一致，请重试。";
                Selection current = await Probe(target, verified);
                if (current.Error != null) return current.Error;
            }

            if (!target.IsCurrent || Native.ModifiersDown ||
                (positions.HasValue && (!Native.CanEdit(target.Focus) || Native.EditSelection(target.Focus) != positions)))
                return "输入焦点或选区已变化，请重试。";
            string replacement = pair.Left + selected + pair.Right;
            Task<bool> received = ClipboardBuffer.PreparePaste(target, replacement, channel);
            bool staged;
            try { staged = await Task.Run(() => backup.StagePaste(read, target)); }
            finally { transfer.OwnsPaste = ClipboardBuffer.GetClipboardOwner() == channel.Handle; }
            if (!staged || !target.IsCurrent || Native.ModifiersDown || ClipboardBuffer.GetClipboardOwner() != channel.Handle)
                return "无法安全替换当前选区，请重试。";
            channel.Dispatching();
            if (!Native.ControlKey(Keys.V)) return "无法向当前窗口发送粘贴。";

            Task deadline = Task.Delay(550);
            if (await Task.WhenAny(received, deadline) == received && !await received)
            {
                string expected = ExpectedValue(verified, replacement);
                if (expected != null && Interlocked.CompareExchange(ref probing, 1, 0) == 0)
                    await Task.WhenAny(VerifyPaste(target, verified.Value, expected, deadline), deadline);
                else
                    await deadline;
            }
            return channel.TextPublished ? null : "当前窗口未读取粘贴内容。";
        }

        private static string ExpectedValue(Selection selection, string replacement)
        {
            if (selection == null || selection.OriginalValue == null) return null;
            string value = selection.OriginalValue;
            int start = value.IndexOf(selection.Text, StringComparison.Ordinal);
            if (start < 0 || value.IndexOf(selection.Text, start + 1, StringComparison.Ordinal) >= 0) return null;
            return value.Substring(0, start) + replacement + value.Substring(start + selection.Text.Length);
        }

        private static async Task VerifyPaste(Target target, ValuePattern value, string expected, Task deadline)
        {
            bool verified = false;
            try
            {
                verified = await Task.Run(async () =>
                {
                    while (!deadline.IsCompleted && target.IsCurrent)
                    {
                        if (SameText(value.Current.Value, expected)) return true;
                        await Task.Delay(30).ConfigureAwait(false);
                    }
                    return false;
                });
            }
            catch (System.Runtime.InteropServices.COMException) { }
            catch (InvalidOperationException) { }
            finally { Interlocked.Exchange(ref probing, 0); }
            if (!verified) await deadline;
        }
    }
}
