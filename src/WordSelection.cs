using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PairTap
{
    internal static class WordSelection
    {
        private const uint NativeObjectModel = 0xFFFFFFF0;
        private const int InsertionPoint = 1, NormalSelection = 2, ColumnSelection = 4, RowSelection = 5, NoProtection = -1;
        private const int InFieldCode = 44;
        private static int running;

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid,
            [MarshalAs(UnmanagedType.IDispatch)] out object result);

        internal static async Task<string> Apply(Target target, Pair pair)
        {
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
                return "Word 正在处理上一次操作，请稍后重试。";

            // A timed-out probe cannot begin editing if Word responds later.
            // Once editing starts, wait for both insertions and the undo record to finish.
            int state = 0;
            var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var worker = new Thread(() =>
            {
                string message;
                try { message = Insert(target, pair, () => Interlocked.CompareExchange(ref state, 1, 0) == 0); }
                catch (Exception error)
                {
                    Diagnostics.Failure("word", error);
                    message = "Word 无法处理当前选区，请重新选中文字。";
                }
                finally { Interlocked.Exchange(ref running, 0); }
                result.TrySetResult(message);
            }) { IsBackground = true };
            try
            {
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
            }
            catch
            {
                Interlocked.Exchange(ref running, 0);
                throw;
            }

            if (await Task.WhenAny(result.Task, Task.Delay(1500)) != result.Task &&
                Interlocked.CompareExchange(ref state, 2, 0) == 0)
                return "Word 暂未响应，请稍后重试。";
            return await result.Task;
        }

        private static string Insert(Target target, Pair pair, Func<bool> beginEdit)
        {
            var dispatch = new Guid("00020400-0000-0000-C000-000000000046");
            object model;
            if (AccessibleObjectFromWindow(target.Focus, NativeObjectModel, ref dispatch, out model) != 0 || model == null)
                return "无法连接到当前 Word 文档。";

            dynamic window = model;
            dynamic selection = window.Selection;
            int type = selection.Type;
            if (type == InsertionPoint) return Wrap.NoSelection;
            if (type != NormalSelection &&
                ((type != ColumnSelection && type != RowSelection) || (int)selection.Cells.Count != 1))
                return "请在 Word 中选中一段连续文字。";

            dynamic document = window.Document;
            if ((bool)document.ReadOnly || (int)document.ProtectionType != NoProtection)
                return "当前 Word 文档为只读或受保护。";

            dynamic range = selection.Range;
            int start = range.Start, selectionEnd = range.End, story = range.StoryType;
            if (selectionEnd - start > 2 * 1024 * 1024) return "Word 选区过长，请分段处理。";
            string selected = range.Text;
            if (string.IsNullOrEmpty(selected)) return Wrap.NoSelection;

            // Word counts the two-character end-of-cell marker as one range position.
            int end = selected.EndsWith("\r\a", StringComparison.Ordinal) || selected.EndsWith("\r", StringComparison.Ordinal)
                ? selectionEnd - 1 : selectionEnd;
            if (start >= end) return Wrap.NoSelection;

            dynamic left = range.Duplicate;
            left.SetRange(start, start);
            dynamic right = range.Duplicate;
            right.SetRange(end, end);
            if ((bool)left.Information[InFieldCode] || (bool)right.Information[InFieldCode])
                return "Word 选区边界位于域代码中。";
            if (LockedControl(left) || LockedControl(right))
                return "Word 选区边界位于锁定的内容控件中。";

            dynamic undo = window.Application.UndoRecord;
            if ((bool)undo.IsRecordingCustomRecord)
                return "Word 正在执行另一项编辑，请稍后重试。";

            dynamic current = window.Selection;
            dynamic currentRange = current.Range;
            if ((int)current.Type != type || (int)currentRange.Start != start ||
                (int)currentRange.End != selectionEnd || (int)currentRange.StoryType != story ||
                !string.Equals((string)currentRange.Text, selected, StringComparison.Ordinal))
                return "Word 选区已变化，请重新选中文字。";
            if (!target.IsCurrent || Native.ModifiersDown || !beginEdit())
                return "Word 输入焦点已变化，请重试。";

            undo.StartCustomRecord("对符输入");
            bool rightInserted = false;
            try
            {
                if (!target.IsCurrent || Native.ModifiersDown)
                    return "Word 输入焦点已变化，请重试。";
                right.InsertBefore(pair.Right);
                rightInserted = true;
                left.InsertBefore(pair.Left);
            }
            catch
            {
                // Remove only the delimiter inserted by this operation if the other boundary rejected the edit.
                if (rightInserted && string.Equals((string)right.Text, pair.Right, StringComparison.Ordinal))
                    right.Delete();
                throw;
            }
            finally { undo.EndCustomRecord(); }

            if (target.IsCurrent)
            {
                // Word can shift range positions while tracking insertions.
                right.Collapse(0);
                right.Select();
            }
            return null;
        }

        private static bool LockedControl(dynamic boundary)
        {
            dynamic control = boundary.ParentContentControl;
            while (control != null)
            {
                if ((bool)control.LockContents) return true;
                control = control.ParentContentControl;
            }
            return false;
        }
    }
}
