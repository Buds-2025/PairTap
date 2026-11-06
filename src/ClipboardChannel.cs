using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PairTap
{
    internal sealed class ClipboardChannel : NativeWindow, IDisposable
    {
        [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetOpenClipboardWindow();

        private TaskCompletionSource<bool> changed, read;
        private Target recipient;
        private IntPtr pendingText;
        private bool listening, dispatched;

        internal ClipboardChannel()
        {
            CreateHandle(new CreateParams { Caption = Product.Name + " Clipboard", Parent = (IntPtr)(-3) });
        }

        internal Task<bool> WatchCopy()
        {
            listening = AddClipboardFormatListener(Handle);
            changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!listening) changed.SetResult(false);
            return changed.Task;
        }

        internal void StopWatching()
        {
            if (listening) RemoveClipboardFormatListener(Handle);
            listening = false;
            changed = null;
        }

        internal Task<bool> PreparePaste(Target target, IntPtr text)
        {
            recipient = target;
            pendingText = text;
            read = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return read.Task;
        }

        internal void Dispatching() { dispatched = true; }
        internal bool TextPublished { get { return dispatched && pendingText == IntPtr.Zero; } }

        internal void EndPaste()
        {
            if (pendingText != IntPtr.Zero) ClipboardBuffer.Free(pendingText);
            pendingText = IntPtr.Zero;
            dispatched = false;
            recipient = null;
            read = null;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x031D && changed != null) changed.TrySetResult(true);
            if (message.Msg == 0x0305 && message.WParam.ToInt64() == ClipboardBuffer.UnicodeFormat &&
                dispatched && recipient != null && recipient.IsCurrent && pendingText != IntPtr.Zero)
            {
                uint reader = Native.ProcessId(GetOpenClipboardWindow());
                if ((reader == 0 || reader == recipient.ProcessId) && ClipboardBuffer.PublishText(pendingText))
                {
                    pendingText = IntPtr.Zero;
                    read.TrySetResult(reader != 0);
                }
                return;
            }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            StopWatching();
            EndPaste();
            DestroyHandle();
        }
    }
}
