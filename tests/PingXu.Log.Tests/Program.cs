using System.Diagnostics;
using System.Text;
using PingXu.Core;

if (args.Length > 0 && args[0] == "--writer")
{
    while (!File.Exists(Path.Combine(args[1], "start"))) Thread.Sleep(10);
    for (int i = 0; i < 48; i++) Write(args[1], $"{args[2]}:{i:D3}:" + new string('x', 14000));
    return 0;
}

var tests = new (string Name, Action<string> Run)[]
{
    ("Append preserves exception details and repeated entries", d =>
    {
        for (int i = 0; i < 12; i++) Write(d, "repeat-中文😀");
        var lines = File.ReadAllLines(Path.Combine(d, "errors.log"));
        Check(lines.Length == 12 && lines.All(l => l.Contains("repeat-中文😀")), "Lost repeated entries");
        BoundedErrorLog.Append(d, new Exception("outer", new Exception("inner")));
        Check(File.ReadAllText(Path.Combine(d, "errors.log")).Contains("inner"), "Lost inner exception");
    }),
    ("Exact byte boundary rotates before overflow", d =>
    {
        File.WriteAllBytes(Path.Combine(d, "errors.log"), Enumerable.Repeat((byte)'a', 1048575).ToArray());
        Write(d, "next");
        Check(new FileInfo(Path.Combine(d, "errors.log.1")).Length == 1048575, "Previous file changed");
        Check(File.ReadAllText(Path.Combine(d, "errors.log")).Contains("next"), "New entry missing");
        Bounds(d);
    }),
    ("Oversized UTF8 records preserve scalars and mark truncation", d =>
    {
        foreach (string scalar in new[] { "😀", "中", "é" })
        {
            Write(d, string.Concat(Enumerable.Repeat(scalar, 1100000)));
            string result = StrictRead(Path.Combine(d, "errors.log"));
            Check(result.Contains("[truncated]") && result.EndsWith(Environment.NewLine), "Missing truncation marker or terminator");
            Check(!result.Contains('\uFFFD'), "Broken Unicode scalar");
            Check(Encoding.UTF8.GetByteCount(result) > 1048500, "Truncated excessively");
        }
        Bounds(d);
    }),
    ("Rotation retains exactly the latest three histories", d =>
    {
        for (int i = 0; i < 8; i++) Write(d, $"record-{i}:" + new string('x', 1100000));
        for (int i = 0; i < 4; i++)
            Check(StrictRead(Path.Combine(d, Name(i))).Contains($"record-{7-i}:"), "Wrong history order");
        Check(Directory.GetFiles(d).Length == 4, "Unexpected log files");
        Bounds(d);
    }),
    ("Threads rotate without missing or torn records", d =>
    {
        Parallel.For(0, 160, i => Write(d, $"thread:{i:D3}:" + new string('x', 14000)));
        Records(d, "thread", 160);
    }),
    ("Processes serialize append and rotation", d =>
    {
        var children = new List<Process>();
        try
        {
            for (int i = 0; i < 4; i++)
            {
                var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add(typeof(Program).Assembly.Location);
                foreach (string arg in new[] { "--writer", d, $"process{i}" }) start.ArgumentList.Add(arg);
                children.Add(Process.Start(start)!);
            }
            File.WriteAllText(Path.Combine(d, "start"), "go");
            foreach (var p in children) Check(p.WaitForExit(60000) && p.ExitCode == 0, "Child failed or timed out");
            for (int i = 0; i < 4; i++) Records(d, $"process{i}", 48);
        }
        finally
        {
            foreach (var p in children) { if (!p.HasExited) { p.Kill(true); p.WaitForExit(); } p.Dispose(); }
        }
    }),
    ("Only fixed log names are managed; transactions stay untouched", d =>
    {
        Directory.CreateDirectory(Path.Combine(d, "transactions"));
        var paths = new[] { "transactions/state.json", "transactions/errors.log", "errors.log.4", "errors.log.backup", "transactions.json" };
        foreach (var path in paths) File.WriteAllText(Path.Combine(d, path), "untouched-😀");
        var times = paths.Select(p => File.GetLastWriteTimeUtc(Path.Combine(d, p))).ToArray();
        for (int i = 0; i < 6; i++) Write(d, new string('x', 1100000));
        for (int i = 0; i < paths.Length; i++)
            Check(File.ReadAllText(Path.Combine(d, paths[i])) == "untouched-😀" && File.GetLastWriteTimeUtc(Path.Combine(d, paths[i])) == times[i], "Unrelated file modified");
        Check(File.Exists(Path.Combine(d, "errors.log.3")), "Rotation did not execute");
    }),
    ("IO and exception formatting failures never escape; later writes recover", d =>
    {
        var blocked = Path.Combine(d, "blocked");
        File.WriteAllText(blocked, "keep");
        Write(blocked, "cannot create directory");
        Write("\0", "invalid path");
        using (var locked = new FileStream(Path.Combine(d, "errors.log"), FileMode.Create, FileAccess.Write, FileShare.None)) Write(d, "locked");
        BoundedErrorLog.Append(d, new BrokenException());
        Write(d, "recovered");
        Check(StrictRead(Path.Combine(d, "errors.log")).Contains("recovered"), "Did not recover");
        Check(File.ReadAllText(blocked) == "keep", "Changed blocking file");
    }),
    ("Preexisting oversized log is bounded during rotation", d =>
    {
        File.WriteAllText(Path.Combine(d, "errors.log"), new string('a', 2100000));
        Write(d, "new-entry");
        Bounds(d);
        Check(StrictRead(Path.Combine(d, "errors.log")).Contains("new-entry"), "New entry missing");
    })
};

int failures = 0;
foreach (var test in tests)
{
    string directory = Path.Combine(Path.GetTempPath(), "PingXu.Log.Tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try { test.Run(directory); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
    finally { Directory.Delete(directory, true); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static void Write(string d, string message) => BoundedErrorLog.Append(d, new Exception(message));
static string Name(int i) => i == 0 ? "errors.log" : $"errors.log.{i}";
static string StrictRead(string path) => new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path));
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static void Bounds(string d)
{
    for (int i = 0; i < 4; i++)
    {
        string path = Path.Combine(d, Name(i));
        if (File.Exists(path)) { Check(new FileInfo(path).Length <= 1048576, "Log exceeds 1 MiB"); _ = StrictRead(path); }
    }
}
static void Records(string d, string prefix, int count)
{
    var lines = Enumerable.Range(0, 4).Select(i => Path.Combine(d, Name(i))).Where(File.Exists)
        .SelectMany(p => StrictRead(p).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        .Where(l => l.Contains(prefix + ":")).ToArray();
    Check(lines.Length == count, $"Expected {count} records, got {lines.Length}");
    for (int i = 0; i < count; i++) Check(lines.Count(l => l.EndsWith($"{prefix}:{i:D3}:" + new string('x', 14000))) == 1, "Missing, duplicated or torn record");
    Bounds(d);
}

sealed class BrokenException : Exception
{
    public override string ToString() => throw new InvalidOperationException("format failed");
}
