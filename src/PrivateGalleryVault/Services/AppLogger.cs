using System.Diagnostics;
using System.IO;
using System.Text;

namespace PrivateGalleryVault.Services;

public static class AppLogger
{
    private static readonly object Gate = new();
    private static bool _initialized;

    public static string LogDirectory { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PrivateGalleryVault",
        "logs");

    public static string CurrentLogPath => Path.Combine(LogDirectory, $"pgv-{DateTime.Now:yyyyMMdd}.log");

    public static string DiagnosticsDirectory => Path.Combine(LogDirectory, "diagnostics");

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized)
                return;

            Directory.CreateDirectory(LogDirectory);
            _initialized = true;

            Info("Logger initialized");
            Info($"Runtime={Environment.Version}; Process={Environment.ProcessId}; 64bitProcess={Environment.Is64BitProcess}");
        }
    }

    public static void CleanupOldLogs(int keepDays = 21)
    {
        TrySafe(() =>
        {
            Directory.CreateDirectory(LogDirectory);
            var threshold = DateTime.Now.AddDays(-Math.Max(1, keepDays));
            foreach (var file in Directory.EnumerateFiles(LogDirectory, "pgv-*.log"))
            {
                var info = new FileInfo(file);
                if (info.LastWriteTime < threshold)
                    info.Delete();
            }

            if (Directory.Exists(DiagnosticsDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(DiagnosticsDirectory, "*.txt"))
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTime < threshold)
                        info.Delete();
                }
            }
        });
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Warn(string message, Exception? exception) => Write("WARN", message, exception);
    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);
    public static void Fatal(string message, Exception? exception = null) => Write("FATAL", message, exception);

    public static void LogMediaOpenStart(string id, string kind, string name, long sizeBytes)
    {
        Info($"MediaOpenStart id={SanitizeId(id)}; kind={Sanitize(kind)}; size={sizeBytes}");
    }

    public static void LogMediaOpenEnd(string id, string kind, string name)
    {
        Info($"MediaOpenEnd id={SanitizeId(id)}; kind={Sanitize(kind)}");
    }

    public static string WriteDiagnosticFile(string fileNamePrefix, string contents)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticsDirectory);
            var prefix = SanitizeFileName(string.IsNullOrWhiteSpace(fileNamePrefix) ? "diagnostic" : fileNamePrefix);
            var path = Path.Combine(DiagnosticsDirectory, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
            File.WriteAllText(path, contents ?? string.Empty, Encoding.UTF8);
            Info($"Diagnostic file written: {Path.GetFileName(path)}");
            return path;
        }
        catch (Exception ex)
        {
            Error("Failed to write diagnostic file", ex);
            return CurrentLogPath;
        }
    }

    public static string WriteCrashReport(string title, Exception? exception = null, string? extra = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Private Gallery Vault Crash Report");
        sb.AppendLine("==================================");
        sb.AppendLine($"Time: {DateTime.Now:O}");
        sb.AppendLine($"Title: {Sanitize(title)}");
        sb.AppendLine($"ProcessId: {Environment.ProcessId}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($"Runtime: {Environment.Version}");
        sb.AppendLine($"64bitOS: {Environment.Is64BitOperatingSystem}");
        sb.AppendLine($"64bitProcess: {Environment.Is64BitProcess}");
        if (!string.IsNullOrWhiteSpace(extra))
        {
            sb.AppendLine();
            sb.AppendLine("Extra");
            sb.AppendLine("-----");
            sb.AppendLine(RedactPaths(extra));
        }
        if (exception != null)
        {
            sb.AppendLine();
            sb.AppendLine("Exception");
            sb.AppendLine("---------");
            sb.AppendLine(RedactPaths(exception.ToString()));
        }

        return WriteDiagnosticFile("crash-" + SanitizeFileName(title), sb.ToString());
    }

    public static string RedactPaths(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var redacted = value.Replace(AppContext.BaseDirectory, "[AppBase]", StringComparison.OrdinalIgnoreCase)
            .Replace(LogDirectory, "[LogDirectory]", StringComparison.OrdinalIgnoreCase)
            .Replace(VaultPaths.VaultRoot, "[VaultRoot]", StringComparison.OrdinalIgnoreCase)
            .Replace(TempFileService.BaseTempDirectory, "[TempDirectory]", StringComparison.OrdinalIgnoreCase);

        return redacted.Replace("\r", " ").Replace("\n", Environment.NewLine);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        TrySafe(() =>
        {
            Directory.CreateDirectory(LogDirectory);
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.Append(" [").Append(level).Append("]");
            sb.Append(" pid=").Append(Environment.ProcessId);
            sb.Append(" tid=").Append(Environment.CurrentManagedThreadId);
            sb.Append(" ").Append(RedactPaths(message));
            if (exception != null)
            {
                sb.AppendLine();
                sb.Append(RedactPaths(exception.ToString()));
            }
            sb.AppendLine();

            lock (Gate)
                File.AppendAllText(CurrentLogPath, sb.ToString(), Encoding.UTF8);
        });
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static string SanitizeId(string? value)
    {
        value = Sanitize(value);
        return value.Length <= 16 ? value : value[..16];
    }

    private static string SanitizeFileName(string? value)
    {
        value = Sanitize(value);
        if (string.IsNullOrWhiteSpace(value))
            return "diagnostic";

        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value.Length > 80 ? value[..80] : value;
    }

    private static void TrySafe(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            try { Debug.WriteLine("AppLogger write failed"); } catch { }
        }
    }
}
