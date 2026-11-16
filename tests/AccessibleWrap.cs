using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

internal static class AccessibleWrap
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();

    [MTAThread]
    private static int Main(string[] args)
    {
        try
        {
            bool rollback = args[2] == "rollback";
            using (Process browser = Process.GetProcessById(int.Parse(args[1])))
            {
                string expectedTitle = "IA2 " + args[2] + " selected - Google Chrome";
                if (browser.MainWindowTitle != expectedTitle || browser.MainWindowHandle == IntPtr.Zero ||
                    GetForegroundWindow() != browser.MainWindowHandle)
                    throw new InvalidOperationException("The isolated rich editor does not own focus.");

                Assembly artifact = Assembly.LoadFile(args[0]);
                Type targetType = artifact.GetType("PairTap.Target", true);
                object target = Activator.CreateInstance(targetType, true);
                if ((IntPtr)targetType.GetField("Window", Instance).GetValue(target) != browser.MainWindowHandle)
                    throw new InvalidOperationException("The native target changed before the IA2 test.");
                Type accessibleType = artifact.GetType("PairTap.AccessibleSelection", true);
                var read = accessibleType.GetMethod("Read", Static, null, new[] { targetType }, null);
                object accessible = read.Invoke(null, new[] { target });
                if (accessible == null || (string)accessibleType.GetField("Text", Instance).GetValue(accessible) != "中\U0001F642Ab")
                    throw new InvalidOperationException("The production IA2 provider did not find the exact rich selection.");

                Type wrap = artifact.GetType("PairTap.Wrap", true);
                Type selectionType = wrap.GetNestedType("Selection", BindingFlags.NonPublic);
                object selection = Activator.CreateInstance(selectionType, true);
                selectionType.GetField("Text", Instance).SetValue(selection, "中\U0001F642Ab");
                selectionType.GetField("Accessible", Instance).SetValue(selection, accessible);
                selectionType.GetField("VerifiedWritable", Instance).SetValue(selection, true);
                selectionType.GetField("OriginalValue", Instance).SetValue(selection,
                    accessibleType.GetMethod("ReadValue", Instance).Invoke(accessible, null));
                Type pairType = artifact.GetType("PairTap.Pair", true);
                object pair = Activator.CreateInstance(pairType, Instance, null, new object[] { "IA2 fixture", "《", "》" }, null);
                uint clipboard = GetClipboardSequenceNumber();
                var operation = (Task<string>)wrap.GetMethod("TypeSelection", Static).Invoke(null, new[] { target, pair, selection });
                string result = operation.GetAwaiter().GetResult();
                if ((rollback ? result == null || !result.Contains("已撤回右侧标点") : result != null) ||
                    GetClipboardSequenceNumber() != clipboard)
                    throw new InvalidOperationException("Unexpected native IA2 result: " + (result ?? "ok"));
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetBaseException().Message);
            return 1;
        }
    }
}
