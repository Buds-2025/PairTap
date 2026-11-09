using System;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
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
        internal const string NoSelection = "请先选中要包裹的文字。";
        private const string UnavailableSelection = "当前控件未提供可核验的选区。";
        private const string ReadOnlySelection = "当前选区为只读或包含受保护内容。";
        private static int probing;
        private static Target clipboardRejected;
        private static string clipboardRejectedEditor;

        internal static async Task<string> Apply(Target target, Pair pair, ClipboardChannel channel)
        {
            for (int i = 0; Native.ModifiersDown && i < 60; i++) await Task.Delay(25);
            if (Native.ModifiersDown) return "松开修饰键后重试。";
            if (!target.IsCurrent) return "输入焦点已变化，请重新选中文字。";

            string className = Native.ClassName(target.Focus);
            if (className == "_WwG") return await WordSelection.Apply(target, pair);

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
                        if (!target.IsCurrent || !Native.CanEdit(target.Focus) ||
                            !string.Equals(Native.ReadEditText(target.Focus), text, StringComparison.Ordinal) ||
                            Native.EditSelection(target.Focus) != positions || !target.IsCurrent || Native.ModifiersDown)
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
            if (CanType(verified) && target.SameControl(clipboardRejected) &&
                verified.EditorIdentity == clipboardRejectedEditor)
            {
                string result = await TypeSelection(target, pair, verified);
                if (result != null)
                {
                    clipboardRejected = null;
                    clipboardRejectedEditor = null;
                }
                return result;
            }
            return await WithClipboard(target, pair, channel, positions, verified);
        }

        private sealed class Selection
        {
            internal string Error, Text;
            internal AutomationElement Element, Provider;
            internal TextPatternRange Range;
            internal ValuePattern Value;
            internal string OriginalValue;
            internal AccessibleSelection Accessible;
            internal bool VerifiedWritable;
            internal string EditorIdentity;
        }

        private static async Task<Selection> Probe(Target target, Selection previous)
        {
            if (Interlocked.CompareExchange(ref probing, 1, 0) != 0)
                return new Selection { Error = "输入控件暂未响应，无法确认选区。" };
            Task<Selection> work = Task.Run(() =>
            {
                try
                {
                    if (previous != null && previous.Accessible != null)
                        return CheckAccessibleSelection(target, previous);
                    Selection result = CheckSelection(target, previous);
                    if (previous == null && result.Error == UnavailableSelection)
                        return CheckAccessibleSelection(target, null);
                    return result;
                }
                finally { Interlocked.Exchange(ref probing, 0); }
            });
            if (await Task.WhenAny(work, Task.Delay(800)) != work)
                return new Selection { Error = "输入控件暂未响应，无法确认选区。" };
            return await work;
        }

        private static Selection CheckSelection(Target target, Selection previous)
        {
            var result = new Selection { Error = UnavailableSelection };
            try
            {
                if (!target.IsCurrent) return result;
                AutomationElement element = AutomationElement.FocusedElement;
                if (element == null) return result;
                if (element.Current.IsPassword || !element.Current.IsEnabled)
                    return new Selection { Error = "当前输入框不支持包裹选区。" };
                AutomationElement provider = element;
                TextPattern textPattern = null;
                for (int i = 0; i < 8 && provider != null && provider.Current.ProcessId == element.Current.ProcessId; i++)
                {
                    object pattern;
                    if (provider.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                    {
                        textPattern = (TextPattern)pattern;
                        break;
                    }
                    provider = TreeWalker.RawViewWalker.GetParent(provider);
                }
                if (textPattern == null) return result;

                var ranges = textPattern.GetSelection();
                if (ranges.Length > 1) return new Selection { Error = "请选择一段连续文字。" };
                if (ranges.Length == 0 || ranges[0].CompareEndpoints(TextPatternRangeEndpoint.Start,
                    ranges[0], TextPatternRangeEndpoint.End) == 0)
                    return element.Current.ControlType == ControlType.Edit ?
                        new Selection { Error = NoSelection } : result;

                TextPatternRange range = ranges[0];
                object readOnly = range.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                object value;
                bool focusedEdit = element.Current.ControlType == ControlType.Edit;
                var valuePattern = focusedEdit && element.TryGetCurrentPattern(ValuePattern.Pattern, out value) ?
                    (ValuePattern)value : null;
                bool writableValue = valuePattern != null && !valuePattern.Current.IsReadOnly;
                if (!provider.Equals(element) && !SelectionBelongsToFocus(range, element))
                    return result;
                if (Equals(readOnly, true) || readOnly == TextPattern.MixedAttributeValue ||
                    (!Equals(readOnly, false) && valuePattern != null && !writableValue))
                    return focusedEdit ? new Selection { Error = ReadOnlySelection } : result;
                bool verifiedWritable = Equals(readOnly, false) || writableValue;
                if (!focusedEdit && !verifiedWritable) return result;

                string selected = range.GetText(2 * 1024 * 1024 + 1);
                if (string.IsNullOrEmpty(selected) || selected.Length > 2 * 1024 * 1024) return result;
                if (previous != null && (!element.Equals(previous.Element) || !provider.Equals(previous.Provider) ||
                    !range.Compare(previous.Range) ||
                    !SameText(selected, previous.Text)))
                    return new Selection { Error = "选区已变化，请重新选中文字。" };
                var verified = new Selection
                {
                    Element = element, Provider = provider, Range = range, Text = selected, VerifiedWritable = verifiedWritable
                };
                if (previous == null && verifiedWritable)
                {
                    int[] id = element.GetRuntimeId();
                    if (id != null && id.Length > 0) verified.EditorIdentity = "uia:" + string.Join(".", id);
                }
                if (writableValue && (previous == null || previous.OriginalValue != null))
                {
                    string field = valuePattern.Current.Value;
                    if (field != null && field.Length <= 64 * 1024)
                    {
                        verified.Value = valuePattern;
                        verified.OriginalValue = field;
                    }
                }
                if (previous != null && previous.OriginalValue != null && !SameText(previous.OriginalValue, verified.OriginalValue))
                    return new Selection { Error = "输入框内容已变化，请重新选中文字。" };
                return verified;
            }
            catch (System.Runtime.InteropServices.COMException error) { Diagnostics.Failure("selection_provider", error); }
            catch (InvalidOperationException error) { Diagnostics.Failure("selection_provider", error); }
            catch (ArgumentException error) { Diagnostics.Failure("selection_provider", error); }
            return result;
        }

        private static Selection CheckAccessibleSelection(Target target, Selection previous)
        {
            AccessibleSelection selected = AccessibleSelection.Read(target);
            if (previous != null && (selected == null || !selected.SameRange(previous.Accessible) ||
                !SameText(selected.Text, previous.Text)))
                return new Selection { Error = "选区已变化，请重新选中文字。" };
            if (selected == null) return new Selection { Error = UnavailableSelection };
            string value = previous == null || previous.OriginalValue != null ? selected.ReadValue() : null;
            if (previous != null && previous.OriginalValue != null && !SameText(previous.OriginalValue, value))
                return new Selection { Error = "输入框内容已变化，请重新选中文字。" };
            return new Selection
            {
                Text = selected.Text,
                Accessible = selected,
                VerifiedWritable = true,
                OriginalValue = value != null && value.Length <= 64 * 1024 ? value : null
            };
        }

        private static bool SelectionBelongsToFocus(TextPatternRange range, AutomationElement focused)
        {
            AutomationElement enclosing = range.GetEnclosingElement();
            for (int i = 0; i < 8 && enclosing != null; i++)
            {
                if (enclosing.Equals(focused)) return true;
                enclosing = TreeWalker.RawViewWalker.GetParent(enclosing);
            }
            return false;
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
            internal bool MayType, PasteUnconfirmed;
        }

        private static async Task<string> WithClipboard(Target target, Pair pair, ClipboardChannel channel,
            ulong? positions, Selection verified)
        {
            using (var backup = new ClipboardBuffer(channel.Handle))
            {
                string captureError = null;
                try { await backup.Capture(); }
                catch (InvalidOperationException captureFailure) { captureError = captureFailure.Message; }
                catch (Win32Exception) { captureError = "当前剪贴板无法安全保留。"; }
                if (captureError != null)
                    return CanType(verified) ? await TypeSelection(target, pair, verified) : captureError;

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
                        if (restored != RestoreResult.Restored) transfer.MayType = transfer.PasteUnconfirmed = false;
                    }
                    if (error != null) error.Throw();
                }
                finally { channel.EndPaste(); }
                if (transfer.MayType && CanType(verified))
                    return await TypeSelection(target, pair, verified);
                if (transfer.PasteUnconfirmed && CanType(verified) && target.IsCurrent &&
                    verified.EditorIdentity != null && (await Probe(target, verified)).Error == null)
                {
                    // A paste already went to this editor. Switch methods on the next explicit shortcut.
                    clipboardRejected = target;
                    clipboardRejectedEditor = verified.EditorIdentity;
                    return "粘贴后选区仍在，请再按一次原快捷键以直接输入。";
                }
                return result;
            }
        }

        private static async Task<string> CopyAndPaste(Target target, Pair pair, ClipboardChannel channel,
            ClipboardBuffer backup, Transfer transfer, ulong? positions, Selection verified)
        {
            Task<ClipboardStamp?> changed = channel.WatchCopy(backup.Original);
            ClipboardStamp? change;
            try
            {
                if (changed.IsCompleted || !target.IsCurrent || Native.ModifiersDown || !backup.Original.IsCurrent)
                    return "焦点或剪贴板已变化，请重试。";
                if (!Native.ControlKey(Keys.C)) return "无法读取当前窗口中的选区。";
                if (await Task.WhenAny(changed, Task.Delay(700)) != changed || !(change = await changed).HasValue)
                {
                    transfer.MayType = backup.Original.IsCurrent;
                    return "未复制到文字，请在可编辑区域选中文字。";
                }
            }
            finally { channel.StopWatching(); }

            ClipboardStamp copied = change.Value;
            bool ownerless = copied.Owner == IntPtr.Zero;
            if (ownerless ? verified == null || !verified.VerifiedWritable : Native.ProcessId(copied.Owner) != target.ProcessId)
                return "剪贴板已由其他应用更新，请重试。";
            if (!ownerless) transfer.Stamp = copied;

            ClipboardStamp read = copied;
            bool canRetryCopy = verified != null && !ownerless;
            string selected = await Task.Run(() => ClipboardBuffer.ReadSelection(copied, canRetryCopy, out read));
            // Some clipboard providers announce a change before their final text format is available.
            for (int retry = 0; selected == null && canRetryCopy && retry < 2 &&
                target.IsCurrent && ClipboardBuffer.GetClipboardOwner() == copied.Owner; retry++)
            {
                await Task.Delay(25);
                selected = await Task.Run(() => ClipboardBuffer.ReadSelection(copied, true, out read));
            }
            if (!ownerless && !canRetryCopy && read.Owner == copied.Owner) transfer.Stamp = read;
            if (string.IsNullOrEmpty(selected))
            {
                transfer.MayType = !ownerless && transfer.Stamp.Value.IsCurrent;
                return "当前选区没有可包裹的纯文本，或复制内容超过上限。";
            }
            if (verified != null && !SameCopiedText(selected, verified.Text, ownerless))
                return "复制内容与选区不一致，请重试。";
            // An ownerless or later clipboard update is accepted only after its contents match the selection.
            transfer.Stamp = read;

            if (!target.IsCurrent || Native.ModifiersDown)
                return "输入焦点或选区已变化，请重试。";
            string replacement = pair.Left + selected + pair.Right;
            Task<bool> received = ClipboardBuffer.PreparePaste(target, replacement, channel);
            bool staged;
            try { staged = await Task.Run(() => backup.StagePaste(read, target)); }
            finally { transfer.OwnsPaste = ClipboardBuffer.GetClipboardOwner() == channel.Handle; }
            if (!staged) transfer.MayType = transfer.OwnsPaste || read.IsCurrent;
            if (!staged || !target.IsCurrent || Native.ModifiersDown || ClipboardBuffer.GetClipboardOwner() != channel.Handle)
                return "无法安全替换当前选区，请重试。";
            if (verified != null)
            {
                Selection current = await Probe(target, verified);
                if (current.Error != null) return current.Error;
                if (ownerless && !current.VerifiedWritable) return "当前无法确认选区可编辑。";
            }
            if (positions.HasValue && (!Native.CanEdit(target.Focus) || Native.EditSelection(target.Focus) != positions))
                return "输入焦点或选区已变化，请重试。";
            if (!target.IsCurrent || Native.ModifiersDown || ClipboardBuffer.GetClipboardOwner() != channel.Handle)
                return "输入焦点或剪贴板已变化，请重试。";
            channel.Dispatching();
            if (!Native.ControlKey(Keys.V)) return "无法向当前窗口发送粘贴。";

            Task deadline = Task.Delay(550);
            if (await Task.WhenAny(received, deadline) != received || !channel.TextPublished)
            {
                transfer.PasteUnconfirmed = !channel.TextPublished;
                return "当前窗口未读取粘贴内容。";
            }

            string expected = ExpectedValue(verified, replacement);
            if (expected != null && Interlocked.CompareExchange(ref probing, 1, 0) == 0)
            {
                Task<bool> confirmed = VerifyPaste(target, verified, expected, deadline);
                if (await Task.WhenAny(confirmed, Task.Delay(650)) != confirmed || !await confirmed)
                {
                    transfer.PasteUnconfirmed = true;
                    return "当前窗口未确认完整替换，请检查选区。";
                }
            }
            else if (verified != null || !await received) await deadline;
            return null;
        }

        private static bool CanType(Selection selection)
        {
            if (selection == null || !selection.VerifiedWritable || selection.Range == null ||
                selection.OriginalValue == null || string.IsNullOrEmpty(selection.Text)) return false;
            foreach (char character in selection.Text)
                if (char.IsControl(character) || character == '\uFFFC' || character == '\u2028' ||
                    character == '\u2029') return false;
            return true;
        }

        private static async Task<string> TypeSelection(Target target, Pair pair, Selection selection)
        {
            Selection current = await Probe(target, selection);
            if (current.Error != null) return current.Error;
            if (!CanType(current)) return "当前无法确认选区可直接输入。";
            string original = current.OriginalValue;
            int end = await MoveToBoundary(target, current, TextPatternRangeEndpoint.End, original, -1);
            int start = end - current.Text.Length;
            if (start < 0 || !Native.TypeUnicode(target, pair.Right))
                return "无法在选区末尾插入标点，请检查原输入框。";
            string withRight = original.Insert(end, pair.Right);
            if (!await ConfirmValue(target, current, withRight))
                return "未能确认右侧标点已插入，请检查原输入框。";
            if (await MoveToBoundary(target, current, TextPatternRangeEndpoint.Start, withRight, start) != start ||
                !Native.TypeUnicode(target, pair.Left) ||
                !await ConfirmValue(target, current, withRight.Insert(start, pair.Left)))
                return "右侧标点已插入，左侧标点未能确认。请检查原输入框。";
            Diagnostics.Write("wrap_backend unicode");
            return null;
        }

        private static async Task<int> MoveToBoundary(Target target, Selection selection,
            TextPatternRangeEndpoint boundary, string expected, int knownOffset)
        {
            if (Interlocked.CompareExchange(ref probing, 1, 0) != 0) return -1;
            Task deadline = Task.Delay(600);
            Task<int> move = Task.Run(async () =>
            {
                try
                {
                    if (!target.IsCurrent || Native.ModifiersDown ||
                        !selection.Element.Equals(AutomationElement.FocusedElement) || selection.Value.Current.IsReadOnly ||
                        !string.Equals(selection.Value.Current.Value, expected, StringComparison.Ordinal)) return -1;
                    var pattern = (TextPattern)selection.Provider.GetCurrentPattern(TextPattern.Pattern);
                    if (knownOffset < 0)
                    {
                        TextPatternRange[] original = pattern.GetSelection();
                        if (original.Length != 1 || !original[0].Compare(selection.Range)) return -1;
                    }
                    var caret = selection.Range.Clone();
                    caret.MoveEndpointByRange(boundary == TextPatternRangeEndpoint.Start ?
                        TextPatternRangeEndpoint.End : TextPatternRangeEndpoint.Start, caret, boundary);
                    var prefix = pattern.DocumentRange;
                    prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);
                    string before = prefix.GetText(expected.Length + 1);
                    int offset = before.Length;
                    if (!expected.StartsWith(before, StringComparison.Ordinal) ||
                        (knownOffset >= 0 ? offset != knownOffset : offset < selection.Text.Length ||
                        string.CompareOrdinal(expected, offset - selection.Text.Length,
                            selection.Text, 0, selection.Text.Length) != 0)) return -1;
                    if (deadline.IsCompleted || !target.IsCurrent || Native.ModifiersDown) return -1;
                    caret.Select();
                    while (!deadline.IsCompleted && target.IsCurrent && !Native.ModifiersDown)
                    {
                        TextPatternRange[] selected = pattern.GetSelection();
                        if (selected.Length == 1 && selected[0].Compare(caret) &&
                            selection.Element.Equals(AutomationElement.FocusedElement) &&
                            string.Equals(selection.Value.Current.Value, expected, StringComparison.Ordinal)) return offset;
                        await Task.Delay(15).ConfigureAwait(false);
                    }
                }
                catch (System.Runtime.InteropServices.COMException) { }
                catch (InvalidOperationException) { }
                finally { Interlocked.Exchange(ref probing, 0); }
                return -1;
            });
            return await Task.WhenAny(move, deadline) == move ? await move : -1;
        }

        private static async Task<bool> ConfirmValue(Target target, Selection selection, string expected)
        {
            if (Interlocked.CompareExchange(ref probing, 1, 0) != 0) return false;
            Task deadline = Task.Delay(550);
            Task<bool> confirmed = VerifyPaste(target, selection, expected, deadline);
            return await Task.WhenAny(confirmed, deadline) == confirmed && await confirmed;
        }

        private static bool SameCopiedText(string copied, string verified, bool ownerless)
        {
            if (SameText(copied, verified)) return true;
            // HTML paragraph boundaries can be one break in the provider and two on the clipboard.
            // Only permit this difference for a copy attributed to the target process.
            return !ownerless && string.Equals(Regex.Replace(copied, "[\r\n]+", "\n"),
                Regex.Replace(verified, "[\r\n]+", "\n"), StringComparison.Ordinal);
        }

        private static string ExpectedValue(Selection selection, string replacement)
        {
            if (selection == null || selection.OriginalValue == null) return null;
            string value = selection.OriginalValue;
            int start = value.IndexOf(selection.Text, StringComparison.Ordinal);
            if (start < 0 || value.IndexOf(selection.Text, start + 1, StringComparison.Ordinal) >= 0) return null;
            return value.Substring(0, start) + replacement + value.Substring(start + selection.Text.Length);
        }

        private static async Task<bool> VerifyPaste(Target target, Selection selection, string expected, Task deadline)
        {
            try
            {
                return await Task.Run(async () =>
                {
                    while (!deadline.IsCompleted && target.IsCurrent)
                    {
                        string value = selection.Accessible == null ? selection.Value.Current.Value : selection.Accessible.ReadValue();
                        if (SameText(value, expected)) return true;
                        await Task.Delay(30).ConfigureAwait(false);
                    }
                    return false;
                });
            }
            catch (System.Runtime.InteropServices.COMException) { }
            catch (InvalidOperationException) { }
            finally { Interlocked.Exchange(ref probing, 0); }
            return false;
        }
    }
}
