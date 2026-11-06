using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
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
            if (spans.Count != other.spans.Count) return false;
            for (int i = 0; i < spans.Count; i++)
            {
                Span left = spans[i], right = other.spans[i];
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
            int start;
            int end = selection.GetSelection(0, out start);
            if (start < 0 || end <= start || end - start > MaxText) return false;
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
        }

        [ComImport, Guid("24FD2FFB-3AAD-4A08-8335-A3AD89C0FB4B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAccessibleText
        {
            void UnusedAddSelection();
            void UnusedGetAttributes();
            void UnusedGetCaretOffset();
            void UnusedGetCharacterExtents();
            int GetSelectionCount();
            void UnusedGetOffsetAtPoint();
            int GetSelection(int index, out int start);
            [return: MarshalAs(UnmanagedType.BStr)] string GetText(int start, int end);
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
