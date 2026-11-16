using System;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
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
        private static readonly ProviderWork providers = new ProviderWork();
        private static Target clipboardRejected;
        private static string clipboardRejectedEditor;

        internal static async Task<string> Apply(Target target, Pair pair, ClipboardChannel channel)
        {
            for (int i = 0; Native.ModifiersDown && i < 60; i++) await Task.Delay(25);
            if (Native.ModifiersDown) return "松开修饰键后重试。";
            if (!target.IsCurrent) return "输入焦点已变化，请重新选中文字。";

            string className = Native.ClassName(target.Focus);
            if (className == "_WwG")
            {
                target.Backend = "word";
                target.Stage = "edit";
                return await WordSelection.Apply(target, pair);
            }

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
                        target.Backend = "native-edit";
                        target.Stage = "replace";
                        return Native.ReplaceEditSelection(target.Focus, replacement) ? null : "当前输入框无法替换选区。";
                    }
                }
            }

            Selection verified = null;
            if (!positions.HasValue)
            {
                target.Stage = "probe";
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
            internal TextPattern Pattern;
            internal ValuePattern Value;
            internal string OriginalValue;
            internal int? StartOffset, EndOffset;
            internal AccessibleSelection Accessible;
            internal string AccessibleText;
            internal AccessibleSelection PlainHtmlInput;
            internal bool VerifiedWritable;
            internal string EditorIdentity;
        }

        private static async Task<Selection> Probe(Target target, Selection previous, bool direct = false)
        {
            Task<Selection> work = providers.TryStart(target.Window, target.ProcessId, () =>
            {
                if (previous != null && previous.Accessible != null)
                    return Task.FromResult(CheckAccessibleSelection(target, previous, direct));
                Selection result = CheckSelection(target, previous);
                if (previous == null && result.Error == UnavailableSelection)
                    result = CheckAccessibleSelection(target, null, direct);
                return Task.FromResult(result);
            });
            if (work == null || await Task.WhenAny(work, Task.Delay(800)) != work)
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
                    Element = element, Provider = provider, Range = range, Pattern = textPattern,
                    Text = selected, VerifiedWritable = verifiedWritable
                };
                if (previous == null && verifiedWritable)
                {
                    int[] id = element.GetRuntimeId();
                    if (id != null && id.Length > 0) verified.EditorIdentity = "uia:" + string.Join(".", id);
                }
                if ((writableValue || verifiedWritable && provider.Equals(element)) &&
                    (previous == null || previous.OriginalValue != null))
                {
                    verified.Value = writableValue ? valuePattern : null;
                    string field = ReadFieldValue(verified);
                    if (field != null && field.Length <= 64 * 1024)
                    {
                        verified.OriginalValue = field;
                        if (previous == null) FindFieldRange(textPattern, verified);
                        else
                        {
                            verified.StartOffset = previous.StartOffset;
                            verified.EndOffset = previous.EndOffset;
                        }
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

        private static Selection CheckAccessibleSelection(Target target, Selection previous, bool direct)
        {
            AccessibleSelection selected = AccessibleSelection.Read(target);
            if (previous != null && (selected == null || !selected.SameRange(previous.Accessible) ||
                !SameText(selected.Text, previous.Text)))
                return new Selection { Error = "选区已变化，请重新选中文字。" };
            if (selected == null) return new Selection { Error = UnavailableSelection };
            string value = previous == null || previous.OriginalValue != null ? selected.ReadValue() : null;
            if (previous != null && previous.OriginalValue != null && !SameText(previous.OriginalValue, value))
                return new Selection { Error = "输入框内容已变化，请重新选中文字。" };
            var verified = new Selection
            {
                Text = selected.Text,
                Accessible = selected,
                AccessibleText = direct ? selected.ObserveDirectText() : null,
                VerifiedWritable = true,
                OriginalValue = value != null && value.Length <= 64 * 1024 ? value : null,
                EditorIdentity = selected.EditorIdentity
            };
            int start, end;
            if (previous != null)
            {
                verified.StartOffset = previous.StartOffset;
                verified.EndOffset = previous.EndOffset;
            }
            else if (selected.TryGetFlatOffsets(verified.OriginalValue, out start, out end))
            {
                verified.StartOffset = start;
                verified.EndOffset = end;
            }
            return verified;
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
            target.Backend = "clipboard";
            target.Stage = "capture";
            ClipboardBuffer backup = null;
            string captureError = null;
            try { backup = await ClipboardBuffer.CaptureForWrap(channel.Handle); }
            catch (InvalidOperationException captureFailure) { captureError = captureFailure.Message; }
            catch (Win32Exception) { captureError = "当前剪贴板无法安全保留。"; }
            if (backup == null)
                return CanType(verified) ? await TypeSelection(target, pair, verified) : captureError;
            using (backup)
            {
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
                        {
                            target.Stage = "restore";
                            return "无法完整恢复原剪贴板。";
                        }
                        if (restored == RestoreResult.Changed)
                        {
                            target.Stage = "restore-clipboard-changed";
                            transfer.MayType = transfer.PasteUnconfirmed = false;
                        }
                    }
                    if (error != null) error.Throw();
                }
                finally { channel.EndPaste(); }
                if (transfer.MayType && CanType(verified))
                    return await TypeSelection(target, pair, verified);
                if (transfer.PasteUnconfirmed && CanType(verified) && target.IsCurrent && verified.EditorIdentity != null)
                {
                    Selection unchanged = await Probe(target, verified, direct: true);
                    if (unchanged.Error == null && HasDirectText(unchanged))
                    {
                        // A paste already went to this editor. Switch methods on the next explicit shortcut.
                        clipboardRejected = target;
                        clipboardRejectedEditor = verified.EditorIdentity;
                        return "粘贴后选区仍在，请再按一次原快捷键以直接输入。";
                    }
                }
                return result;
            }
        }

        private static async Task<string> CopyAndPaste(Target target, Pair pair, ClipboardChannel channel,
            ClipboardBuffer backup, Transfer transfer, ulong? positions, Selection verified)
        {
            target.Stage = "copy";
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

            bool canRetryCopy = verified != null && !ownerless;
            Tuple<string, ClipboardStamp> copy;
            try
            {
                copy = await ClipboardBuffer.ReadSelectionBounded(copied, canRetryCopy);
                // Some providers announce a change before their final text format is available.
                for (int retry = 0; copy.Item1 == null && canRetryCopy && retry < 2 &&
                    target.IsCurrent && ClipboardBuffer.GetClipboardOwner() == copied.Owner; retry++)
                {
                    await Task.Delay(25);
                    copy = await ClipboardBuffer.ReadSelectionBounded(copied, true);
                }
            }
            catch (InvalidOperationException error) { return error.Message + " 请检查当前剪贴板。"; }
            string selected = copy.Item1;
            ClipboardStamp read = copy.Item2;
            if (string.IsNullOrEmpty(selected))
            {
                transfer.MayType = backup.Original.IsCurrent;
                return "当前选区没有可包裹的纯文本，或复制内容超过上限。请检查当前剪贴板。";
            }
            if (verified != null)
            {
                bool richParagraphs = !ownerless && (verified.Accessible == null ?
                    verified.Value == null : verified.Accessible.HasNestedText);
                if (!SameCopiedText(selected, verified.Text, richParagraphs))
                    return "复制内容与选区不一致，已保留当前剪贴板，请重试。";
            }
            // The same process can perform an unrelated copy while the shortcut is running.
            // Do not claim the clipboard update until its text matches the selection when available.
            transfer.Stamp = read;

            if (!target.IsCurrent || Native.ModifiersDown)
                return "输入焦点或选区已变化，请重试。";
            target.Stage = "prepare-paste";
            string replacement = pair.Left + selected + pair.Right;
            Task<bool> received = ClipboardBuffer.PreparePaste(target, replacement, channel);
            bool staged = await Task.Run(() => backup.StagePaste(read, target, out transfer.OwnsPaste));
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
            target.Stage = "paste";
            if (!Native.ControlKey(Keys.V)) return "无法向当前窗口发送粘贴。";

            Task deadline = Task.Delay(550);
            if (await Task.WhenAny(received, deadline) != received || !channel.TextPublished)
            {
                transfer.PasteUnconfirmed = !channel.TextPublished;
                return "当前窗口未读取粘贴内容。";
            }

            string expected = ExpectedValue(verified, replacement);
            if (expected != null)
            {
                target.Stage = "verify";
                Task<bool> confirmed = VerifyFieldValue(target, verified, expected, deadline,
                    verified.Value == null && verified.Accessible == null);
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
            if (selection == null || !selection.VerifiedWritable || string.IsNullOrEmpty(selection.Text)) return false;
            if (selection.Accessible != null) return true;
            if (selection.OriginalValue == null || !selection.StartOffset.HasValue || !selection.EndOffset.HasValue)
                return false;
            foreach (char character in selection.Text)
                if (character == '\uFFFC') return false;
            return true;
        }

        private static bool HasDirectText(Selection selection)
        {
            return CanType(selection) && (selection.Accessible == null || selection.AccessibleText != null);
        }

        private static async Task<string> TypeSelection(Target target, Pair pair, Selection selection)
        {
            target.Backend = selection.Accessible == null ? "uia-keys" : "ia2-keys";
            target.Stage = "probe";
            Selection current = await Probe(target, selection, direct: true);
            if (current.Error != null) return current.Error;
            if (!HasDirectText(current)) return "当前无法确认选区可直接输入。";
            string original = current.AccessibleText ?? current.OriginalValue;
            int start = current.Accessible == null ? current.StartOffset.Value : current.Accessible.DirectStart;
            int end = current.Accessible == null ? current.EndOffset.Value : current.Accessible.DirectEnd;
            target.Stage = "right";
            if (!await InsertAtBoundary(target, current, TextPatternRangeEndpoint.End, original, end, pair.Right, true))
                return "无法在选区末尾插入标点，请检查原输入框。";
            string withRight = original.Insert(end, pair.Right);
            if (!await ConfirmValue(target, current, withRight))
            {
                target.Stage = "rollback";
                if (await RestoreDetachedPlainInput(target, current, withRight, original))
                    return "输入焦点已变化，已撤回原输入框中的右侧标点。";
                return "未能确认右侧标点的写入结果，请检查原输入框。";
            }
            target.Stage = "left";
            if (!await InsertAtBoundary(target, current, TextPatternRangeEndpoint.Start, withRight, start, pair.Left, false) ||
                !await ConfirmValue(target, current, withRight.Insert(start, pair.Left)))
            {
                target.Stage = "rollback";
                if (await RemoveInsertedSuffix(target, current, withRight, end, pair.Right) &&
                    await ConfirmValue(target, current, original))
                    return "无法完成两侧标点，已撤回右侧标点。";
                if (await RestoreDetachedPlainInput(target, current, withRight, original))
                    return "输入焦点已变化，已撤回原输入框中的右侧标点。";
                return "未能完成或撤回本次操作，请检查原输入框是否只写入了右侧标点。";
            }
            return null;
        }

        private static async Task<bool> InsertAtBoundary(Target target, Selection selection,
            TextPatternRangeEndpoint boundary, string expected, int offset, string delimiter, bool requireSelection)
        {
            Task deadline = Task.Delay(600);
            Task<bool> move = providers.TryStart(target.Window, target.ProcessId, async () =>
            {
                try
                {
                    if (requireSelection && Native.ClassName(target.Window) == "Chrome_WidgetWin_1")
                        BindPlainHtmlInput(target, selection, expected);
                    if (selection.Accessible != null)
                        return await selection.Accessible.SelectAndApply(target, expected, offset, offset,
                            requireSelection, deadline, () => Native.TypeUnicode(target, delimiter)).ConfigureAwait(false);
                    if (!SameWritableField(target, selection, expected)) return false;
                    var pattern = (TextPattern)selection.Provider.GetCurrentPattern(TextPattern.Pattern);
                    if (requireSelection)
                    {
                        TextPatternRange[] original = pattern.GetSelection();
                        if (original.Length != 1 || !original[0].Compare(selection.Range)) return false;
                    }
                    TextPatternRange caret;
                    if (requireSelection)
                    {
                        caret = selection.Range.Clone();
                        caret.MoveEndpointByRange(boundary == TextPatternRangeEndpoint.Start ?
                            TextPatternRangeEndpoint.End : TextPatternRangeEndpoint.Start, caret, boundary);
                    }
                    else
                    {
                        // Editing the right boundary can move or invalidate the provider's original range.
                        caret = RangeToOffset(pattern, expected, offset, deadline);
                        if (caret == null) return false;
                        caret.MoveEndpointByRange(TextPatternRangeEndpoint.Start, caret, TextPatternRangeEndpoint.End);
                    }
                    var prefix = pattern.DocumentRange;
                    prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);
                    if (PrefixLength(expected, prefix.GetText(expected.Length * 2 + 1)) != offset ||
                        deadline.IsCompleted || !target.IsCurrent || Native.ModifiersDown) return false;
                    caret.Select();
                    while (!deadline.IsCompleted && target.IsCurrent && !Native.ModifiersDown)
                    {
                        TextPatternRange[] selected = pattern.GetSelection();
                        if (selected.Length == 1 && selected[0].Compare(caret) &&
                            SameWritableField(target, selection, expected) &&
                            BoundaryIsWritable(caret, selection) &&
                            !deadline.IsCompleted && target.IsCurrent && !Native.ModifiersDown)
                            return Native.TypeUnicode(target, delimiter);
                        await Task.Delay(15).ConfigureAwait(false);
                    }
                }
                catch (System.Runtime.InteropServices.COMException) { }
                catch (InvalidOperationException) { }
                return false;
            });
            return move != null && await Task.WhenAny(move, deadline) == move && await move;
        }

        private static void BindPlainHtmlInput(Target target, Selection selection, string expected)
        {
            if (selection.Accessible == null && (selection.Value == null ||
                selection.Element.Current.FrameworkId != "Chrome")) return;
            try
            {
                AccessibleSelection proof = selection.Accessible ?? AccessibleSelection.Read(target);
                int start, end;
                if (proof != null && proof.TryGetFlatOffsets(expected, out start, out end) &&
                    (selection.Accessible != null || start == selection.StartOffset && end == selection.EndOffset) &&
                    proof.IsPlainHtmlInput(expected, true)) selection.PlainHtmlInput = proof;
            }
            catch (System.Runtime.InteropServices.COMException) { }
        }

        private static async Task<bool> RestoreDetachedPlainInput(Target target, Selection selection, string expected, string original)
        {
            if (selection.PlainHtmlInput == null || !target.IsAlive) return false;
            Task deadline = Task.Delay(650);
            Task<bool> restore = providers.TryStart(target.Window, target.ProcessId, async () =>
            {
                try
                {
                    if (target.IsCurrent && selection.Element != null &&
                        selection.Element.Equals(AutomationElement.FocusedElement)) return false;
                    if (!selection.PlainHtmlInput.RestorePlainHtmlInput(target, expected, original, deadline)) return false;
                    while (!deadline.IsCompleted && target.IsAlive)
                    {
                        if (string.Equals(selection.PlainHtmlInput.ReadValue(), original, StringComparison.Ordinal)) return true;
                        await Task.Delay(15).ConfigureAwait(false);
                    }
                }
                catch (System.Runtime.InteropServices.COMException) { }
                catch (InvalidOperationException) { }
                return false;
            });
            return restore != null && await Task.WhenAny(restore, deadline) == restore && await restore;
        }

        private static async Task<bool> RemoveInsertedSuffix(Target target, Selection selection, string expected,
            int offset, string suffix)
        {
            Task deadline = Task.Delay(600);
            Task<bool> remove = providers.TryStart(target.Window, target.ProcessId, async () =>
            {
                try
                {
                    if (selection.Accessible != null)
                        return await selection.Accessible.SelectAndApply(target, expected, offset, offset + suffix.Length,
                            false, deadline, () => Native.DeleteSelection(target)).ConfigureAwait(false);
                    if (!SameWritableField(target, selection, expected)) return false;
                    var pattern = (TextPattern)selection.Provider.GetCurrentPattern(TextPattern.Pattern);
                    TextPatternRange first = RangeToOffset(pattern, expected, offset, deadline);
                    TextPatternRange last = RangeToOffset(pattern, expected, offset + suffix.Length, deadline);
                    if (first == null || last == null) return false;
                    first.MoveEndpointByRange(TextPatternRangeEndpoint.Start, first, TextPatternRangeEndpoint.End);
                    first.MoveEndpointByRange(TextPatternRangeEndpoint.End, last, TextPatternRangeEndpoint.End);
                    if (first.GetText(suffix.Length + 1) != suffix || !SameWritableField(target, selection, expected) ||
                        !BoundaryIsWritable(first, selection) ||
                        deadline.IsCompleted) return false;
                    first.Select();
                    while (!deadline.IsCompleted && target.IsCurrent && !Native.ModifiersDown)
                    {
                        TextPatternRange[] selected = pattern.GetSelection();
                        if (selected.Length == 1 && selected[0].Compare(first) &&
                            SameWritableField(target, selection, expected) && !deadline.IsCompleted)
                            return Native.DeleteSelection(target);
                        await Task.Delay(15).ConfigureAwait(false);
                    }
                }
                catch (System.Runtime.InteropServices.COMException) { }
                catch (InvalidOperationException) { }
                return false;
            });
            return remove != null && await Task.WhenAny(remove, deadline) == remove && await remove;
        }

        private static bool SameWritableField(Target target, Selection selection, string expected)
        {
            return target.IsCurrent && !Native.ModifiersDown &&
                selection.Element.Equals(AutomationElement.FocusedElement) &&
                (selection.Value == null || !selection.Value.Current.IsReadOnly) &&
                string.Equals(ReadFieldValue(selection), expected, StringComparison.Ordinal);
        }

        private static bool BoundaryIsWritable(TextPatternRange range, Selection selection)
        {
            object readOnly = range.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
            return Equals(readOnly, false) || selection.Value != null &&
                !Equals(readOnly, true) && readOnly != TextPattern.MixedAttributeValue;
        }

        private static string ReadFieldValue(Selection selection)
        {
            if (selection.Accessible != null) return selection.Accessible.ReadValue();
            if (selection.Value != null) return selection.Value.Current.Value;
            return selection.Pattern.DocumentRange.GetText(64 * 1024 + 1);
        }
        private static TextPatternRange RangeToOffset(TextPattern pattern, string value, int offset, Task deadline)
        {
            // UIA character units can group several UTF-16 units into one user-visible character.
            for (int low = 0, high = offset, count = offset; low <= high && !deadline.IsCompleted;)
            {
                TextPatternRange prefix = pattern.DocumentRange;
                prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, prefix, TextPatternRangeEndpoint.Start);
                prefix.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, count);
                int length = PrefixLength(value, prefix.GetText(value.Length * 2 + 1));
                if (length < 0) return null;
                if (length == offset) return prefix;
                if (length > offset) high = count - 1;
                else low = count + 1;
                count = low + (high - low) / 2;
            }
            return null;
        }

        private static void FindFieldRange(TextPattern pattern, Selection selection)
        {
            try
            {
                var prefix = pattern.DocumentRange;
                string field = selection.OriginalValue;
                prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, selection.Range, TextPatternRangeEndpoint.Start);
                int start = PrefixLength(field, prefix.GetText(field.Length * 2 + 1));
                prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, selection.Range, TextPatternRangeEndpoint.End);
                int end = PrefixLength(field, prefix.GetText(field.Length * 2 + 1));
                if (start >= 0 && end > start && SameText(field.Substring(start, end - start), selection.Text))
                {
                    selection.StartOffset = start;
                    selection.EndOffset = end;
                }
            }
            catch (System.Runtime.InteropServices.COMException) { }
            catch (InvalidOperationException) { }
        }

        private static int PrefixLength(string value, string prefix)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal) &&
                (prefix.Length == 0 || prefix.Length == value.Length ||
                prefix[prefix.Length - 1] != '\r' || value[prefix.Length] != '\n')) return prefix.Length;
            int offset = 0;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (offset == value.Length) return -1;
                char actual = value[offset++], supplied = prefix[i];
                if ((actual == '\n' || actual == '\r') && (supplied == '\n' || supplied == '\r'))
                {
                    if (actual == '\r' && offset < value.Length && value[offset] == '\n') offset++;
                    if (supplied == '\r' && i + 1 < prefix.Length && prefix[i + 1] == '\n') i++;
                }
                else if (actual != supplied) return -1;
            }
            return offset;
        }

        private static async Task<bool> ConfirmValue(Target target, Selection selection, string expected)
        {
            Task deadline = Task.Delay(550);
            Task<bool> confirmed = VerifyFieldValue(target, selection, expected, deadline, direct: true);
            return await Task.WhenAny(confirmed, Task.Delay(650)) == confirmed && await confirmed;
        }

        private static bool SameCopiedText(string copied, string verified, bool richParagraphs)
        {
            if (SameText(copied, verified)) return true;
            // HTML paragraph boundaries can be one break in the provider and two on the clipboard.
            // Plain editors must preserve the number of line breaks.
            return richParagraphs && string.Equals(Regex.Replace(copied, "[\r\n]+", "\n"),
                Regex.Replace(verified, "[\r\n]+", "\n"), StringComparison.Ordinal);
        }

        private static string ExpectedValue(Selection selection, string replacement)
        {
            if (selection == null || selection.OriginalValue == null) return null;
            string value = selection.OriginalValue;
            if (selection.StartOffset.HasValue && selection.EndOffset.HasValue)
                return value.Remove(selection.StartOffset.Value, selection.EndOffset.Value - selection.StartOffset.Value)
                    .Insert(selection.StartOffset.Value, replacement);
            int start = value.IndexOf(selection.Text, StringComparison.Ordinal);
            if (start < 0 || value.IndexOf(selection.Text, start + 1, StringComparison.Ordinal) >= 0) return null;
            return value.Substring(0, start) + replacement + value.Substring(start + selection.Text.Length);
        }

        private static Task<bool> VerifyFieldValue(Target target, Selection selection, string expected, Task deadline,
            bool allowParagraphBreakDifferences = false, bool direct = false)
        {
            return providers.TryStart(target.Window, target.ProcessId, async () =>
            {
                try
                {
                    while (!deadline.IsCompleted && target.IsCurrent)
                    {
                        string value = direct && selection.Accessible != null ?
                            selection.Accessible.ReadDirectText() : ReadFieldValue(selection);
                        if (direct ? string.Equals(value, expected, StringComparison.Ordinal) :
                            SameText(value, expected) || (allowParagraphBreakDifferences && value != null &&
                            SameCopiedText(value, expected, true))) return true;
                        await Task.Delay(30).ConfigureAwait(false);
                    }
                }
                catch (System.Runtime.InteropServices.COMException) { }
                catch (InvalidOperationException) { }
                return false;
            }) ?? Task.FromResult(false);
        }
    }
}
