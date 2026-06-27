using System.IO;
using System.Text.Json;
using PrivateGalleryVault.Models;

namespace PrivateGalleryVault.Services;

public static class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(VaultPaths.SettingsPath))
                return new AppSettings();

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(VaultPaths.SettingsPath)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        VaultPaths.EnsureBaseDirectories();
        File.WriteAllText(VaultPaths.SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
