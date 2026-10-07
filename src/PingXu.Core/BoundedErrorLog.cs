using System.Text;

namespace PingXu.Core;

public static class BoundedErrorLog
{
    private const int MaximumBytes = 1024 * 1024;
    // One system-wide mutex also covers differently cased paths and directory aliases.
    // All participating processes must use this writer. No lock file is created.
    private const string MutexName = @"Global\PingXu.BoundedErrorLog.v1";
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    /// <summary>
    /// Best-effort append to errors.log (1 MiB), retaining errors.log.1 through .3.
    /// IO, formatting and synchronization failures drop this entry without throwing.
    /// </summary>
    public static void Append(string directory, Exception exception)
    {
        try
        {
            byte[] record = EncodeRecord($"{DateTimeOffset.Now:O} {exception}");
            string root = Path.GetFullPath(directory);
            using var mutex = new Mutex(false, MutexName);
            bool acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(2)); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) return;

                // Refuse redirects rather than following a log link into unrelated data.
                for (var ancestor = new DirectoryInfo(root); ancestor != null; ancestor = ancestor.Parent)
                    if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0) return;
                string[] paths = Enumerable.Range(0, 4)
                    .Select(i => Path.Combine(root, i == 0 ? "errors.log" : $"errors.log.{i}")).ToArray();
                foreach (string path in paths)
                {
                    try
                    {
                        if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) return;
                    }
                    catch (FileNotFoundException) { }
                    catch (DirectoryNotFoundException) { }
                }
                Directory.CreateDirectory(root);

                // Older versions appended without a limit. Bound only our four files.
                foreach (string path in paths)
                {
                    if (!File.Exists(path) || new FileInfo(path).Length <= MaximumBytes) continue;
                    var chars = new char[MaximumBytes];
                    int count;
                    using (var reader = new StreamReader(path, Utf8, detectEncodingFromByteOrderMarks: true))
                        count = reader.ReadBlock(chars, 0, chars.Length);
                    // ReadBlock may stop between UTF-16 surrogates; remove an incomplete pair.
                    if (count > 0 && char.IsHighSurrogate(chars[count - 1])) count--;
                    File.WriteAllBytes(path, EncodeRecord(new string(chars, 0, count), forceTruncation: true));
                }

                if (File.Exists(paths[0]) && new FileInfo(paths[0]).Length + record.Length > MaximumBytes)
                {
                    File.Delete(paths[3]);
                    for (int i = 2; i >= 0; i--)
                        if (File.Exists(paths[i])) File.Move(paths[i], paths[i + 1], overwrite: true);
                }
                using var stream = new FileStream(paths[0], FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(record);
            }
            finally { if (acquired) mutex.ReleaseMutex(); }
        }
        catch { /* Error reporting must never become another application failure. */ }
    }

    private static byte[] EncodeRecord(string text, bool forceTruncation = false)
    {
        string newline = Environment.NewLine;
        if (!forceTruncation && Utf8.GetByteCount(text) <= MaximumBytes - Utf8.GetByteCount(newline))
            return Utf8.GetBytes(text + newline);

        byte[] suffix = Utf8.GetBytes(" [truncated]" + newline);
        var buffer = new byte[MaximumBytes];
        // Encoder.Convert consumes whole Unicode scalars; it cannot split a UTF-8 sequence.
        Utf8.GetEncoder().Convert(text.AsSpan(), buffer.AsSpan(0, MaximumBytes - suffix.Length),
            flush: true, out _, out int written, out _);
        suffix.CopyTo(buffer, written);
        return buffer.AsSpan(0, written + suffix.Length).ToArray();
    }
}
