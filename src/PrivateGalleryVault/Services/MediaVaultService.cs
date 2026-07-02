using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using PrivateGalleryVault.Models;

namespace PrivateGalleryVault.Services;

public sealed class MediaVaultService
{
    private readonly byte[] _masterKey;
    private readonly DatabaseService _db;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".avi", ".mkv", ".wmv", ".webm"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".txt", ".md", ".rtf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".csv", ".json", ".xml", ".html", ".htm", ".log", ".ini", ".yaml", ".yml"
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".iso"
    };

    private static readonly HashSet<string> BlockedExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jse", ".scr", ".com", ".pif", ".lnk"
    };

    // Files in this list may be kept inside the encrypted vault, but they must not be
    // opened directly through Windows shell execution from the app. Users can still
    // export them intentionally and handle them outside the vault if needed.
    private static readonly HashSet<string> DefaultOpenBlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ade", ".adp", ".app", ".appref-ms", ".appx", ".appxbundle", ".application",
        ".bas", ".cab", ".chm", ".cpl", ".crt", ".diagcab", ".fxp", ".gadget",
        ".hta", ".inf", ".ins", ".isp", ".jar", ".jnlp", ".mad", ".maf",
        ".mag", ".mam", ".maq", ".mar", ".mas", ".mat", ".mau", ".mav",
        ".maw", ".mda", ".mdb", ".mde", ".mdt", ".mdw", ".mdz",
        ".msc", ".msix", ".msixbundle", ".msh", ".msh1", ".msh2",
        ".mshxml", ".msh1xml", ".msh2xml", ".ops", ".pcd", ".prf",
        ".psd1", ".psm1", ".reg", ".scf", ".sct", ".shb", ".shs",
        ".url", ".vb", ".vbe", ".ws", ".wsc", ".wsf", ".wsh", ".xbap", ".xll"
    };

    public MediaVaultService(byte[] masterKey, DatabaseService db)
    {
        _masterKey = masterKey;
        _db = db;
    }

    public static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(ext)) return false;
        if (BlockedExecutableExtensions.Contains(ext)) return false;
        return ImageExtensions.Contains(ext)
               || VideoExtensions.Contains(ext)
               || DocumentExtensions.Contains(ext)
               || ArchiveExtensions.Contains(ext)
               || !BlockedExecutableExtensions.Contains(ext);
    }

    public string ComputeSourceFingerprint(string sourcePath)
    {
        using var stream = File.OpenRead(sourcePath);
        using var hmac = new HMACSHA256(_masterKey);
        return Convert.ToHexString(hmac.ComputeHash(stream)).ToLowerInvariant();
    }

    public static MediaKind DetermineKind(string path)
    {
        var ext = Path.GetExtension(path);
        if (ImageExtensions.Contains(ext)) return MediaKind.Image;
        if (VideoExtensions.Contains(ext)) return MediaKind.Video;
        if (DocumentExtensions.Contains(ext)) return MediaKind.Document;
        if (ArchiveExtensions.Contains(ext)) return MediaKind.Archive;
        if (!BlockedExecutableExtensions.Contains(ext) && !string.IsNullOrWhiteSpace(ext)) return MediaKind.Other;
        throw new NotSupportedException("지원하지 않는 파일 형식입니다: " + ext);
    }

    public MediaItem ImportMedia(string sourcePath, string topicId, string? sourceFingerprint = null)
    {
        MediaItem? item = null;
        try
        {
            item = PrepareMediaImport(sourcePath, topicId, sourceFingerprint);
            _db.AddMedia(item);
            return item;
        }
        catch
        {
            CleanupPreparedImport(item);
            throw;
        }
    }

    public MediaItem PrepareMediaImport(string sourcePath, string topicId, string? sourceFingerprint = null)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("파일을 찾을 수 없습니다.", sourcePath);
        if (!IsSupported(sourcePath))
            throw new NotSupportedException("지원하지 않는 파일 형식입니다: " + sourcePath);

        var normalizedSourceFingerprint = string.IsNullOrWhiteSpace(sourceFingerprint)
            ? ComputeSourceFingerprint(sourcePath)
            : sourceFingerprint.Trim().ToLowerInvariant();

        var id = Guid.NewGuid().ToString("N");
        var kind = DetermineKind(sourcePath);
        var objectRel = VaultPaths.CreateObjectRelativePath(id);
        var thumbRel = VaultPaths.CreateThumbRelativePath(id);
        var objectAbs = VaultPaths.ToAbsoluteVaultPath(objectRel);
        var thumbAbs = VaultPaths.ToAbsoluteVaultPath(thumbRel);

        string thumbSourceRel = string.Empty;

        try
        {
            FileCryptoService.EncryptFile(_masterKey, sourcePath, objectAbs);

            var capturedAt = TimeSpan.Zero;
            byte[] thumb;
            try
            {
                if (kind == MediaKind.Video && ThumbnailService.TryCreateVideoThumbnailSet(sourcePath, 480, out var videoThumb, out var sourceFrame, out capturedAt))
                {
                    thumb = videoThumb;
                    thumbSourceRel = VaultPaths.CreateThumbSourceRelativePath(id);
                    FileCryptoService.EncryptBytesToFile(_masterKey, sourceFrame, VaultPaths.ToAbsoluteVaultPath(thumbSourceRel));
                }
                else
                {
                    thumb = ThumbnailService.CreateThumbnailBytes(sourcePath, kind);
                }
            }
            catch
            {
                // 파일 암호화는 성공할 수 있지만, 파일명/코덱/메타데이터 문제로 썸네일 생성만 실패하는 경우가 있습니다.
                // 이 경우 가져오기 자체를 실패시키지 않고 안전한 플레이스홀더 썸네일로 저장합니다.
                thumb = ThumbnailService.CreatePlaceholderBytes(kind, Path.GetExtension(sourcePath), 480);
                if (!string.IsNullOrWhiteSpace(thumbSourceRel))
                {
                    TryDeleteVaultFile(thumbSourceRel);
                    thumbSourceRel = string.Empty;
                }
            }
            FileCryptoService.EncryptBytesToFile(_masterKey, thumb, thumbAbs);

            var info = new FileInfo(sourcePath);
            var item = new MediaItem
            {
                Id = id,
                TopicId = topicId,
                Kind = kind,
                OriginalName = Path.GetFileName(sourcePath),
                Extension = Path.GetExtension(sourcePath),
                ObjectPath = objectRel.Replace('\\', '/'),
                ThumbPath = thumbRel.Replace('\\', '/'),
                ThumbSourcePath = thumbSourceRel.Replace('\\', '/'),
                SourceHash = normalizedSourceFingerprint,
                SizeBytes = info.Length,
                DurationSeconds = 0,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            if (kind == MediaKind.Image)
                TryFillImageDimensions(sourcePath, item);

            return item;
        }
        catch
        {
            TryDeleteVaultFile(objectRel);
            TryDeleteVaultFile(thumbRel);
            if (!string.IsNullOrWhiteSpace(thumbSourceRel))
                TryDeleteVaultFile(thumbSourceRel);
            throw;
        }
    }

    public BitmapImage LoadThumbnail(MediaItem item)
    {
        try
        {
            var thumbPath = VaultPaths.ToAbsoluteVaultPath(item.ThumbPath);
            var bytes = FileCryptoService.DecryptFileToBytes(_masterKey, thumbPath);

            // 이전 버전에서 가져온 영상은 단순 VIDEO 플레이스홀더가 암호화되어 있을 수 있습니다.
            // 작은 플레이스홀더로 판단되면 한 번 실제 영상 프레임 썸네일을 재생성하여 덮어씁니다.
            if (item.Kind == MediaKind.Video && bytes.Length < 16_000)
            {
                var regenerated = TryRegenerateVideoThumbnail(item);
                if (regenerated != null)
                    return regenerated;
            }

            return ThumbnailService.BytesToBitmap(bytes);
        }
        catch
        {
            if (item.Kind == MediaKind.Video)
            {
                var regenerated = TryRegenerateVideoThumbnail(item);
                if (regenerated != null)
                    return regenerated;
            }

            return ThumbnailService.CreatePlaceholderBitmap(item.Kind, item.Extension);
        }
    }

    private BitmapImage? TryRegenerateVideoThumbnail(MediaItem item)
    {
        string? temp = null;
        try
        {
            temp = TempFileService.CreateTempMediaPath(item.Id + "_thumb", item.Extension);
            FileCryptoService.DecryptFileToPath(_masterKey, VaultPaths.ToAbsoluteVaultPath(item.ObjectPath), temp);

            if (!ThumbnailService.TryCreateVideoThumbnailSet(temp, 480, out var thumbBytes, out var sourceFrameBytes, out var capturedAt))
                return null;

            SaveThumbnailBytes(item, thumbBytes, sourceFrameBytes);
            return ThumbnailService.BytesToBitmap(thumbBytes);
        }
        catch
        {
            return null;
        }
        finally
        {
            TempFileService.TryDelete(temp ?? string.Empty);
        }
    }

    public BitmapImage LoadImage(MediaItem item)
    {
        var bytes = FileCryptoService.DecryptFileToBytes(_masterKey, VaultPaths.ToAbsoluteVaultPath(item.ObjectPath));
        using var ms = new MemoryStream(bytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public BitmapImage? LoadThumbnailSource(MediaItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ThumbSourcePath))
            return null;

        try
        {
            var sourcePath = VaultPaths.ToAbsoluteVaultPath(item.ThumbSourcePath);
            if (!File.Exists(sourcePath))
                return null;

            var bytes = FileCryptoService.DecryptFileToBytes(_masterKey, sourcePath);
            return ThumbnailService.BytesToBitmap(bytes);
        }
        catch
        {
            return null;
        }
    }

    public void SetCustomThumbnail(MediaItem item, byte[] thumbnailBytes)
    {
        SaveThumbnailBytes(item, thumbnailBytes, null);
    }

    public void SetCustomThumbnail(MediaItem item, byte[] thumbnailBytes, byte[]? sourceFrameBytes)
    {
        SaveThumbnailBytes(item, thumbnailBytes, sourceFrameBytes);
    }

    public void SetVideoThumbnailFromFrame(MediaItem item, byte[] sourceFrameBytes, byte[] thumbnailBytes, TimeSpan capturedAt)
    {
        if (item.Kind != MediaKind.Video)
            throw new InvalidOperationException("동영상 항목에만 영상 프레임 썸네일을 설정할 수 있습니다.");

        SaveThumbnailBytes(item, thumbnailBytes, sourceFrameBytes);
    }

    private void SaveThumbnailBytes(MediaItem item, byte[] thumbnailBytes, byte[]? sourceFrameBytes)
    {
        if (thumbnailBytes == null || thumbnailBytes.Length == 0)
            throw new ArgumentException("썸네일 데이터가 비어 있습니다.", nameof(thumbnailBytes));

        FileCryptoService.EncryptBytesToFile(_masterKey, thumbnailBytes, VaultPaths.ToAbsoluteVaultPath(item.ThumbPath));

        if (sourceFrameBytes != null && sourceFrameBytes.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(item.ThumbSourcePath))
                item.ThumbSourcePath = VaultPaths.CreateThumbSourceRelativePath(item.Id).Replace('\\', '/');

            FileCryptoService.EncryptBytesToFile(_masterKey, sourceFrameBytes, VaultPaths.ToAbsoluteVaultPath(item.ThumbSourcePath));
            _db.SetThumbnailPaths(item.Id, item.ThumbPath, item.ThumbSourcePath);
        }
        else
        {
            _db.TouchMedia(item.Id);
        }

        item.UpdatedUtc = DateTime.UtcNow;
    }

    public string DecryptVideoToTemp(MediaItem item)
    {
        // MediaElement can keep the previous temp file handle for a short time after the viewer closes.
        // Use a unique temp path for each open so reopening the same video does not fail with file-in-use.
        var temp = TempFileService.CreateUniqueTempMediaPath(item.Id, item.Extension);
        FileCryptoService.DecryptFileToPath(_masterKey, VaultPaths.ToAbsoluteVaultPath(item.ObjectPath), temp);
        return temp;
    }

    public string DecryptFileToTemp(MediaItem item)
    {
        var temp = TempFileService.CreateTempMediaPath(item.Id, item.Extension);
        FileCryptoService.DecryptFileToPath(_masterKey, VaultPaths.ToAbsoluteVaultPath(item.ObjectPath), temp);
        return temp;
    }

    public void OpenWithDefaultApp(MediaItem item)
    {
        if (IsDefaultOpenBlocked(item))
        {
            var ext = string.IsNullOrWhiteSpace(item.Extension) ? Path.GetExtension(item.OriginalName) : item.Extension;
            throw new InvalidOperationException($"보안상 '{ext}' 형식은 Windows 기본 앱으로 바로 열 수 없습니다. 필요하면 파일을 내보낸 뒤 직접 확인해 주세요.");
        }

        var temp = DecryptFileToTemp(item);
        AppLogger.Warn($"Default app open created temporary decrypted file id={item.Id}; kind={item.Kind}");
        var psi = new ProcessStartInfo(temp)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(temp) ?? TempFileService.CurrentSessionRoot
        };
        Process.Start(psi);
    }

    public void ExportMedia(MediaItem item, string targetPath)
    {
        FileCryptoService.DecryptFileToPath(_masterKey, VaultPaths.ToAbsoluteVaultPath(item.ObjectPath), targetPath);
    }

    public void DeleteMediaFiles(MediaItem item)
    {
        TryDeleteVaultFile(item.ObjectPath);
        TryDeleteVaultFile(item.ThumbPath);
        if (!string.IsNullOrWhiteSpace(item.ThumbSourcePath))
            TryDeleteVaultFile(item.ThumbSourcePath);
        _db.DeleteMedia(item.Id);
    }

    public void CleanupPreparedImport(MediaItem? item)
    {
        if (item == null)
            return;

        TryDeleteVaultFile(item.ObjectPath);
        TryDeleteVaultFile(item.ThumbPath);
        if (!string.IsNullOrWhiteSpace(item.ThumbSourcePath))
            TryDeleteVaultFile(item.ThumbSourcePath);
    }

    public static bool IsSecuritySensitiveExtension(string? extensionOrPath)
    {
        var ext = extensionOrPath ?? string.Empty;
        if (!ext.StartsWith('.'))
            ext = Path.GetExtension(ext);

        return !string.IsNullOrWhiteSpace(ext) &&
               (BlockedExecutableExtensions.Contains(ext) || DefaultOpenBlockedExtensions.Contains(ext));
    }

    public static bool IsSecuritySensitiveFile(string? path)
    {
        return IsSecuritySensitiveExtension(path);
    }

    public static bool IsDefaultOpenBlocked(MediaItem item)
    {
        var ext = string.IsNullOrWhiteSpace(item.Extension) ? Path.GetExtension(item.OriginalName) : item.Extension;
        return IsSecuritySensitiveExtension(ext);
    }

    public static string GetSecurityWarningText(MediaItem item)
    {
        var ext = string.IsNullOrWhiteSpace(item.Extension) ? Path.GetExtension(item.OriginalName) : item.Extension;
        ext = string.IsNullOrWhiteSpace(ext) ? "알 수 없는 형식" : ext.ToLowerInvariant();
        return $"'{ext}' 형식은 Windows에서 실행성 동작을 할 수 있어 바로 열기를 제한합니다. 보관은 가능하지만, 내보내기 후 직접 확인할 때도 주의하세요.";
    }

    private static void TryDeleteVaultFile(string relativePath)
    {
        try
        {
            var path = VaultPaths.ToAbsoluteVaultPath(relativePath);
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    public string ExportMediaToExternalDragTemp(MediaItem item)
    {
        var exportDir = Path.Combine(TempFileService.CurrentSessionRoot, "external-drag");
        Directory.CreateDirectory(exportDir);

        var ext = string.IsNullOrWhiteSpace(item.Extension) ? Path.GetExtension(item.OriginalName) : item.Extension;
        var fileName = $"media_{item.Id[..Math.Min(12, item.Id.Length)]}{(ext.StartsWith('.') ? ext : "." + ext)}";

        var targetPath = Path.Combine(exportDir, fileName);
        if (File.Exists(targetPath))
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var duplicateExt = Path.GetExtension(fileName);
            targetPath = Path.Combine(exportDir, $"{baseName}_{item.Id[..Math.Min(8, item.Id.Length)]}{duplicateExt}");
        }

        FileCryptoService.DecryptFileToPath(_masterKey, VaultPaths.ToAbsoluteVaultPath(item.ObjectPath), targetPath);
        return targetPath;
    }

    private static string SanitizeFileName(string? fileName)
    {
        fileName = string.IsNullOrWhiteSpace(fileName) ? "media" : fileName.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');
        return string.IsNullOrWhiteSpace(fileName) ? "media" : fileName;
    }

    private static void TryFillImageDimensions(string sourcePath, MediaItem item)
    {
        if (ThumbnailService.TryReadImageDimensions(sourcePath, out var width, out var height))
        {
            item.Width = width;
            item.Height = height;
            return;
        }

        item.Width = 0;
        item.Height = 0;
    }
}
