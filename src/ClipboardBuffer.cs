using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
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
        private static readonly uint ExcludeFromHistory = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");

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

        internal Task Capture() { return Task.Run((Action)CaptureCore); }

        private void CaptureCore()
        {
            if (!Open(IntPtr.Zero)) throw new InvalidOperationException("剪贴板正在使用，请重试。");
            try
            {
                uint format = 0;
                ulong bytes = 0;
                var name = new StringBuilder(256);
                while (true)
                {
                    SetLastError(0);
                    format = EnumClipboardFormats(format);
                    if (format == 0)
                    {
                        if (Marshal.GetLastWin32Error() != 0) throw new Win32Exception();
                        break;
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
                Original = ClipboardStamp.Current;
            }
            finally { CloseClipboard(); }
        }

        internal bool Restore(uint expected)
        {
            return RestoreIfCurrent(new ClipboardStamp(expected, GetClipboardOwner()), false) == RestoreResult.Restored;
        }

        internal RestoreResult RestoreIfCurrent(ClipboardStamp expected, bool ownsPaste)
        {
            if (!Matches(expected, ownsPaste)) return RestoreResult.Changed;
            if (!Open(owner)) return Matches(expected, ownsPaste) ? RestoreResult.Failed : RestoreResult.Changed;
            try
            {
                if (!Matches(expected, ownsPaste)) return RestoreResult.Changed;
                if (!EmptyClipboard()) return RestoreResult.Failed;
                bool restored = true;
                foreach (Entry entry in entries)
                {
                    if (SetClipboardData(entry.Format, entry.Handle) != IntPtr.Zero) entry.Handle = IntPtr.Zero;
                    else restored = false;
                }
                return restored ? RestoreResult.Restored : RestoreResult.Failed;
            }
            finally { CloseClipboard(); }
        }

        private bool Matches(ClipboardStamp expected, bool ownsPaste)
        {
            return ownsPaste ? GetClipboardOwner() == owner : expected.IsCurrent;
        }

        private static bool Open(IntPtr window)
        {
            for (int i = 0; i < 12; i++)
            {
                if (OpenClipboard(window)) return true;
                System.Threading.Thread.Sleep(15);
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

        internal static string ReadSelection(ClipboardStamp copied, Target target, out ClipboardStamp read)
        {
            read = copied;
            if (!Open(IntPtr.Zero)) return null;
            try
            {
                if (!copied.IsCurrent || copied.Owner == IntPtr.Zero || Native.ProcessId(copied.Owner) != target.ProcessId)
                    return null;
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

        internal bool StagePaste(ClipboardStamp expected, Target target)
        {
            IntPtr marker = Allocate(sizeof(int));
            try
            {
                if (!OpenClipboard(owner)) return false;
                try
                {
                    if (!expected.IsCurrent || !target.IsCurrent || Native.ModifiersDown || !EmptyClipboard()) return false;
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
