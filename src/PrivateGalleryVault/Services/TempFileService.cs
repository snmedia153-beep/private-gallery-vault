using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace PrivateGalleryVault.Services;

public static class TempFileService
{
    private static readonly string BaseTempRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PrivateGalleryVault",
        "temp");

    private static readonly string SessionId = Guid.NewGuid().ToString("N");
    private static readonly object Gate = new();
    private static readonly HashSet<string> AclAppliedDirectories = new(StringComparer.OrdinalIgnoreCase);

    private static string PendingDeleteListPath => Path.Combine(BaseTempRoot, ".pending-delete.txt");
    public static string BaseTempDirectory => BaseTempRoot;
    public static string CurrentSessionRoot => Path.Combine(BaseTempRoot, SessionId);

    public static void CleanPreviousSessions()
    {
        EnsureSecureDirectory(BaseTempRoot);
        CleanPendingDeletes();

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(BaseTempRoot))
            {
                if (string.Equals(Path.GetFullPath(directory), Path.GetFullPath(CurrentSessionRoot), StringComparison.OrdinalIgnoreCase))
                    continue;

                TryDeleteDirectory(directory);
            }
        }
        catch
        {
            // 다음 실행 때 다시 정리합니다.
        }

        EnsureSecureDirectory(CurrentSessionRoot);
    }

    public static string CreateTempMediaPath(string mediaId, string extension)
    {
        EnsureSecureDirectory(CurrentSessionRoot);
        extension = NormalizeExtension(extension);
        return Path.Combine(CurrentSessionRoot, SanitizeFileNamePart(mediaId) + extension);
    }

    public static string CreateUniqueTempMediaPath(string mediaId, string extension)
    {
        EnsureSecureDirectory(CurrentSessionRoot);
        extension = NormalizeExtension(extension);
        return Path.Combine(CurrentSessionRoot, $"{SanitizeFileNamePart(mediaId)}_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{extension}");
    }

    public static string CreateAnonymousTempMediaPath(string mediaId, string extension, string suffix = "")
    {
        EnsureSecureDirectory(CurrentSessionRoot);
        extension = NormalizeExtension(extension);
        suffix = SanitizeFileNamePart(suffix);
        var id = SanitizeFileNamePart(mediaId);
        var shortId = id.Length <= 12 ? id : id[..12];
        var name = string.IsNullOrWhiteSpace(suffix) ? $"media_{shortId}" : $"media_{shortId}_{suffix}";
        return Path.Combine(CurrentSessionRoot, name + extension);
    }

    public static void TryDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsPathInsideTempRoot(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            RegisterPendingDelete(path);
        }
    }

    public static void CleanCurrentSession()
    {
        CleanPendingDeletes();
        TryDeleteDirectory(CurrentSessionRoot);
    }

    public static void CleanPendingDeletes()
    {
        EnsureSecureDirectory(BaseTempRoot);

        List<string> pending;
        lock (Gate)
        {
            if (!File.Exists(PendingDeleteListPath))
                return;

            pending = File.ReadAllLines(PendingDeleteListPath, Encoding.UTF8)
                .Where(IsPathInsideTempRoot)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var stillPending = new List<string>();
        foreach (var path in pending)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);

                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);

                if (File.Exists(path) || Directory.Exists(path))
                    stillPending.Add(path);
            }
            catch
            {
                stillPending.Add(path);
            }
        }

        lock (Gate)
        {
            if (stillPending.Count == 0)
            {
                try { File.Delete(PendingDeleteListPath); } catch { }
            }
            else
            {
                File.WriteAllLines(PendingDeleteListPath, stillPending, Encoding.UTF8);
            }
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !IsPathInsideTempRoot(directory))
            return;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);

                if (!Directory.Exists(directory))
                    return;
            }
            catch
            {
                Thread.Sleep(120);
            }
        }

        try
        {
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                    RegisterPendingDelete(file);
            }
        }
        catch
        {
        }
    }

    private static void RegisterPendingDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsPathInsideTempRoot(path))
            return;

        lock (Gate)
        {
            try
            {
                EnsureSecureDirectory(BaseTempRoot);
                File.AppendAllText(PendingDeleteListPath, Path.GetFullPath(path) + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }

    private static void EnsureSecureDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        Directory.CreateDirectory(directory);
        ApplyCurrentUserAclBestEffort(directory);
    }

    private static void ApplyCurrentUserAclBestEffort(string directory)
    {
        if (!OperatingSystem.IsWindows())
            return;

        lock (Gate)
        {
            var fullDirectory = Path.GetFullPath(directory);
            if (AclAppliedDirectories.Contains(fullDirectory) && Directory.Exists(fullDirectory))
                return;

            try
            {
                var currentUser = WindowsIdentity.GetCurrent().Name;
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo("icacls.exe")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                process.StartInfo.ArgumentList.Add(directory);
                process.StartInfo.ArgumentList.Add("/inheritance:r");
                process.StartInfo.ArgumentList.Add("/grant:r");
                process.StartInfo.ArgumentList.Add($"{currentUser}:(OI)(CI)F");
                process.StartInfo.ArgumentList.Add("/grant:r");
                process.StartInfo.ArgumentList.Add("*S-1-5-18:(OI)(CI)F");
                process.StartInfo.ArgumentList.Add("/grant:r");
                process.StartInfo.ArgumentList.Add("*S-1-5-32-544:(OI)(CI)F");
                process.StartInfo.ArgumentList.Add("/T");
                process.StartInfo.ArgumentList.Add("/C");
                process.Start();
                if (!process.WaitForExit(1500))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }
                AclAppliedDirectories.Add(fullDirectory);
            }
            catch
            {
                // ACL 강화 실패가 앱 실행 실패로 이어지지 않게 합니다.
            }
        }
    }

    private static bool IsPathInsideTempRoot(string path)
    {
        try
        {
            var root = Path.GetFullPath(BaseTempRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return candidate.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                   || candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeExtension(string extension)
    {
        extension = string.IsNullOrWhiteSpace(extension) ? ".tmp" : extension.Trim();
        if (!extension.StartsWith('.'))
            extension = "." + extension;

        var safe = new string(extension.Where(c => c == '.' || char.IsLetterOrDigit(c)).ToArray());
        if (string.IsNullOrWhiteSpace(safe) || safe == ".")
            return ".tmp";

        return safe.Length > 16 ? safe[..16] : safe;
    }

    private static string SanitizeFileNamePart(string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        value = new string(value.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.').ToArray());
        value = value.Trim('_', '-', '.', ' ');
        if (string.IsNullOrWhiteSpace(value))
            value = Guid.NewGuid().ToString("N");

        return value.Length > 64 ? value[..64] : value;
    }
}
