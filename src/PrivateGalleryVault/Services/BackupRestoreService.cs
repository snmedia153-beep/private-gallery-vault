using System.IO;
using System.IO.Compression;

namespace PrivateGalleryVault.Services;

public sealed class BackupRestoreService
{
    public string CreateBackup(string targetPath, IProgress<double>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("백업 파일 경로가 비어 있습니다.", nameof(targetPath));

        var fullTarget = Path.GetFullPath(targetPath);
        var parent = Path.GetDirectoryName(fullTarget);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        progress?.Report(0.05);
        if (File.Exists(fullTarget))
            File.Delete(fullTarget);

        var tempZip = Path.Combine(Path.GetTempPath(), $"pgv_backup_{Guid.NewGuid():N}.zip");
        try
        {
            ZipFile.CreateFromDirectory(VaultPaths.VaultRoot, tempZip, CompressionLevel.Optimal, false);
            progress?.Report(0.85);
            File.Move(tempZip, fullTarget, true);
            progress?.Report(1.0);
            return fullTarget;
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
    }

    public string GetDefaultBackupName()
    {
        return $"PrivateGalleryVault_Backup_{DateTime.Now:yyyy-MM-dd_HHmm}.pgvbackup";
    }
}
