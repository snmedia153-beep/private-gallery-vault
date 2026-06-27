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
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.Combine(VaultRoot, normalized);
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
