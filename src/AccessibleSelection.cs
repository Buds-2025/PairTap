using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Accessibility;

namespace PairTap
{
    internal sealed class AccessibleSelection
    {
        private const uint ClientObject = 0xFFFFFFFC;
        private const int Self = 0, Focused = 0x4;
        private const int Blocked = 0x1 | 0x40 | 0x20000000; // Unavailable, read-only, protected.
        private const int Editable = 0x8, DefunctOrStale = 0x4 | 0x4000;
        private const int MaxText = 2 * 1024 * 1024;
        private const int DirectTextLimit = 64 * 1024;
        private const int RendererLimit = 8;
        private const char EmbeddedObject = '\uFFFC';

        internal readonly string Text;
        private readonly List<Span> spans;
        private readonly IAccessible editor;

        private struct Span
        {
            internal IntPtr Window;
            internal int Id, Start, End;
        }

        private sealed class TextRun
        {
            internal IAccessibleText Provider;
            internal Span Node;
            internal int Start, End, Offset;
        }

        private sealed class TextSnapshot
        {
            internal readonly StringBuilder Value = new StringBuilder();
            internal readonly List<TextRun> Runs = new List<TextRun>();
            internal readonly HashSet<int> Nodes = new HashSet<int>();
            internal int Start = -1, End = -1, SelectedLength;

            internal TextRun RunAt(int offset, bool previous)
            {
                foreach (TextRun run in Runs)
                {
                    if (previous ? run.Start < offset && offset <= run.End :
                        run.Start <= offset && offset < run.End) return run;
                }
                return null;
            }
        }

        private AccessibleSelection(string text, List<Span> spans, IAccessible editor)
        {
            Text = text;
            this.spans = spans;
            this.editor = editor;
        }

        internal string ReadValue()
        {
            try { return editor.get_accValue(Self); }
            catch (COMException) { return null; }
        }

        internal string EditorIdentity { get { return "ia2:" + spans[0].Window + ":" + spans[0].Id; } }
        internal bool HasNestedText { get { return spans.Count > 1; } }
        internal int DirectStart { get; private set; }
        internal int DirectEnd { get; private set; }

        internal string ObserveDirectText()
        {
            TextSnapshot snapshot = ReadDirectSnapshot(true);
            if (snapshot == null || snapshot.SelectedLength != Text.Length) return null;
            string value = snapshot.Value.ToString();
            if (!MatchesDirectSelection(value, Text, snapshot.Start, snapshot.End)) return null;
            DirectStart = snapshot.Start;
            DirectEnd = snapshot.End;
            return value;
        }

        private static bool MatchesDirectSelection(string field, string selected, int start, int end)
        {
            return field != null && field.Length <= DirectTextLimit && start >= 0 && end > start && end <= field.Length &&
                IsCharacterBoundary(field, start) && IsCharacterBoundary(field, end) &&
                string.Equals(field.Substring(start, end - start), selected, StringComparison.Ordinal);
        }

        private static bool IsCharacterBoundary(string field, int offset)
        {
            return offset == 0 || offset == field.Length ||
                !char.IsSurrogatePair(field[offset - 1], field[offset]);
        }

        internal string ReadDirectText()
        {
            TextSnapshot snapshot = ReadDirectSnapshot(false);
            return snapshot == null ? null : snapshot.Value.ToString();
        }

        private TextSnapshot ReadDirectSnapshot(bool withSelection)
        {
            var snapshot = new TextSnapshot();
            try
            {
                if (AppendDirectText(editor, snapshot, withSelection, 0)) return snapshot;
            }
            catch (COMException) { }
            catch (InvalidCastException) { }
            catch (ArgumentException) { }
            return null;
        }

