using System.IO;
using System.Text.Json;

namespace PrivateGalleryVault.Services;

public sealed class VideoPlayerPreferences
{
    public double LastVolume { get; set; } = 0.75;
    public bool IsMuted { get; set; } = false;
    public string RepeatMode { get; set; } = "none";
}

public static class VideoPlayerPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string PreferencesPath => Path.Combine(VaultPaths.VaultRoot, "video-player-preferences.json");

    public static bool HasSavedPreferences()
    {
        try
        {
            return File.Exists(PreferencesPath);
        }
        catch
        {
            return false;
        }
    }

    public static VideoPlayerPreferences Load()
    {
        try
        {
            if (!File.Exists(PreferencesPath))
                return new VideoPlayerPreferences();

            var prefs = JsonSerializer.Deserialize<VideoPlayerPreferences>(File.ReadAllText(PreferencesPath));
            if (prefs == null)
                return new VideoPlayerPreferences();

            if (double.IsNaN(prefs.LastVolume) || double.IsInfinity(prefs.LastVolume))
                prefs.LastVolume = 0.75;

            prefs.LastVolume = Math.Clamp(prefs.LastVolume, 0.0, 1.0);
            if (string.IsNullOrWhiteSpace(prefs.RepeatMode))
                prefs.RepeatMode = "none";
            return prefs;
        }
        catch
        {
            return new VideoPlayerPreferences();
        }
    }

    public static void Save(double lastVolume, bool isMuted, string repeatMode)
    {
        try
        {
            VaultPaths.EnsureBaseDirectories();

            var prefs = new VideoPlayerPreferences
            {
                LastVolume = Math.Clamp(lastVolume, 0.0, 1.0),
                IsMuted = isMuted,
                RepeatMode = string.IsNullOrWhiteSpace(repeatMode) ? "none" : repeatMode.Trim().ToLowerInvariant()
            };

            File.WriteAllText(PreferencesPath, JsonSerializer.Serialize(prefs, JsonOptions));
        }
        catch
        {
            // Video playback must never fail just because preference saving failed.
        }
    }
}
