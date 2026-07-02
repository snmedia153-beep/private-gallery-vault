using System.IO;

namespace PrivateGalleryVault.Services;

public static class VaultPaths
{
    public static string AppRoot => AppContext.BaseDirectory;
    public static string VaultRoot => Path.Combine(AppRoot, "vault");
    public static string MasterFilePath => Path.Combine(VaultRoot, "master.json");
    public static string DatabasePath => Path.Combine(VaultRoot, "catalog.db");
    public static string SettingsPath => Path.Combine(VaultRoot, "settings.json");
    public static string ObjectsRoot => Path.Combine(VaultRoot, "objects");
    public static string ThumbsRoot => Path.Combine(VaultRoot, "thumbs");
    public static string ThumbSourcesRoot => Path.Combine(VaultRoot, "thumb_sources");

    public static void EnsureBaseDirectories()
    {
        Directory.CreateDirectory(VaultRoot);
        Directory.CreateDirectory(ObjectsRoot);
        Directory.CreateDirectory(ThumbsRoot);
        Directory.CreateDirectory(ThumbSourcesRoot);
    }

    public static string ToAbsoluteVaultPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Vault 상대 경로가 비어 있습니다.", nameof(relativePath));

        var normalized = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Trim();

        if (Path.IsPathRooted(normalized))
            throw new InvalidOperationException("Vault 외부 절대 경로는 사용할 수 없습니다.");

        var root = Path.GetFullPath(VaultRoot);
        var candidate = Path.GetFullPath(Path.Combine(root, normalized));

        if (!IsPathInsideDirectory(candidate, root))
            throw new InvalidOperationException("Vault 외부로 벗어나는 경로는 사용할 수 없습니다.");

        return candidate;
    }

    public static bool IsPathInsideVault(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            return IsPathInsideDirectory(Path.GetFullPath(path), Path.GetFullPath(VaultRoot));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPathInsideDirectory(string candidatePath, string rootPath)
    {
        var root = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = candidatePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return candidate.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
               || candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    public static string CreateObjectRelativePath(string id)
    {
        var safe = id.Replace("-", string.Empty);
        var prefix = safe[..2];
        return Path.Combine("objects", prefix, safe + ".bin");
    }

    public static string CreateThumbRelativePath(string id)
    {
        var safe = id.Replace("-", string.Empty);
        var prefix = safe[..2];
        return Path.Combine("thumbs", prefix, safe + ".bin");
    }

    public static string CreateThumbSourceRelativePath(string id)
    {
        var safe = id.Replace("-", string.Empty);
        var prefix = safe[..2];
        return Path.Combine("thumb_sources", prefix, safe + ".bin");
    }

    public static void EnsureParentDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
    }
}