        private bool AppendDirectText(IAccessible node, TextSnapshot snapshot, bool withSelection, int depth)
        {
            if (node == null || depth == 8 || snapshot.Nodes.Count >= 64) return false;
            var identity = Query<IAccessible2>(node);
            object state = node.get_accState(Self);
            if (identity == null || !(state is int) || ((int)state & Blocked) != 0) return false;
            int ia2State = identity.GetStates();
            Span current = new Span { Id = identity.GetUniqueId(), Window = identity.GetWindowHandle() };
            if ((ia2State & DefunctOrStale) != 0 || current.Id == 0 || current.Window != spans[0].Window ||
                !snapshot.Nodes.Add(current.Id) ||
                depth == 0 && (current.Id != spans[0].Id || (ia2State & Editable) == 0)) return false;
            var text = Query<IAccessibleText>(node);
            if (text == null) return false;
            int count = text.GetCharacterCount();
            if (count < 0 || count > DirectTextLimit + 2) return false;
            if (count == 0) return true;
            string value = text.GetText(0, count);
            if (value == null || value.Length != count) return false;
            Span selected = withSelection ? spans.Find(span => span.Id == current.Id && span.Window == current.Window) : new Span();

            // An IA2 embedded object occupies one unit in the parent but has its own offsets.
            // Record the owner of each text run so a boundary can be applied to its exact node.
            int offset = 0;
            while (offset < value.Length)
            {
                int marker = value.IndexOf(EmbeddedObject, offset);
                int end = marker < 0 ? value.Length : marker;
                int length = end - offset;
                int start = snapshot.Value.Length;
                if (start + length > DirectTextLimit + 2) return false;
                if (length > 0)
                {
                    snapshot.Value.Append(value, offset, length);
                    snapshot.Runs.Add(new TextRun { Provider = text, Node = current, Start = start,
                        End = start + length, Offset = offset });
                    int first = Math.Max(offset, selected.Start), last = Math.Min(end, selected.End);
                    if (selected.Id != 0 && first < last)
                    {
                        if (snapshot.Start < 0) snapshot.Start = start + first - offset;
                        snapshot.End = start + last - offset;
                        snapshot.SelectedLength += last - first;
                    }
                }
                if (marker < 0) break;
                var hypertext = text as IAccessibleHypertext;
                int child = hypertext == null ? -1 : hypertext.GetHyperlinkIndex(marker);
                if (child < 0 || !AppendDirectText(hypertext.GetHyperlink(child) as IAccessible,
                    snapshot, withSelection, depth + 1)) return false;
                offset = marker + 1;
            }
            return true;
        }

        internal bool TryGetFlatOffsets(string value, out int start, out int end)
        {
            start = spans[0].Start;
            end = spans[0].End;
            if (spans.Count != 1 || value == null || end > value.Length ||
                !string.Equals(value.Substring(start, end - start), Text, StringComparison.Ordinal)) return false;
            try
            {
                var text = Query<IAccessibleText>(editor);
                return text != null && string.Equals(text.GetText(0, -1), value, StringComparison.Ordinal);
            }
            catch (COMException) { return false; }
        }

        internal bool IsPlainHtmlInput(string value, bool focused)
        {
            var identity = Query<IAccessible2>(editor);
            if (identity == null || identity.GetUniqueId() != spans[0].Id ||
                identity.GetWindowHandle() != spans[0].Window ||
                (identity.GetStates() & (Editable | DefunctOrStale)) != Editable) return false;
            object state = editor.get_accState(Self);
            int expected = focused ? Focused : 0;
            if (!(state is int) || ((int)state & (Blocked | Focused)) != expected ||
                !string.Equals(ReadValue(), value, StringComparison.Ordinal)) return false;
            return HasPlainInputTag(identity.GetAttributes());
        }

        private static bool HasPlainInputTag(string attributes)
        {
            if (attributes == null) return false;
            string tag = null;
            int start = 0;
            for (int i = 0; i <= attributes.Length; i++)
            {
                if (i < attributes.Length && attributes[i] != ';')
                {
                    if (attributes[i] == '\\') i++;
                    continue;
                }
                if (i - start >= 4 && string.CompareOrdinal(attributes, start, "tag:", 0, 4) == 0)
                {
                    if (tag != null) return false;
                    tag = attributes.Substring(start + 4, i - start - 4);
                }
                start = i + 1;
            }
            return tag == "input" || tag == "textarea";
        }

        internal bool RestorePlainHtmlInput(Target target, string expected, string original, Task deadline)
        {
            IntPtr editorWindow = spans[0].Window;
            if (!target.IsAlive || Native.RootWindow(editorWindow) != target.Window ||
                Native.ProcessId(editorWindow) != target.ProcessId || !IsPlainHtmlInput(expected, false) ||
                !string.Equals(ReadValue(), expected, StringComparison.Ordinal) || deadline.IsCompleted || !target.IsAlive)
                return false;
            // The original HTML input still contains exactly the text plus our delimiter.
            // Set its value directly because keyboard input now belongs to a different element.
            editor.set_accValue(Self, original);
            return true;
        }

