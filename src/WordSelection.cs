using System;
using System.Collections.Generic;
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
        private static readonly HashSet<uint> running = new HashSet<uint>();
        private enum InterruptedEdit { Unconfirmed, Completed, Reverted }

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid,
            [MarshalAs(UnmanagedType.IDispatch)] out object result);

        internal static async Task<string> Apply(Target target, Pair pair)
        {
            if (!TryEnter(target.ProcessId))
                return "Word 正在处理上一次操作，请稍后重试。";

            // Word may block in a COM call. Once the caller times out, the worker may only
            // finish an in-flight call and clean up its own insertion in the original document.
            int state = 0;
            var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var worker = new Thread(() =>
            {
                string message;
                try { message = Insert(target, pair, () => Interlocked.CompareExchange(ref state, 1, 0) == 0,
                    () => Volatile.Read(ref state) == 1); }
                catch (Exception error)
                {
                    Diagnostics.Failure("word", error);
                    message = "Word 无法处理当前选区，请重新选中文字。";
                }
                finally { Leave(target.ProcessId); }
                result.TrySetResult(message);
            }) { IsBackground = true };
            try
            {
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
            }
            catch
            {
                Leave(target.ProcessId);
                throw;
            }

            if (await Task.WhenAny(result.Task, Task.Delay(1500)) != result.Task)
            {
                int previous = Interlocked.Exchange(ref state, 2);
                return previous == 0 ? "Word 暂未响应，请稍后重试。" :
                    "Word 编辑响应超时，请先检查原文档。";
            }
            return await result.Task;
        }

        private static bool TryEnter(uint process)
        {
            // UndoRecord is shared by documents in a Word process, but a hung instance
            // must not hold a separate Word process's edit slot.
            lock (running) return running.Count < 8 && running.Add(process);
        }

        private static void Leave(uint process)
        {
            lock (running) running.Remove(process);
        }

        private static string Insert(Target target, Pair pair, Func<bool> beginEdit, Func<bool> canContinue)
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
            int markerLength = selected.EndsWith("\r\a", StringComparison.Ordinal) ? 2 :
                selected.EndsWith("\r", StringComparison.Ordinal) ? 1 : 0;
            int end = markerLength == 0 ? selectionEnd : selectionEnd - 1;
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
            bool rightAttempted = false, leftAttempted = false;
            try
            {
                if (!canContinue() || !target.IsCurrent || Native.ModifiersDown)
                    return "Word 输入焦点已变化，请重试。";
                rightAttempted = true;
                right.InsertBefore(pair.Right);
                if (!canContinue() || !target.IsCurrent || Native.ModifiersDown)
                    throw new InvalidOperationException("Word editing was interrupted between the two insertions.");
                leftAttempted = true;
                left.InsertBefore(pair.Left);
            }
            catch (Exception error)
            {
                if (!rightAttempted) throw;
                Diagnostics.Failure("word_insert", error);
                InterruptedEdit outcome;
                try
                {
                    outcome = RecoverInterruptedEdit(range, left, right, start, end,
                        selected.Substring(0, selected.Length - markerLength), pair, leftAttempted);
                }
                catch (Exception recoveryError)
                {
                    Diagnostics.Failure("word_rollback", recoveryError);
                    outcome = InterruptedEdit.Unconfirmed;
                }
                if (outcome == InterruptedEdit.Reverted) return "Word 无法完成两侧标点，已撤回右侧标点。";
                if (outcome != InterruptedEdit.Completed) return "Word 未能确认写入结果，请检查原文档。";
            }
            finally { undo.EndCustomRecord(); }

            if (canContinue() && target.IsCurrent)
            {
                // Word can shift range positions while tracking insertions.
                try
                {
                    right.Collapse(0);
                    if (canContinue() && target.IsCurrent) right.Select();
                }
                catch (COMException error) { Diagnostics.Failure("word_caret", error); }
            }
            return null;
        }

        private static InterruptedEdit RecoverInterruptedEdit(dynamic originalRange, dynamic left, dynamic right,
            int start, int end, string original, Pair pair, bool leftAttempted)
        {
            // The duplicated Word ranges expand on insertion. Require both the tracked
            // boundary and the unchanged surrounding selection before editing in recovery.
            dynamic probe = originalRange.Duplicate;
            int shiftedEnd = end + pair.Left.Length;
            if (leftAttempted && MatchesInsertedRange(left, start, pair.Left) &&
                MatchesInsertedRange(right, shiftedEnd, pair.Right))
            {
                probe.SetRange(start, shiftedEnd + pair.Right.Length);
                return string.Equals((string)probe.Text, pair.Left + original + pair.Right, StringComparison.Ordinal)
                    ? InterruptedEdit.Completed : InterruptedEdit.Unconfirmed;
            }

            probe.SetRange(start, end);
            if (!string.Equals((string)probe.Text, original, StringComparison.Ordinal) ||
                !MatchesInsertedRange(right, end, pair.Right)) return InterruptedEdit.Unconfirmed;
            right.Delete();
            return (int)right.Start == end && (int)right.End == end ?
                InterruptedEdit.Reverted : InterruptedEdit.Unconfirmed;
        }

        private static bool MatchesInsertedRange(dynamic range, int start, string delimiter)
        {
            return (int)range.Start == start && (int)range.End == start + delimiter.Length &&
                string.Equals((string)range.Text, delimiter, StringComparison.Ordinal);
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
