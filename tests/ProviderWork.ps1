param([Parameter(Mandatory)][string]$Artifact)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$temp = Join-Path $env:TEMP ('PairTap-provider-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
$source = Join-Path $temp 'ProbeFixture.cs'
$exe = Join-Path $temp 'ProbeFixture.exe'
@'
using System;
using System.Collections;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PairTap;

internal static class ProbeFixture
{
    private static int Main(string[] args)
    {
        var providers = new ProviderWork();
        var stalled = new TaskCompletionSource<int>();
        var first = providers.TryStart((IntPtr)101, 10, () => stalled.Task);
        if (first == null || first.Wait(100)) throw new InvalidOperationException("The first provider must still be blocked.");
        if (providers.TryStart((IntPtr)101, 10, () => Task.FromResult(0)) != null)
            throw new InvalidOperationException("Two operations ran against the blocked provider.");
        var healthy = providers.TryStart((IntPtr)202, 20, () => Task.FromResult(42));
        if (healthy == null || !healthy.Wait(1500) || healthy.Result != 42 || first.IsCompleted)
            throw new InvalidOperationException("The blocked provider prevented a different window from responding.");
        stalled.SetResult(1);
        if (!first.Wait(1500)) throw new InvalidOperationException("The first operation did not finish.");
        var retry = providers.TryStart((IntPtr)101, 10, () => Task.FromResult(2));
        if (retry == null || !retry.Wait(1500) || retry.Result != 2)
            throw new InvalidOperationException("The recovered provider stayed locked.");
        Console.WriteLine("PASS a stalled provider is isolated and its window can recover");
        CheckClipboardCapture(args[0]);
        CheckClipboardRestore(args[0]);
        CheckPlainInputIdentity(args[0]);
        CheckAccessibleOffsets(args[0]);
        CheckCopiedText(args[0]);
        CheckWordProcesses(args[0]);
        CheckWordInterruptedEdit(args[0]);
        return 0;
    }

    private static void CheckClipboardCapture(string artifact)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var buffer = Assembly.LoadFile(artifact).GetType("PairTap.ClipboardBuffer", true);
        var constructor = buffer.GetConstructor(instance, null, new[] { typeof(IntPtr) }, null);
        var bounded = buffer.GetMethod("CaptureBounded", BindingFlags.Static | BindingFlags.NonPublic);
        var boundedRead = buffer.GetMethod("ReadBounded", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(typeof(string));
        Func<Action<Task>, Task> capture = read => (Task)bounded.Invoke(null,
            new[] { constructor.Invoke(new object[] { IntPtr.Zero }), (object)read });
        Func<Func<Task, string>, Task<string>> readCopy = read =>
            (Task<string>)boundedRead.Invoke(null, new object[] { read, null });
        var entered = new TaskCompletionSource<bool>();
        var finish = new TaskCompletionSource<bool>();
        Task stalled = capture(deadline =>
        {
            entered.SetResult(true);
            if (!finish.Task.Wait(5000)) throw new TimeoutException("The test renderer was not released.");
        });
        try
        {
            if (!entered.Task.Wait(1500) || Task.WhenAny(stalled, Task.Delay(1700)).Result != stalled || !stalled.IsFaulted)
                throw new InvalidOperationException("A blocked clipboard read did not time out.");
            Task overlapping = capture(deadline => { throw new Exception("Unexpected overlapping clipboard read."); });
            if (!overlapping.IsFaulted || !(overlapping.Exception.GetBaseException() is InvalidOperationException))
                throw new InvalidOperationException("A second clipboard read was started while the first remained blocked.");
            Task overlappingCopy = readCopy(deadline => { throw new Exception("Unexpected clipboard selection read."); });
            if (!overlappingCopy.IsFaulted || !(overlappingCopy.Exception.GetBaseException() is InvalidOperationException))
                throw new InvalidOperationException("Reading the copied selection bypassed the occupied clipboard read slot.");
        }
        finally { finish.SetResult(true); }

        for (int i = 0; i < 30; i++)
        {
            Task retry = capture(deadline => { });
            if (!retry.IsFaulted && retry.Wait(1500))
            {
                var snapshot = (IDisposable)retry.GetType().GetProperty("Result").GetValue(retry);
                snapshot.Dispose();
                using (var releaseCopy = new ManualResetEventSlim())
                {
                    Task<string> stalledCopy = readCopy(deadline =>
                    {
                        if (!releaseCopy.Wait(5000)) throw new TimeoutException();
                        return "late copy";
                    });
                    try
                    {
                        if (Task.WhenAny(stalledCopy, Task.Delay(1700)).Result != stalledCopy || !stalledCopy.IsFaulted)
                            throw new InvalidOperationException("A blocked selection read did not time out.");
                        Task overlap = capture(deadline => { throw new Exception("Unexpected overlapping backup."); });
                        if (!overlap.IsFaulted || !(overlap.Exception.GetBaseException() is InvalidOperationException))
                            throw new InvalidOperationException("Backing up the clipboard bypassed the occupied selection read slot.");
                    }
                    finally { releaseCopy.Set(); }
                    for (int attempt = 0; attempt < 30; attempt++)
                    {
                        Task<string> copied = readCopy(deadline => "ready");
                        if (!copied.IsFaulted && copied.Wait(1500) && copied.Result == "ready")
                        {
                            Console.WriteLine("PASS backup and selection reads share a timeout and recover without overlap");
                            return;
                        }
                        Thread.Sleep(25);
                    }
                }
                throw new InvalidOperationException("The selection read slot stayed locked after the renderer returned.");
            }
            Thread.Sleep(25);
        }
        throw new InvalidOperationException("The clipboard read slot stayed locked after the renderer returned.");
    }

    private static void CheckClipboardRestore(string artifact)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var buffer = Assembly.LoadFile(artifact).GetType("PairTap.ClipboardBuffer", true);
        var constructor = buffer.GetConstructor(instance, null, new[] { typeof(IntPtr) }, null);
        var entry = buffer.GetNestedType("Entry", BindingFlags.NonPublic);
        var formatField = entry.GetField("Format", instance);
        var handleField = entry.GetField("Handle", instance);
        var restore = buffer.GetMethod("RestoreFormats", instance);
        foreach (bool recovers in new[] { true, false })
        {
            var snapshot = constructor.Invoke(new object[] { IntPtr.Zero });
            var entries = (IList)buffer.GetField("entries", instance).GetValue(snapshot);
            foreach (uint format in new uint[] { 13, 49152 })
            {
                var item = Activator.CreateInstance(entry, true);
                formatField.SetValue(item, format);
                handleField.SetValue(item, (IntPtr)format);
                entries.Add(item);
            }

            int plainAttempts = 0, richAttempts = 0;
            Func<uint, IntPtr, IntPtr> publish = (format, handle) =>
            {
                if (format == 13) { plainAttempts++; return handle; }
                return ++richAttempts == 4 && recovers ? handle : IntPtr.Zero;
            };
            try
            {
                bool restored = (bool)restore.Invoke(snapshot, new object[] { publish });
                if (restored != recovers || plainAttempts != 1 || richAttempts != 4 ||
                    (IntPtr)handleField.GetValue(entries[0]) != IntPtr.Zero ||
                    ((IntPtr)handleField.GetValue(entries[1]) == IntPtr.Zero) != recovers)
                    throw new InvalidOperationException("Clipboard restore lost track of published or failed formats.");
            }
            finally
            {
                entries.Clear(); // The fixture uses sentinel values instead of native memory handles.
                ((IDisposable)snapshot).Dispose();
            }
        }
        Console.WriteLine("PASS delayed clipboard restoration and bounded failure preserve published formats");
    }

    private static void CheckPlainInputIdentity(string artifact)
    {
        var selection = Assembly.LoadFile(artifact).GetType("PairTap.AccessibleSelection", true);
        var isPlain = selection.GetMethod("HasPlainInputTag", BindingFlags.Static | BindingFlags.NonPublic);
        Func<string, bool> matches = attributes => (bool)isPlain.Invoke(null, new object[] { attributes });
        if (!matches("tag:input;class:field;") || !matches("class:field;tag:textarea") ||
            matches(@"tag:div;id:fake\;tag:input\;;") || matches("tag:input;tag:div;") ||
            matches("tag:div;xml-roles:textbox;") || matches(null))
            throw new InvalidOperationException("A rich editor could be misidentified as a plain HTML field.");
        Console.WriteLine("PASS the detached rollback rejects escaped tags and rich HTML editors");
    }

    private static void CheckAccessibleOffsets(string artifact)
    {
        var selection = Assembly.LoadFile(artifact).GetType("PairTap.AccessibleSelection", true);
        var match = selection.GetMethod("MatchesDirectSelection", BindingFlags.Static | BindingFlags.NonPublic);
        Func<string, int, int, bool> matches = (value, start, end) =>
            (bool)match.Invoke(null, new object[] { "a\U0001F642a", value, start, end });
        if (!matches("a", 3, 4) || !matches("\U0001F642", 1, 3) || matches("a", 1, 2) ||
            matches("\uD83D", 1, 2) || matches("\uDE42", 2, 3) || matches("a", -1, 1))
            throw new InvalidOperationException("A direct edit was given an invalid or ambiguous boundary.");
        Console.WriteLine("PASS IA2 direct edits verify exact UTF-16 offsets and reject split surrogates");
    }

    private static void CheckCopiedText(string artifact)
    {
        var wrap = Assembly.LoadFile(artifact).GetType("PairTap.Wrap", true);
        var match = wrap.GetMethod("SameCopiedText", BindingFlags.Static | BindingFlags.NonPublic);
        Func<string, string, bool, bool> matches = (copy, selection, rich) =>
            (bool)match.Invoke(null, new object[] { copy, selection, rich });
        if (!matches("one\r\ntwo", "one\ntwo", false) || matches("one\r\n\r\ntwo", "one\ntwo", false) ||
            !matches("one\r\n\r\ntwo", "one\ntwo", true) || matches("new copy", "selection", true))
            throw new InvalidOperationException("The copy verification accepted different text or rejected valid rich paragraphs.");
        Console.WriteLine("PASS clipboard verification distinguishes changed plain text from rich paragraph breaks");
    }

    private static void CheckWordProcesses(string artifact)
    {
        var word = Assembly.LoadFile(artifact).GetType("PairTap.WordSelection", true);
        const BindingFlags methods = BindingFlags.Static | BindingFlags.NonPublic;
        var enter = word.GetMethod("TryEnter", methods);
        var leave = word.GetMethod("Leave", methods);
        Func<uint, bool> begin = process => (bool)enter.Invoke(null, new object[] { process });
        Action<uint> end = process => leave.Invoke(null, new object[] { process });
        if (!begin(101) || begin(101) || !begin(202))
            throw new InvalidOperationException("Word edits are not isolated by process.");
        end(202);
        if (!begin(202) || begin(101))
            throw new InvalidOperationException("A Word instance released a different instance's edit slot.");
        end(101);
        end(202);
        if (!begin(101)) throw new InvalidOperationException("A recovered Word instance stayed locked.");
        end(101);
        Console.WriteLine("PASS Word serializes edits within an instance and isolates other processes");
    }

    private static void CheckWordInterruptedEdit(string artifact)
    {
        var product = Assembly.LoadFile(artifact);
        var word = product.GetType("PairTap.WordSelection", true);
        var pair = Activator.CreateInstance(product.GetType("PairTap.Pair", true), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { "Word fixture", "(", ")" }, null);
        var recover = word.GetMethod("RecoverInterruptedEdit", BindingFlags.Static | BindingFlags.NonPublic);
        Action<string, int, int, int, bool, string, string> check = (text, leftEnd, rightStart, rightEnd, leftAttempted, expected, finalText) =>
        {
            var body = new StringBuilder(text);
            var original = new WordRangeFixture(body, 1, 4);
            var left = new WordRangeFixture(body, 1, leftEnd);
            var right = new WordRangeFixture(body, rightStart, rightEnd);
            string outcome = recover.Invoke(null, new object[] { original, left, right, 1, 4, "abc", pair, leftAttempted }).ToString();
            if (outcome != expected || body.ToString() != finalText)
                throw new InvalidOperationException("An interrupted Word edit had the wrong outcome: " + outcome);
        };
        check("xabc)y", 1, 4, 5, false, "Reverted", "xabcy");
        check("xabc)y", 1, 4, 5, true, "Reverted", "xabcy");
        check("x(abc)y", 2, 5, 6, true, "Completed", "x(abc)y");
        check("xabc)y", 1, 4, 4, false, "Unconfirmed", "xabc)y");
        check("xAbc)y", 1, 4, 5, false, "Unconfirmed", "xAbc)y");
        check("x(Abc)y", 2, 5, 6, true, "Unconfirmed", "x(Abc)y");
        Console.WriteLine("PASS Word recovery keeps a completed pair and removes only a verified partial insertion");
    }
}

public sealed class WordRangeFixture
{
    private readonly StringBuilder body;
    public int Start { get; private set; }
    public int End { get; private set; }
    public string Text { get { return body.ToString(Start, End - Start); } }
    public WordRangeFixture Duplicate { get { return new WordRangeFixture(body, Start, End); } }

    public WordRangeFixture(StringBuilder body, int start, int end)
    {
        this.body = body;
        SetRange(start, end);
    }

    public void SetRange(int start, int end) { Start = start; End = end; }

    public void Delete()
    {
        body.Remove(Start, End - Start);
        End = Start;
    }
}
'@ | Set-Content -LiteralPath $source -Encoding utf8
try {
    & "$framework\csc.exe" /nologo /warnaserror "/out:$exe" (Join-Path $root 'src\ProviderWork.cs') $source
    if ($LASTEXITCODE) { throw 'Provider fixture could not be compiled.' }
    & $exe (Resolve-Path -LiteralPath $Artifact).Path
    if ($LASTEXITCODE) { throw 'Provider checks failed.' }
}
finally {
    Remove-Item -LiteralPath $source, $exe -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temp -ErrorAction SilentlyContinue
}