        internal async Task<bool> SelectAndApply(Target target, string expected, int start, int end,
            bool requireOriginalSelection, Task deadline, Func<bool> edit)
        {
            if (!IsFocusedWritable(target)) return false;
            TextSnapshot snapshot = ReadDirectSnapshot(requireOriginalSelection);
            if (snapshot == null || !string.Equals(snapshot.Value.ToString(), expected, StringComparison.Ordinal) ||
                requireOriginalSelection && (snapshot.Start != DirectStart || snapshot.End != DirectEnd ||
                    snapshot.SelectedLength != Text.Length || !HasOriginalSelection())) return false;
            TextRun run = snapshot.RunAt(start, requireOriginalSelection);
            if (run == null || end > run.End) return false;
            int localStart = run.Offset + start - run.Start, localEnd = run.Offset + end - run.Start;
            var text = run.Provider;
            int selectionCount = text.GetSelectionCount();
            if (selectionCount < 0 || selectionCount > 1) return false;
            if (deadline.IsCompleted || !target.IsCurrent || Native.ModifiersDown) return false;
            if (selectionCount != 0) text.SetSelection(0, localStart, localEnd);
            else if (start == end) text.SetCaretOffset(localStart);
            else text.AddSelection(localStart, localEnd);
            while (!deadline.IsCompleted && target.IsCurrent && !Native.ModifiersDown)
            {
                if (MatchesSelection(text, localStart, localEnd) && SameTextRun(expected, start, requireOriginalSelection, run) &&
                    MatchesSelection(text, localStart, localEnd) && IsFocusedWritable(target) &&
                    !deadline.IsCompleted && target.IsCurrent && !Native.ModifiersDown)
                    return edit();
                await Task.Delay(15).ConfigureAwait(false);
            }
            return false;
        }

        private bool HasOriginalSelection()
        {
            var currentSpans = new List<Span>();
            var currentText = new StringBuilder();
            return ReadRange(editor, Query<IAccessible2>(editor), currentSpans, currentText, 0) &&
                string.Equals(currentText.ToString(), Text, StringComparison.Ordinal) && SameRange(currentSpans);
        }

        private bool SameTextRun(string expected, int offset, bool previous, TextRun original)
        {
            TextSnapshot current = ReadDirectSnapshot(false);
            if (current == null || !string.Equals(current.Value.ToString(), expected, StringComparison.Ordinal)) return false;
            TextRun run = current.RunAt(offset, previous);
            return run != null && run.Node.Id == original.Node.Id && run.Node.Window == original.Node.Window &&
                run.Start == original.Start && run.End == original.End && run.Offset == original.Offset;
        }

        private bool IsFocusedWritable(Target target)
        {
            if (!target.IsCurrent || Native.ModifiersDown) return false;
            object state = editor.get_accState(Self);
            return state is int && ((int)state & (Focused | Blocked)) == Focused;
        }

        private static bool MatchesSelection(IAccessibleText text, int start, int end)
        {
            int count = text.GetSelectionCount();
            if (count == 0) return start == end && text.GetCaretOffset() == start;
            int actualStart, actualEnd;
            // The renderer can collapse the selection between these two COM calls.
            return count == 1 && text.GetSelection(0, out actualStart, out actualEnd) == 0 &&
                actualStart == start && actualEnd == end;
        }

        internal static AccessibleSelection Read(Target target)
        {
            if (!target.IsCurrent) return null;
            try
            {
                var selection = Read(target.Focus);
                if (selection == null && target.Focus != target.Window && target.IsCurrent)
                    selection = Read(target.Window);
                if (selection == null && target.IsCurrent)
                    selection = ReadRendererChildren(target);
                return target.IsCurrent ? selection : null;
            }
            catch (COMException) { return null; }
            catch (InvalidCastException) { return null; }
            catch (ArgumentException) { return null; }
        }

        private static AccessibleSelection ReadRendererChildren(Target target)
        {
            // Some Chromium hosts expose accessibility only on the renderer's child HWND.
            var windows = new List<IntPtr>();
            EnumChildWindows(target.Window, (window, unused) =>
            {
                if (window != target.Focus && Native.ProcessId(window) == target.ProcessId && IsWindowVisible(window) &&
                    Native.ClassName(window) == "Chrome_RenderWidgetHostHWND")
                    windows.Add(window);
                return windows.Count <= RendererLimit;
            }, IntPtr.Zero);
            if (windows.Count > RendererLimit) return null;

            AccessibleSelection verified = null;
            foreach (IntPtr window in windows)
            {
                if (!target.IsCurrent) return null;
                AccessibleSelection candidate = Read(window);
                if (candidate == null) continue;
                if (verified != null && !verified.SameRange(candidate)) return null;
                verified = candidate;
            }
            return verified;
        }

        internal bool SameRange(AccessibleSelection other)
        {
            return SameRange(other.spans);
        }

        private bool SameRange(List<Span> other)
        {
            if (spans.Count != other.Count) return false;
            for (int i = 0; i < spans.Count; i++)
            {
                Span left = spans[i], right = other[i];
                if (left.Window != right.Window || left.Id != right.Id || left.Start != right.Start || left.End != right.End)
                    return false;
            }
            return true;
        }

