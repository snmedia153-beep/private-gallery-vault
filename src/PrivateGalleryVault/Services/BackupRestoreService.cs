using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

namespace PrivateGalleryVault.Services;

public sealed class BackupPackageInfo
{
    public string BackupPath { get; init; } = string.Empty;
    public string FileName => Path.GetFileName(BackupPath);
    public long SizeBytes { get; init; }
    public int EntryCount { get; init; }
    public bool HasMasterFile { get; init; }
    public bool HasDatabase { get; init; }
    public bool HasVirtualFolderSchema { get; init; }
    public int FolderCount { get; init; }
    public int FolderedMediaCount { get; init; }
    public string DatabaseSchemaNote { get; init; } = string.Empty;
    public bool IsValid => HasMasterFile && HasDatabase;
}

public sealed class BackupRestoreService
{
    private static readonly HashSet<string> LiveDatabaseFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        Path.GetFileName(VaultPaths.DatabasePath),
        Path.GetFileName(VaultPaths.DatabasePath) + "-wal",
        Path.GetFileName(VaultPaths.DatabasePath) + "-shm",
        Path.GetFileName(VaultPaths.DatabasePath) + "-journal"
    };

    private readonly DatabaseService? _database;

    public BackupRestoreService(DatabaseService? database = null)
    {
        _database = database;
    }

    public string CreateBackup(string targetPath, IProgress<double>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("백업 파일 경로가 비어 있습니다.", nameof(targetPath));

        var fullTarget = Path.GetFullPath(targetPath);
        var parent = Path.GetDirectoryName(fullTarget);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        progress?.Report(0.03);
        if (File.Exists(fullTarget))
            File.Delete(fullTarget);

        var tempRoot = Path.Combine(Path.GetTempPath(), $"pgv_backup_stage_{Guid.NewGuid():N}");
        var tempZip = Path.Combine(Path.GetTempPath(), $"pgv_backup_{Guid.NewGuid():N}.zip");

        try
        {
            Directory.CreateDirectory(tempRoot);

            // catalog.db is opened by the running app, so it must not be copied directly.
            // Copy all vault files first, skipping SQLite live files, then inject a consistent
            // online SQLite snapshot as catalog.db. This preserves backup behavior while avoiding
            // "process cannot access the file" errors.
            CopyVaultFilesExceptLiveDatabase(VaultPaths.VaultRoot, tempRoot, progress);
            progress?.Report(0.72);

            CreateDatabaseSnapshot(Path.Combine(tempRoot, Path.GetFileName(VaultPaths.DatabasePath)));
            progress?.Report(0.82);

            WriteBackupDiagnostics(Path.Combine(tempRoot, "diagnostics", "vault-diagnostics.txt"));
            progress?.Report(0.86);

            CreateZipFromDirectorySafe(tempRoot, tempZip);
            progress?.Report(0.94);

            File.Move(tempZip, fullTarget, true);
            progress?.Report(1.0);
            return fullTarget;
        }
        finally
        {
            TryDeleteFile(tempZip);
            TryDeleteDirectory(tempRoot);
        }
    }

    public string GetDefaultBackupName()
    {
        return $"PrivateGalleryVault_Backup_{DateTime.Now:yyyy-MM-dd_HHmm}.pgvbackup";
    }

    public BackupPackageInfo InspectBackupPackage(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
            throw new ArgumentException("복원 파일 경로가 비어 있습니다.", nameof(backupPath));
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("복원 파일을 찾을 수 없습니다.", backupPath);

        using var archive = ZipFile.OpenRead(backupPath);
        var masterEntry = archive.Entries.FirstOrDefault(entry => IsRootEntry(entry.FullName, Path.GetFileName(VaultPaths.MasterFilePath)));
        var databaseEntry = archive.Entries.FirstOrDefault(entry => IsRootEntry(entry.FullName, Path.GetFileName(VaultPaths.DatabasePath)));
        var schema = InspectBackupDatabaseSchema(databaseEntry);
        var hasDiagnostics = archive.Entries.Any(entry => entry.FullName.Replace('\\', '/').StartsWith("diagnostics/", StringComparison.OrdinalIgnoreCase));
        var note = schema.Note + (hasDiagnostics ? " · 진단 리포트 포함" : string.Empty);

        return new BackupPackageInfo
        {
            BackupPath = backupPath,
            SizeBytes = new FileInfo(backupPath).Length,
            EntryCount = archive.Entries.Count,
            HasMasterFile = masterEntry != null,
            HasDatabase = databaseEntry != null,
            HasVirtualFolderSchema = schema.HasVirtualFolderSchema,
            FolderCount = schema.FolderCount,
            FolderedMediaCount = schema.FolderedMediaCount,
            DatabaseSchemaNote = note
        };
    }

    public string PrepareRestoreStage(string backupPath, IProgress<double>? progress = null)
    {
        var info = InspectBackupPackage(backupPath);
        if (!info.IsValid)
            throw new InvalidDataException("올바른 Private Gallery Vault 백업 파일이 아닙니다. master.json 또는 catalog.db가 없습니다.");

        var stageRoot = Path.Combine(Path.GetTempPath(), $"pgv_restore_stage_{Guid.NewGuid():N}");

        try
        {
            progress?.Report(0.10);
            ExtractBackupPackageSafe(backupPath, stageRoot);
            progress?.Report(0.80);
            ValidateExtractedBackup(stageRoot);
            progress?.Report(1.0);
            return stageRoot;
        }
        catch
        {
            TryDeleteDirectory(stageRoot);
            throw;
        }
    }

    public void RestoreBackupPackage(string backupPath, IProgress<double>? progress = null)
    {
        var info = InspectBackupPackage(backupPath);
        if (!info.IsValid)
            throw new InvalidDataException("올바른 Private Gallery Vault 백업 파일이 아닙니다. master.json 또는 catalog.db가 없습니다.");

        var parent = Path.GetDirectoryName(VaultPaths.VaultRoot) ?? AppContext.BaseDirectory;
        var stageRoot = Path.Combine(Path.GetTempPath(), $"pgv_restore_stage_{Guid.NewGuid():N}");
        var rollbackRoot = Path.Combine(parent, $"vault_restore_rollback_{DateTime.Now:yyyyMMdd_HHmmss}");
        var currentVaultMoved = false;

        try
        {
            progress?.Report(0.05);
            ExtractBackupPackageSafe(backupPath, stageRoot);
            ValidateExtractedBackup(stageRoot);
            progress?.Report(0.45);

            if (Directory.Exists(VaultPaths.VaultRoot))
            {
                Directory.Move(VaultPaths.VaultRoot, rollbackRoot);
                currentVaultMoved = true;
            }
            progress?.Report(0.62);

            Directory.Move(stageRoot, VaultPaths.VaultRoot);
            progress?.Report(0.88);

            TryDeleteDirectory(rollbackRoot);
            progress?.Report(1.0);
        }
        catch
        {
            if (currentVaultMoved)
            {
                TryDeleteDirectory(VaultPaths.VaultRoot);
                try
                {
                    if (Directory.Exists(rollbackRoot) && !Directory.Exists(VaultPaths.VaultRoot))
                        Directory.Move(rollbackRoot, VaultPaths.VaultRoot);
                }
                catch
                {
                    // The original vault is also protected by the pre-restore safety backup.
                }
            }
            throw;
        }
        finally
        {
            TryDeleteDirectory(stageRoot);
        }
    }

    public string GetDefaultPreRestoreBackupName()
    {
        return $"PrivateGalleryVault_PreRestore_{DateTime.Now:yyyy-MM-dd_HHmm}.pgvbackup";
    }


    private sealed class BackupDatabaseSchemaInfo
    {
        public bool HasVirtualFolderSchema { get; init; }
        public int FolderCount { get; init; }
        public int FolderedMediaCount { get; init; }
        public string Note { get; init; } = string.Empty;
    }

    private static BackupDatabaseSchemaInfo InspectBackupDatabaseSchema(ZipArchiveEntry? databaseEntry)
    {
        if (databaseEntry == null)
            return new BackupDatabaseSchemaInfo { Note = "catalog.db 없음" };

        var tempDb = Path.Combine(Path.GetTempPath(), $"pgv_backup_schema_{Guid.NewGuid():N}.db");
        try
        {
            using (var input = databaseEntry.Open())
            using (var output = new FileStream(tempDb, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                input.CopyTo(output);
            }

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = tempDb,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };

            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();

            var hasMedia = TableExists(connection, "media");
            var hasTopicFolders = TableExists(connection, "topic_folders");
            var hasMediaFolderId = hasMedia && ColumnExists(connection, "media", "folder_id");
            var folderCount = hasTopicFolders ? ExecuteScalarInt(connection, "SELECT COUNT(*) FROM topic_folders;") : 0;
            var folderedMediaCount = hasMediaFolderId ? ExecuteScalarInt(connection, "SELECT COUNT(*) FROM media WHERE folder_id IS NOT NULL;") : 0;

            return new BackupDatabaseSchemaInfo
            {
                HasVirtualFolderSchema = hasTopicFolders && hasMediaFolderId,
                FolderCount = folderCount,
                FolderedMediaCount = folderedMediaCount,
                Note = hasTopicFolders && hasMediaFolderId
                    ? $"가상 폴더 스키마 포함 · 폴더 {folderCount}개 · 폴더 지정 파일 {folderedMediaCount}개"
                    : "구버전 백업 · 복원 후 폴더 스키마 자동 마이그레이션"
            };
        }
        catch (Exception ex)
        {
            return new BackupDatabaseSchemaInfo
            {
                Note = "catalog.db 스키마 검토 실패: " + ex.Message
            };
        }
        finally
        {
            TryDeleteFile(tempDb);
        }
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static int ExecuteScalarInt(SqliteConnection connection, string commandText)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static bool IsRootEntry(string entryName, string expectedFileName)
    {
        var normalized = entryName.Replace('\\', '/').Trim('/');
        return string.Equals(normalized, expectedFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static void ExtractBackupPackageSafe(string backupPath, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        var fullDestinationRoot = Path.GetFullPath(destinationRoot);
        if (!fullDestinationRoot.EndsWith(Path.DirectorySeparatorChar))
            fullDestinationRoot += Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(backupPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName))
                continue;

            var normalizedEntryName = entry.FullName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, normalizedEntryName));
            if (!targetPath.StartsWith(fullDestinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("백업 파일 내부 경로가 안전하지 않습니다: " + entry.FullName);

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            entry.ExtractToFile(targetPath, true);
        }
    }

    private static void ValidateExtractedBackup(string extractedRoot)
    {
        var masterPath = Path.Combine(extractedRoot, Path.GetFileName(VaultPaths.MasterFilePath));
        var databasePath = Path.Combine(extractedRoot, Path.GetFileName(VaultPaths.DatabasePath));

        if (!File.Exists(masterPath))
            throw new InvalidDataException("복원 백업에 master.json이 없습니다.");
        if (!File.Exists(databasePath))
            throw new InvalidDataException("복원 백업에 catalog.db가 없습니다.");
    }

    private void CopyVaultFilesExceptLiveDatabase(string sourceRoot, string destinationRoot, IProgress<double>? progress)
    {
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException("Vault 폴더를 찾을 수 없습니다: " + sourceRoot);

        var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToList();
        var total = Math.Max(1, files.Count);
        var copied = 0;

        foreach (var sourceFile in files)
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourceFile);

            if (ShouldSkipLiveDatabaseFile(relativePath))
            {
                copied++;
                progress?.Report(0.03 + 0.62 * copied / total);
                continue;
            }

            var destinationFile = Path.Combine(destinationRoot, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            CopyFileWithSharedRead(sourceFile, destinationFile);
            copied++;
            progress?.Report(0.03 + 0.62 * copied / total);
        }

        EnsureEmptyDirectories(sourceRoot, destinationRoot);
    }

    private static bool ShouldSkipLiveDatabaseFile(string relativePath)
    {
        // The SQLite files are located directly under VaultRoot. Do not skip unrelated files
        // with the same name in media/object subfolders.
        if (relativePath.Contains(Path.DirectorySeparatorChar) || relativePath.Contains(Path.AltDirectorySeparatorChar))
            return false;

        return LiveDatabaseFileNames.Contains(Path.GetFileName(relativePath));
    }

    private void CreateDatabaseSnapshot(string destinationPath)
    {
        if (_database != null)
        {
            _database.CreateOnlineBackup(destinationPath);
            return;
        }

        // Fallback for non-runtime usage. The running app always supplies DatabaseService.
        // Use shared read so command-line/offline backup can still succeed when possible.
        CopyFileWithSharedRead(VaultPaths.DatabasePath, destinationPath);
    }

    private void WriteBackupDiagnostics(string destinationPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var report = _database != null
                ? _database.BuildVaultDiagnosticsReport()
                : BuildOfflineBackupDiagnosticsReport();
            File.WriteAllText(destinationPath, report, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Failed to write backup diagnostics", ex);
        }
    }

    private static string BuildOfflineBackupDiagnosticsReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Private Gallery Vault Backup Diagnostics");
        sb.AppendLine("========================================");
        sb.AppendLine($"GeneratedLocal: {DateTime.Now:O}");
        sb.AppendLine("VaultRoot: [redacted]");
        sb.AppendLine("DatabasePath: [redacted]");
        sb.AppendLine("MasterFilePath: [redacted]");
        sb.AppendLine($"catalog.db exists: {File.Exists(VaultPaths.DatabasePath)}");
        sb.AppendLine($"master.json exists: {File.Exists(VaultPaths.MasterFilePath)}");
        return sb.ToString();
    }

    private static void CopyFileWithSharedRead(string sourcePath, string destinationPath)
    {
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(destination);
    }

    private static void EnsureEmptyDirectories(string sourceRoot, string destinationRoot)
    {
        foreach (var sourceDirectory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourceDirectory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relativePath));
        }
    }


    private static void CreateZipFromDirectorySafe(string sourceDirectory, string destinationZipPath)
    {
        if (File.Exists(destinationZipPath))
            File.Delete(destinationZipPath);

        using var zipStream = new FileStream(destinationZipPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any())
                continue;

            var relativeDirectory = Path.GetRelativePath(sourceDirectory, directory)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            if (!relativeDirectory.EndsWith('/'))
                relativeDirectory += "/";

            archive.CreateEntry(relativeDirectory, CompressionLevel.NoCompression);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');

            AddFileToArchiveWithRetry(archive, file, relativePath);
        }
    }

    private static void AddFileToArchiveWithRetry(ZipArchive archive, string sourcePath, string entryName)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                entry.LastWriteTime = File.GetLastWriteTime(sourcePath);

                using var output = entry.Open();
                input.CopyTo(output);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
                System.Threading.Thread.Sleep(150 + attempt * 100);
            }
        }

        throw new IOException($"백업 압축 중 파일을 읽을 수 없습니다: {sourcePath}", lastError);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
        }
    }
}
