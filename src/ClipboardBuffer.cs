using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PairTap
{
    internal struct ClipboardStamp
    {
        internal readonly uint Sequence;
        internal readonly IntPtr Owner;

        internal ClipboardStamp(uint sequence, IntPtr owner) { Sequence = sequence; Owner = owner; }

        internal static ClipboardStamp Current
        {
            get { return new ClipboardStamp(ClipboardBuffer.GetClipboardSequenceNumber(), ClipboardBuffer.GetClipboardOwner()); }
        }

        internal bool IsCurrent
        {
            get { return Sequence == ClipboardBuffer.GetClipboardSequenceNumber() && Owner == ClipboardBuffer.GetClipboardOwner(); }
        }
    }

    internal enum RestoreResult { Restored, Changed, Failed }

    internal sealed class ClipboardBuffer : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] private static extern bool CloseClipboard();
        [DllImport("user32.dll")] private static extern bool EmptyClipboard();
        [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
        [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint EnumClipboardFormats(uint format);
        [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr handle);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClipboardFormatName(uint format, StringBuilder name, int count);
        [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] internal static extern IntPtr GetClipboardOwner();
        [DllImport("ole32.dll")] private static extern IntPtr OleDuplicateData(IntPtr source, uint format, uint flags);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern void SetLastError(uint error);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
        [DllImport("gdi32.dll")] private static extern bool DeleteEnhMetaFile(IntPtr handle);
        [DllImport("gdi32.dll")] private static extern bool DeleteMetaFile(IntPtr handle);
        [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetBitmapInfo(IntPtr bitmap, int bytes, out BitmapInfo info);
        [DllImport("gdi32.dll")] private static extern uint GetEnhMetaFileBits(IntPtr metafile, uint bytes, IntPtr data);
        [DllImport("gdi32.dll")] private static extern uint GetMetaFileBitsEx(IntPtr metafile, uint bytes, IntPtr data);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CopyEnhMetaFile(IntPtr metafile, string path);
        [DllImport("gdi32.dll")] private static extern uint GetPaletteEntries(IntPtr palette, uint start, uint count, IntPtr entries);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);

        internal const uint UnicodeFormat = 13;
        private const int TextLimit = 2 * 1024 * 1024;
        private const ulong SnapshotLimit = 128 * 1024 * 1024;
        private static readonly int[] RestoreDelays = { 0, 25, 75, 150 };
        private static readonly uint ExcludeFromHistory = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
        private static int readRunning;

        private readonly List<Entry> entries = new List<Entry>();
        private readonly IntPtr owner;
        internal ClipboardStamp Original { get; private set; }

        private sealed class Entry
        {
            internal uint Format;
            internal IntPtr Handle;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            internal int Type, Width, Height, Stride;
            internal ushort Planes, BitsPerPixel;
            internal IntPtr Bits;
        }

        internal ClipboardBuffer(IntPtr owner) { this.owner = owner; }

        internal Task Capture() { return Task.Run(() => CaptureCore(null)); }

        internal static Task<ClipboardBuffer> CaptureForWrap(IntPtr owner)
        {
            var backup = new ClipboardBuffer(owner);
            return CaptureBounded(backup, backup.CaptureCore);
        }

        private static Task<ClipboardBuffer> CaptureBounded(ClipboardBuffer backup, Action<Task> read)
        {
            return ReadBounded(deadline =>
            {
                try { read(deadline); return backup; }
                catch { backup.Dispose(); throw; }
            }, lateBackup => lateBackup.Dispose());
        }

        private static async Task<T> ReadBounded<T>(Func<Task, T> read, Action<T> discard)
        {
            if (Interlocked.CompareExchange(ref readRunning, 1, 0) != 0)
                throw new InvalidOperationException("上一次剪贴板读取尚未结束。");
            Task deadline = Task.Delay(1000);
            Task<T> work = Task.Run(() => read(deadline));
            if (await Task.WhenAny(work, deadline).ConfigureAwait(false) != work)
            {
                ReleaseAfterRead(work, discard);
                throw new InvalidOperationException("剪贴板内容读取超时。");
            }
            try { return await work.ConfigureAwait(false); }
            finally { Volatile.Write(ref readRunning, 0); }
        }

        private static void ReleaseAfterRead<T>(Task<T> work, Action<T> discard)
        {
            // Windows can still be rendering data after the caller's timeout.
            work.ContinueWith(completed =>
            {
                try
                {
                    if (completed.IsFaulted) Diagnostics.Failure("clipboard_read", completed.Exception.GetBaseException());
                    else if (completed.Status == TaskStatus.RanToCompletion && discard != null) discard(completed.Result);
                }
                finally { Volatile.Write(ref readRunning, 0); }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private void CaptureCore(Task deadline)
        {
            if (!Open(IntPtr.Zero)) throw new InvalidOperationException("剪贴板正在使用，请重试。");
            try
            {
                uint format = 0;
                ulong bytes = 0;
                var name = new StringBuilder(256);
                while (deadline == null || !deadline.IsCompleted)
                {
                    SetLastError(0);
                    format = EnumClipboardFormats(format);
                    if (format == 0)
                    {
                        if (Marshal.GetLastWin32Error() != 0) throw new Win32Exception();
                        Original = ClipboardStamp.Current;
                        return;
                    }

                    string formatName = format >= 0xC000 && GetClipboardFormatName(format, name, name.Capacity) > 0
                        ? name.ToString() : "";
                    if (formatName == "Ole Private Data" || formatName == "DataObject") continue;
                    if (format == 0x80 || (format >= 0x200 && format < 0x400) ||
                        formatName == "FileContents" || formatName == "FileGroupDescriptorW" ||
                        formatName == "FileGroupDescriptor")
                        throw new InvalidOperationException("剪贴板里有无法安全保留的内容。");

                    IntPtr source = GetClipboardData(format);
                    if (source == IntPtr.Zero) throw new InvalidOperationException("当前剪贴板内容无法安全保留。");
                    uint storage = StorageFormat(format);
                    ulong size = DataSize(source, storage);
                    if (size == 0 || size > SnapshotLimit - bytes)
                        throw new InvalidOperationException("剪贴板内容过大或无法安全保留。");
                    bytes += size;
                    if (entries.Count >= 128) throw new InvalidOperationException("剪贴板格式过多。");
                    IntPtr duplicate = storage == 14 ? CopyEnhMetaFile(source, null) : OleDuplicateData(source, storage, 0);
                    if (duplicate == IntPtr.Zero || duplicate == source)
                        throw new InvalidOperationException("当前剪贴板内容无法安全保留。");

                    entries.Add(new Entry { Format = format, Handle = duplicate });
                }
                throw new InvalidOperationException("剪贴板内容读取超时。");
            }
            finally { CloseClipboard(); }
        }

        internal bool Restore(uint expected)
        {
            return RestoreIfCurrent(new ClipboardStamp(expected, GetClipboardOwner()), false) == RestoreResult.Restored;
        }

        internal RestoreResult RestoreIfCurrent(ClipboardStamp expected, bool ownsPaste)
        {
            return RestoreIfCurrent(expected, ownsPaste, SetClipboardData);
        }

        private RestoreResult RestoreIfCurrent(ClipboardStamp expected, bool ownsPaste,
            Func<uint, IntPtr, IntPtr> publish)
        {
            if (!Matches(expected, ownsPaste)) return RestoreResult.Changed;
            if (!Open(owner, 40)) return Matches(expected, ownsPaste) ? RestoreResult.Failed : RestoreResult.Changed;
            try
            {
                if (!Matches(expected, ownsPaste)) return RestoreResult.Changed;
                if (!EmptyClipboard()) return RestoreResult.Failed;
                if (RestoreFormats(publish)) return RestoreResult.Restored;
            }
            finally { CloseClipboard(); }

            // Closing can synthesize a text format and advance the sequence. Only this
            // private owner can still publish into the partial restore; a new copy takes ownership.
            Thread.Sleep(150);
            if (GetClipboardOwner() != owner) return RestoreResult.Changed;
            if (!Open(owner)) return GetClipboardOwner() == owner ? RestoreResult.Failed : RestoreResult.Changed;
            try
            {
                if (GetClipboardOwner() != owner) return RestoreResult.Changed;
                return RestoreFormats(publish) ? RestoreResult.Restored : RestoreResult.Failed;
            }
            finally { CloseClipboard(); }
        }

        private bool RestoreFormats(Func<uint, IntPtr, IntPtr> publish)
        {
            // Keep the clipboard open during these short retries so a newer copy cannot be overwritten.
            foreach (int delay in RestoreDelays)
            {
                if (delay != 0) Thread.Sleep(delay);
                bool restored = true;
                foreach (Entry entry in entries)
                {
                    if (entry.Handle == IntPtr.Zero) continue;
                    if (publish(entry.Format, entry.Handle) != IntPtr.Zero) entry.Handle = IntPtr.Zero;
                    else restored = false;
                }
                if (restored) return true;
            }
            return false;
        }

        private bool Matches(ClipboardStamp expected, bool ownsPaste)
        {
            return ownsPaste ? GetClipboardOwner() == owner : expected.IsCurrent;
        }

        private static bool Open(IntPtr window, int attempts = 12)
        {
            for (int i = 0; i < attempts; i++)
            {
                if (OpenClipboard(window)) return true;
                Thread.Sleep(15);
            }
            return false;
        }

        private static uint StorageFormat(uint format)
        {
            return format == 0x82 || format == 0x83 || format == 0x8E ? format - 0x80 : format;
        }

        private static ulong DataSize(IntPtr source, uint format)
        {
            if (format == 2)
            {
                BitmapInfo bitmap;
                return GetBitmapInfo(source, Marshal.SizeOf(typeof(BitmapInfo)), out bitmap) > 0 &&
                    bitmap.Stride > 0 && bitmap.Height > 0
                    ? (ulong)bitmap.Stride * (ulong)bitmap.Height * Math.Max((ushort)1, bitmap.Planes) : 0;
            }
            if (format == 9) return GetPaletteEntries(source, 0, 0, IntPtr.Zero) * 4UL;
            if (format == 14) return GetEnhMetaFileBits(source, 0, IntPtr.Zero);
            if (format != 3) return GlobalSize(source).ToUInt64();
            int offset = IntPtr.Size == 8 ? 16 : 12;
            if (GlobalSize(source).ToUInt64() < (ulong)(offset + IntPtr.Size)) return 0;
            IntPtr picture = GlobalLock(source);
            if (picture == IntPtr.Zero) return 0;
            try { return GetMetaFileBitsEx(Marshal.ReadIntPtr(picture, offset), 0, IntPtr.Zero); }
            finally { GlobalUnlock(source); }
        }

        internal static string ReadSelection(ClipboardStamp copied, bool allowSameOwnerUpdate, out ClipboardStamp read)
        {
            return ReadSelectionCore(copied, allowSameOwnerUpdate, null, out read);
        }

        private static string ReadSelectionCore(ClipboardStamp copied, bool allowSameOwnerUpdate, Task deadline, out ClipboardStamp read)
        {
            read = copied;
            if (deadline != null && deadline.IsCompleted || !Open(IntPtr.Zero)) return null;
            try
            {
                ClipboardStamp current = ClipboardStamp.Current;
                if (current.Owner != copied.Owner || (current.Sequence != copied.Sequence && !allowSameOwnerUpdate))
                    return null;
                if (deadline != null && deadline.IsCompleted) return null;
                IntPtr data = GetClipboardData(UnicodeFormat);
                read = ClipboardStamp.Current;
                if (data == IntPtr.Zero || read.Owner != copied.Owner) return null;
                ulong bytes = GlobalSize(data).ToUInt64();
                if (bytes < 2 || bytes > TextLimit * 2 + 2 || bytes % 2 != 0) return null;
                IntPtr address = GlobalLock(data);
                if (address == IntPtr.Zero) return null;
                try
                {
                    string text = Marshal.PtrToStringUni(address, (int)(bytes / 2));
                    int end = text.IndexOf('\0');
                    return end <= 0 ? null : text.Substring(0, end);
                }
                finally { GlobalUnlock(data); }
            }
            finally { CloseClipboard(); }
        }

        internal static Task<Tuple<string, ClipboardStamp>> ReadSelectionBounded(ClipboardStamp copied, bool allowSameOwnerUpdate)
        {
            return ReadBounded(deadline =>
            {
                ClipboardStamp read;
                string text = ReadSelectionCore(copied, allowSameOwnerUpdate, deadline, out read);
                return Tuple.Create(text, read);
            }, null);
        }

        internal static Task<bool> PreparePaste(Target target, string text, ClipboardChannel channel)
        {
            IntPtr unicode = Allocate(checked((text.Length + 1) * 2));
            try
            {
                IntPtr address = GlobalLock(unicode);
                if (address == IntPtr.Zero) throw new InvalidOperationException("无法写入临时剪贴板。");
                try { Marshal.Copy(text.ToCharArray(), 0, address, text.Length); }
                finally { GlobalUnlock(unicode); }
                Task<bool> received = channel.PreparePaste(target, unicode);
                unicode = IntPtr.Zero;
                return received;
            }
            finally { Free(unicode); }
        }

        internal bool StagePaste(ClipboardStamp expected, Target target, out bool changed)
        {
            changed = false;
            IntPtr marker = Allocate(sizeof(int));
            try
            {
                if (!OpenClipboard(owner)) return false;
                try
                {
                    if (!expected.IsCurrent || !target.IsCurrent || Native.ModifiersDown || !EmptyClipboard()) return false;
                    changed = true;
                    if (ExcludeFromHistory == 0 || SetClipboardData(ExcludeFromHistory, marker) == IntPtr.Zero) return false;
                    marker = IntPtr.Zero;
                    SetClipboardData(UnicodeFormat, IntPtr.Zero);
                    return IsClipboardFormatAvailable(UnicodeFormat);
                }
                finally { CloseClipboard(); }
            }
            finally { Free(marker); }
        }

        private static IntPtr Allocate(int bytes)
        {
            IntPtr result = GlobalAlloc(0x42, (UIntPtr)bytes);
            if (result == IntPtr.Zero) throw new InvalidOperationException("无法分配剪贴板内存。");
            return result;
        }

        internal static void Free(IntPtr data) { if (data != IntPtr.Zero) GlobalFree(data); }

        internal static bool PublishText(IntPtr data) { return SetClipboardData(UnicodeFormat, data) != IntPtr.Zero; }

        public void Dispose()
        {
            foreach (Entry entry in entries)
            {
                if (entry.Handle == IntPtr.Zero) continue;
                switch (entry.Format)
                {
                    case 2:
                    case 9:
                    case 0x82:
                        DeleteObject(entry.Handle);
                        break;
                    case 14:
                    case 0x8E:
                        DeleteEnhMetaFile(entry.Handle);
                        break;
                    case 3:
                    case 0x83:
                        IntPtr picture = GlobalLock(entry.Handle);
                        if (picture != IntPtr.Zero)
                        {
                            DeleteMetaFile(Marshal.ReadIntPtr(picture, IntPtr.Size == 8 ? 16 : 12));
                            GlobalUnlock(entry.Handle);
                        }
                        GlobalFree(entry.Handle);
                        break;
                    default:
                        GlobalFree(entry.Handle);
                        break;
                }
            }
            entries.Clear();
        }

    }
}