        private static AccessibleSelection Read(IntPtr window)
        {
            Guid iid = typeof(IAccessible).GUID;
            IAccessible node;
            if (AccessibleObjectFromWindow(window, ClientObject, ref iid, out node) != 0) return null;
            for (int depth = 0; depth < 8 && node != null; depth++)
            {
                object state = node.get_accState(Self);
                if (state is int && ((int)state & (Focused | Blocked)) == Focused)
                {
                    var editor = Query<IAccessible2>(node);
                    if (editor != null && (editor.GetStates() & (Editable | DefunctOrStale)) == Editable)
                    {
                        var spans = new List<Span>();
                        var text = new StringBuilder();
                        if (ReadRange(node, editor, spans, text, 0) && text.Length > 0)
                            return new AccessibleSelection(text.ToString(), spans, node);
                        return null;
                    }
                }
                object focus = node.accFocus;
                IAccessible next = focus as IAccessible;
                if (focus is int && (int)focus > Self) next = node.get_accChild(focus) as IAccessible;
                if (ReferenceEquals(next, node)) break;
                node = next;
            }
            return null;
        }

        private static bool ReadRange(IAccessible node, IAccessible2 identity, List<Span> spans, StringBuilder text, int depth)
        {
            if (depth == 8 || spans.Count == 64 || identity == null) return false;
            object state = node.get_accState(Self);
            if (!(state is int) || ((int)state & Blocked) != 0) return false;
            var selection = Query<IAccessibleText>(node);
            if (selection == null || selection.GetSelectionCount() != 1) return false;
            int start, end;
            if (selection.GetSelection(0, out start, out end) != 0 ||
                start < 0 || end <= start || end - start > MaxText) return false;
            string part = selection.GetText(start, end);
            if (string.IsNullOrEmpty(part) || part.Length > MaxText - text.Length) return false;
            int id = identity.GetUniqueId();
            IntPtr window = identity.GetWindowHandle();
            if (id == 0 || window == IntPtr.Zero) return false;
            spans.Add(new Span { Window = window, Id = id, Start = start, End = end });

            int position = 0;
            int embedded = part.IndexOf(EmbeddedObject);
            if (embedded < 0)
            {
                text.Append(part);
                return true;
            }

            // IA2 represents a rich-text child as one character in its parent.
            // Read that child's own selected offsets, which can cover only part of its text.
            var hypertext = selection as IAccessibleHypertext;
            if (hypertext == null || part.Length != end - start) return false;
            while (embedded >= 0)
            {
                if (embedded - position > MaxText - text.Length) return false;
                text.Append(part, position, embedded - position);
                int index = hypertext.GetHyperlinkIndex(start + embedded);
                IAccessible child = index >= 0 ? hypertext.GetHyperlink(index) as IAccessible : null;
                if (child == null || !ReadRange(child, Query<IAccessible2>(child), spans, text, depth + 1)) return false;
                position = embedded + 1;
                embedded = part.IndexOf(EmbeddedObject, position);
            }
            if (part.Length - position > MaxText - text.Length) return false;
            text.Append(part, position, part.Length - position);
            return true;
        }

        private static T Query<T>(IAccessible node) where T : class
        {
            var provider = node as IServiceProvider;
            if (provider == null) return null;
            Guid service = typeof(IAccessible).GUID;
            Guid iid = typeof(T).GUID;
            IntPtr pointer;
            if (provider.QueryService(ref service, ref iid, out pointer) != 0 || pointer == IntPtr.Zero)
                return null;
            try { return Marshal.GetObjectForIUnknown(pointer) as T; }
            finally { Marshal.Release(pointer); }
        }

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);

        private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, WindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider
        {
            [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out IntPtr result);
        }

        [ComImport, Guid("E89F726E-C4F4-4C19-BB19-B647D7FA8478"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAccessible2
        {
            // IDispatch and IAccessible contribute 25 slots before the first IA2 method.
            void _VtblGap1_32();
            int GetStates();
            void _VtblGap2_5();
            int GetUniqueId();
            IntPtr GetWindowHandle();
            void _VtblGap3_2();
            [return: MarshalAs(UnmanagedType.BStr)] string GetAttributes();
        }

        [ComImport, Guid("24FD2FFB-3AAD-4A08-8335-A3AD89C0FB4B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAccessibleText
        {
            void AddSelection(int start, int end);
            void UnusedGetAttributes();
            int GetCaretOffset();
            void UnusedGetCharacterExtents();
            int GetSelectionCount();
            void UnusedGetOffsetAtPoint();
            [PreserveSig] int GetSelection(int index, out int start, out int end);
            [return: MarshalAs(UnmanagedType.BStr)] string GetText(int start, int end);
            void _VtblGap1_4();
            void SetCaretOffset(int offset);
            void SetSelection(int index, int start, int end);
            int GetCharacterCount();
        }

        // The first 19 slots are inherited from IAccessibleText.
        [ComImport, Guid("6B4F8BBF-F1F2-418A-B35E-A195BC4103B9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAccessibleHypertext
        {
            void _VtblGap1_20();
            [return: MarshalAs(UnmanagedType.IUnknown)] object GetHyperlink(int index);
            int GetHyperlinkIndex(int offset);
        }
    }
}
