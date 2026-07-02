using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using PrivateGalleryVault.Models;

namespace PrivateGalleryVault.Services;

public sealed class FolderIntegrityRepairResult
{
    public string? BeforeRepairSnapshotPath { get; set; }
    public string? BeforeRepairReportPath { get; set; }
    public int OrphanFoldersRemoved { get; set; }
    public int MediaFolderLinksCleared { get; set; }
    public int InvalidParentLinksCleared { get; set; }
    public int SelfParentLinksCleared { get; set; }
    public int CyclesBroken { get; set; }
    public int FolderNamesRecovered { get; set; }
    public int DuplicateFolderNamesRenamed { get; set; }
    public int SortOrdersNormalized { get; set; }

    public bool HasChanges => OrphanFoldersRemoved > 0
        || MediaFolderLinksCleared > 0
        || InvalidParentLinksCleared > 0
        || SelfParentLinksCleared > 0
        || CyclesBroken > 0
        || FolderNamesRecovered > 0
        || DuplicateFolderNamesRenamed > 0
        || SortOrdersNormalized > 0;

    public string ToSummary()
    {
        if (!HasChanges)
            return "폴더 구조 이상 없음";

        var parts = new List<string>();
        if (OrphanFoldersRemoved > 0) parts.Add($"고아 폴더 삭제 {OrphanFoldersRemoved}개");
        if (MediaFolderLinksCleared > 0) parts.Add($"잘못된 파일 폴더 연결 해제 {MediaFolderLinksCleared}개");
        if (InvalidParentLinksCleared > 0) parts.Add($"잘못된 상위 폴더 연결 해제 {InvalidParentLinksCleared}개");
        if (SelfParentLinksCleared > 0) parts.Add($"자기 자신 상위 연결 해제 {SelfParentLinksCleared}개");
        if (CyclesBroken > 0) parts.Add($"순환 폴더 구조 해제 {CyclesBroken}개");
        if (FolderNamesRecovered > 0) parts.Add($"손상된 폴더명 복구 {FolderNamesRecovered}개");
        if (DuplicateFolderNamesRenamed > 0) parts.Add($"중복 폴더명 정리 {DuplicateFolderNamesRenamed}개");
        if (SortOrdersNormalized > 0) parts.Add($"폴더 순서 정리 {SortOrdersNormalized}개");
        return string.Join(", ", parts);
    }
}

public sealed class DatabaseService : IDisposable
{
    private readonly byte[] _masterKey;
    private readonly SqliteConnection _connection;

    public DatabaseService(byte[] masterKey)
    {
        _masterKey = masterKey;
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = VaultPaths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
        _connection = new SqliteConnection(cs);
        _connection.Open();
    }

    public void Initialize()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;
CREATE TABLE IF NOT EXISTS topics (
    id TEXT PRIMARY KEY,
    name_enc BLOB NOT NULL,
    description_enc BLOB NULL,
    cover_media_id TEXT NULL,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS topic_folders (
    id TEXT PRIMARY KEY,
    topic_id TEXT NOT NULL,
    parent_folder_id TEXT NULL,
    name_enc BLOB NOT NULL,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(topic_id) REFERENCES topics(id) ON DELETE CASCADE,
    FOREIGN KEY(parent_folder_id) REFERENCES topic_folders(id) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS media (
    id TEXT PRIMARY KEY,
    topic_id TEXT NOT NULL,
    folder_id TEXT NULL,
    kind INTEGER NOT NULL,
    original_name_enc BLOB NOT NULL,
    extension_enc BLOB NOT NULL,
    object_path TEXT NOT NULL,
    thumb_path TEXT NOT NULL,
    thumb_source_path TEXT NOT NULL DEFAULT '',
    source_hash TEXT NOT NULL DEFAULT '',
    size_bytes INTEGER NOT NULL,
    width INTEGER NOT NULL DEFAULT 0,
    height INTEGER NOT NULL DEFAULT 0,
    duration_seconds REAL NOT NULL DEFAULT 0,
    favorite INTEGER NOT NULL DEFAULT 0,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(topic_id) REFERENCES topics(id) ON DELETE CASCADE,
    FOREIGN KEY(folder_id) REFERENCES topic_folders(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS idx_media_topic ON media(topic_id);
CREATE INDEX IF NOT EXISTS idx_media_created ON media(created_utc DESC);
CREATE TABLE IF NOT EXISTS activity_logs (
    id TEXT PRIMARY KEY,
    action_type TEXT NOT NULL,
    title TEXT NOT NULL,
    detail TEXT NOT NULL DEFAULT '',
    target_id TEXT NOT NULL DEFAULT '',
    target_name TEXT NOT NULL DEFAULT '',
    result TEXT NOT NULL DEFAULT 'success',
    actor TEXT NOT NULL DEFAULT 'User',
    title_enc BLOB NULL,
    detail_enc BLOB NULL,
    target_name_enc BLOB NULL,
    actor_enc BLOB NULL,
    created_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS tags (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    name_enc BLOB NULL,
    color TEXT NOT NULL DEFAULT '#3B82F6',
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS media_tags (
    media_id TEXT NOT NULL,
    tag_id TEXT NOT NULL,
    PRIMARY KEY(media_id, tag_id),
    FOREIGN KEY(media_id) REFERENCES media(id) ON DELETE CASCADE,
    FOREIGN KEY(tag_id) REFERENCES tags(id) ON DELETE CASCADE
);
";
        cmd.ExecuteNonQuery();
        EnsureColumn("topics", "description_enc", "BLOB NULL");
        EnsureColumn("topics", "cover_media_id", "TEXT NULL");
        EnsureColumn("topics", "sort_order", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("media", "folder_id", "TEXT NULL");
        EnsureColumn("media", "thumb_source_path", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("media", "source_hash", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("media", "sort_order", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("activity_logs", "title_enc", "BLOB NULL");
        EnsureColumn("activity_logs", "detail_enc", "BLOB NULL");
        EnsureColumn("activity_logs", "target_name_enc", "BLOB NULL");
        EnsureColumn("activity_logs", "actor_enc", "BLOB NULL");
        EnsureColumn("tags", "name_enc", "BLOB NULL");
        MigrateActivityLogMetadataToEncryptedColumns();
        MigrateTagNamesToEncryptedColumn();
        EnsureIndex("idx_topics_sort", "topics", "sort_order, created_utc");
        EnsureIndex("idx_topic_folders_parent_sort", "topic_folders", "topic_id, parent_folder_id, sort_order, created_utc");
        EnsureIndex("idx_topic_folders_topic", "topic_folders", "topic_id");
        EnsureIndex("idx_media_sort", "media", "topic_id, sort_order, created_utc");
        EnsureIndex("idx_media_topic_folder", "media", "topic_id, folder_id, sort_order, created_utc");
        EnsureIndex("idx_media_folder", "media", "folder_id");
        EnsureIndex("idx_media_source_hash", "media", "source_hash");
        EnsureIndex("idx_activity_created", "activity_logs", "created_utc DESC");
        EnsureIndex("idx_activity_action", "activity_logs", "action_type, created_utc DESC");
        EnsureIndex("idx_media_tags_tag", "media_tags", "tag_id, media_id");

        try
        {
            var repair = ValidateAndRepairFolderIntegrity(writeActivityLog: false);
            if (repair.HasChanges)
            {
                AppLogger.Info("Folder integrity repaired during Initialize: " + repair.ToSummary());
                if (!string.IsNullOrWhiteSpace(repair.BeforeRepairSnapshotPath))
                    AppLogger.Info("Folder integrity pre-repair snapshot: " + repair.BeforeRepairSnapshotPath);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Folder integrity repair failed during Initialize", ex);
        }

        AppLogger.Info("Database initialized");
    }

    private void EnsureColumn(string tableName, string columnName, string definition)
    {
        using var check = _connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({tableName});";
        using var r = check.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return;
        }

        using var alter = _connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        alter.ExecuteNonQuery();
    }



    private void MigrateActivityLogMetadataToEncryptedColumns()
    {
        try
        {
            var rows = new List<(string Id, string Title, string Detail, string TargetName, string Actor)>();
            using (var select = _connection.CreateCommand())
            {
                select.CommandText = @"SELECT id, title, detail, target_name, actor
                                       FROM activity_logs
                                       WHERE title_enc IS NULL OR length(title_enc)=0;";
                using var r = select.ExecuteReader();
                while (r.Read())
                {
                    rows.Add((
                        r.GetString(0),
                        r.IsDBNull(1) ? string.Empty : r.GetString(1),
                        r.IsDBNull(2) ? string.Empty : r.GetString(2),
                        r.IsDBNull(3) ? string.Empty : r.GetString(3),
                        r.IsDBNull(4) ? "User" : r.GetString(4)));
                }
            }

            if (rows.Count == 0)
                return;

            using var tx = _connection.BeginTransaction();
            using var update = _connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText = @"UPDATE activity_logs
                                   SET title='[encrypted]', detail='', target_name='', actor=$actor,
                                       title_enc=$titleEnc, detail_enc=$detailEnc,
                                       target_name_enc=$targetNameEnc, actor_enc=$actorEnc
                                   WHERE id=$id;";
            var idParam = update.CreateParameter(); idParam.ParameterName = "$id"; update.Parameters.Add(idParam);
            var actorParam = update.CreateParameter(); actorParam.ParameterName = "$actor"; update.Parameters.Add(actorParam);
            var titleParam = update.CreateParameter(); titleParam.ParameterName = "$titleEnc"; update.Parameters.Add(titleParam);
            var detailParam = update.CreateParameter(); detailParam.ParameterName = "$detailEnc"; update.Parameters.Add(detailParam);
            var targetNameParam = update.CreateParameter(); targetNameParam.ParameterName = "$targetNameEnc"; update.Parameters.Add(targetNameParam);
            var actorEncParam = update.CreateParameter(); actorEncParam.ParameterName = "$actorEnc"; update.Parameters.Add(actorEncParam);

            foreach (var row in rows)
            {
                idParam.Value = row.Id;
                actorParam.Value = NormalizeActor(row.Actor);
                titleParam.Value = EncryptActivityLogText(row.Id, "title", row.Title);
                detailParam.Value = EncryptActivityLogText(row.Id, "detail", row.Detail);
                targetNameParam.Value = EncryptActivityLogText(row.Id, "target_name", row.TargetName);
                actorEncParam.Value = EncryptActivityLogText(row.Id, "actor", row.Actor);
                update.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Activity log metadata migration failed", ex);
        }
    }

    private void MigrateTagNamesToEncryptedColumn()
    {
        try
        {
            var rows = new List<(string Id, string Name)>();
            using (var select = _connection.CreateCommand())
            {
                select.CommandText = @"SELECT id, name
                                       FROM tags
                                       WHERE name_enc IS NULL OR length(name_enc)=0;";
                using var r = select.ExecuteReader();
                while (r.Read())
                    rows.Add((r.GetString(0), r.IsDBNull(1) ? string.Empty : r.GetString(1)));
            }

            if (rows.Count == 0)
                return;

            using var tx = _connection.BeginTransaction();
            using var update = _connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText = @"UPDATE tags
                                   SET name=$placeholder, name_enc=$nameEnc, updated_utc=$updated
                                   WHERE id=$id;";
            var idParam = update.CreateParameter(); idParam.ParameterName = "$id"; update.Parameters.Add(idParam);
            var placeholderParam = update.CreateParameter(); placeholderParam.ParameterName = "$placeholder"; update.Parameters.Add(placeholderParam);
            var nameEncParam = update.CreateParameter(); nameEncParam.ParameterName = "$nameEnc"; update.Parameters.Add(nameEncParam);
            var updatedParam = update.CreateParameter(); updatedParam.ParameterName = "$updated"; update.Parameters.Add(updatedParam);
            var now = DateTime.UtcNow.ToString("O");

            foreach (var row in rows)
            {
                idParam.Value = row.Id;
                placeholderParam.Value = BuildTagNamePlaceholder(row.Id);
                nameEncParam.Value = EncryptTagName(row.Id, row.Name);
                updatedParam.Value = now;
                update.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Tag metadata migration failed", ex);
        }
    }

    private byte[] EncryptActivityLogText(string logId, string field, string? text)
    {
        return EncryptString(text ?? string.Empty, $"activity_logs.{field}:{logId}");
    }

    private string DecryptActivityLogText(SqliteDataReader reader, int ordinal, string logId, string field, string fallback)
    {
        if (reader.IsDBNull(ordinal))
            return fallback;

        try
        {
            return DecryptString((byte[])reader.GetValue(ordinal), $"activity_logs.{field}:{logId}");
        }
        catch
        {
            return fallback;
        }
    }

    private byte[] EncryptTagName(string tagId, string? name)
    {
        return EncryptString(name ?? string.Empty, $"tags.name:{tagId}");
    }

    private string DecryptTagName(SqliteDataReader reader, int ordinal, string tagId, string fallback)
    {
        if (reader.IsDBNull(ordinal))
            return fallback;

        try
        {
            return DecryptString((byte[])reader.GetValue(ordinal), $"tags.name:{tagId}");
        }
        catch
        {
            return fallback;
        }
    }

    private static string BuildTagNamePlaceholder(string tagId)
    {
        var safeId = string.IsNullOrWhiteSpace(tagId) ? Guid.NewGuid().ToString("N") : tagId.Trim();
        return "tag_" + safeId[..Math.Min(24, safeId.Length)];
    }

    private static string NormalizeActor(string? actor)
    {
        return string.Equals(actor, "System", StringComparison.OrdinalIgnoreCase) ? "System" : "User";
    }

    private int GetNextTopicSortOrder()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(sort_order), 0) + 10 FROM topics;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private int GetNextMediaSortOrder(string topicId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = string.IsNullOrWhiteSpace(topicId)
            ? "SELECT COALESCE(MAX(sort_order), 0) + 10 FROM media WHERE topic_id IS NULL OR topic_id='';"
            : "SELECT COALESCE(MAX(sort_order), 0) + 10 FROM media WHERE topic_id=$topic;";
        if (!string.IsNullOrWhiteSpace(topicId))
            cmd.Parameters.AddWithValue("$topic", topicId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private int GetNextMediaSortOrder(string topicId, string? folderId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT COALESCE(MAX(sort_order), 0) + 10
                            FROM media
                            WHERE topic_id=$topic
                              AND ((folder_id IS NULL AND $folder IS NULL) OR folder_id=$folder);";
        cmd.Parameters.AddWithValue("$topic", topicId);
        cmd.Parameters.AddWithValue("$folder", string.IsNullOrWhiteSpace(folderId) ? DBNull.Value : folderId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private int GetNextFolderSortOrder(string topicId, string? parentFolderId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT COALESCE(MAX(sort_order), 0) + 10
                            FROM topic_folders
                            WHERE topic_id=$topic
                              AND ((parent_folder_id IS NULL AND $parent IS NULL) OR parent_folder_id=$parent);";
        cmd.Parameters.AddWithValue("$topic", topicId);
        cmd.Parameters.AddWithValue("$parent", string.IsNullOrWhiteSpace(parentFolderId) ? DBNull.Value : parentFolderId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void UpdateTopicSortOrders(IReadOnlyList<string> orderedIds)
    {
        using var tx = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE topics SET sort_order=$sort, updated_utc=$updated WHERE id=$id;";
        var idParam = cmd.CreateParameter(); idParam.ParameterName = "$id"; cmd.Parameters.Add(idParam);
        var sortParam = cmd.CreateParameter(); sortParam.ParameterName = "$sort"; cmd.Parameters.Add(sortParam);
        var updatedParam = cmd.CreateParameter(); updatedParam.ParameterName = "$updated"; cmd.Parameters.Add(updatedParam);

        var now = DateTime.UtcNow.ToString("O");
        for (var i = 0; i < orderedIds.Count; i++)
        {
            idParam.Value = orderedIds[i];
            sortParam.Value = (i + 1) * 10;
            updatedParam.Value = now;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void UpdateMediaSortOrders(IReadOnlyList<string> orderedIds)
    {
        using var tx = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE media SET sort_order=$sort, updated_utc=$updated WHERE id=$id;";
        var idParam = cmd.CreateParameter(); idParam.ParameterName = "$id"; cmd.Parameters.Add(idParam);
        var sortParam = cmd.CreateParameter(); sortParam.ParameterName = "$sort"; cmd.Parameters.Add(sortParam);
        var updatedParam = cmd.CreateParameter(); updatedParam.ParameterName = "$updated"; cmd.Parameters.Add(updatedParam);

        var now = DateTime.UtcNow.ToString("O");
        for (var i = 0; i < orderedIds.Count; i++)
        {
            idParam.Value = orderedIds[i];
            sortParam.Value = (i + 1) * 10;
            updatedParam.Value = now;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }


    public string WriteVaultDiagnosticsReport(string? targetDirectory = null)
    {
        var directory = string.IsNullOrWhiteSpace(targetDirectory) ? AppLogger.DiagnosticsDirectory : targetDirectory;
        Directory.CreateDirectory(directory!);
        var path = Path.Combine(directory!, $"vault-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
        File.WriteAllText(path, BuildVaultDiagnosticsReport(), Encoding.UTF8);
        return path;
    }

    public string BuildVaultDiagnosticsReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Private Gallery Vault Diagnostics");
        sb.AppendLine("=================================");
        sb.AppendLine($"GeneratedLocal: {DateTime.Now:O}");
        sb.AppendLine($"GeneratedUtc: {DateTime.UtcNow:O}");
        sb.AppendLine("AppBase: [redacted]");
        sb.AppendLine("VaultRoot: [redacted]");
        sb.AppendLine("DatabasePath: [redacted]");
        sb.AppendLine("MasterFilePath: [redacted]");
        sb.AppendLine("LogDirectory: [redacted]");
        sb.AppendLine();

        AppendFileStatus(sb, "master.json", VaultPaths.MasterFilePath);
        AppendFileStatus(sb, "catalog.db", VaultPaths.DatabasePath);
        AppendDirectoryStatus(sb, "objects", VaultPaths.ObjectsRoot);
        AppendDirectoryStatus(sb, "thumbs", VaultPaths.ThumbsRoot);
        AppendDirectoryStatus(sb, "thumb_sources", VaultPaths.ThumbSourcesRoot);
        AppendDirectoryStatus(sb, "repair_snapshots", FolderRepairSnapshotDirectory);
        AppendRecentFolderRepairSnapshots(sb, 5);
        sb.AppendLine();

        sb.AppendLine("Counts");
        sb.AppendLine("------");
        AppendCount(sb, "topics", "SELECT COUNT(*) FROM topics;");
        AppendCount(sb, "topic_folders", "SELECT COUNT(*) FROM topic_folders;");
        AppendCount(sb, "media", "SELECT COUNT(*) FROM media;");
        AppendCount(sb, "media_in_root", "SELECT COUNT(*) FROM media WHERE folder_id IS NULL OR folder_id='';");
        AppendCount(sb, "media_in_folders", "SELECT COUNT(*) FROM media WHERE folder_id IS NOT NULL AND folder_id<>'';");
        AppendCount(sb, "favorites", "SELECT COUNT(*) FROM media WHERE favorite=1;");
        AppendCount(sb, "tags", "SELECT COUNT(*) FROM tags;");
        AppendCount(sb, "media_tags", "SELECT COUNT(*) FROM media_tags;");
        AppendCount(sb, "activity_logs", "SELECT COUNT(*) FROM activity_logs;");
        sb.AppendLine();

        sb.AppendLine("MediaKinds");
        sb.AppendLine("----------");
        AppendCount(sb, "image", "SELECT COUNT(*) FROM media WHERE kind=0;");
        AppendCount(sb, "video", "SELECT COUNT(*) FROM media WHERE kind=1;");
        AppendCount(sb, "document", "SELECT COUNT(*) FROM media WHERE kind=2;");
        AppendCount(sb, "archive", "SELECT COUNT(*) FROM media WHERE kind=3;");
        AppendCount(sb, "other", "SELECT COUNT(*) FROM media WHERE kind=4;");
        sb.AppendLine();

        sb.AppendLine("FolderIntegrityChecks");
        sb.AppendLine("---------------------");
        AppendCount(sb, "orphan_folders", @"SELECT COUNT(*) FROM topic_folders f
            WHERE f.topic_id IS NULL OR f.topic_id='' OR NOT EXISTS (SELECT 1 FROM topics t WHERE t.id=f.topic_id);");
        AppendCount(sb, "invalid_media_folder_links", @"SELECT COUNT(*) FROM media m
            WHERE m.folder_id IS NOT NULL AND NOT EXISTS (
                SELECT 1 FROM topic_folders f WHERE f.id=m.folder_id AND f.topic_id=m.topic_id
            );");
        AppendCount(sb, "self_parent_folders", "SELECT COUNT(*) FROM topic_folders WHERE parent_folder_id=id;");
        AppendCount(sb, "invalid_parent_links", @"SELECT COUNT(*) FROM topic_folders f
            WHERE f.parent_folder_id IS NOT NULL AND NOT EXISTS (
                SELECT 1 FROM topic_folders p WHERE p.id=f.parent_folder_id AND p.topic_id=f.topic_id
            );");
        AppendFolderDepthAndCycleSummary(sb);
        sb.AppendLine();

        sb.AppendLine("RecentActivity");
        sb.AppendLine("--------------");
        AppendRecentActivity(sb, 12);

        return sb.ToString();
    }

    private static void AppendFileStatus(StringBuilder sb, string label, string path)
    {
        try
        {
            var info = new FileInfo(path);
            sb.AppendLine($"{label}: exists={info.Exists}; size={FormatBytes(info.Exists ? info.Length : 0)}; path=[redacted]");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"{label}: inspect failed; path=[redacted]; error={ex.Message}");
        }
    }

    private static void AppendDirectoryStatus(StringBuilder sb, string label, string path)
    {
        try
        {
            var exists = Directory.Exists(path);
            var fileCount = exists ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count() : 0;
            sb.AppendLine($"{label}: exists={exists}; files={fileCount}; path=[redacted]");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"{label}: inspect failed; path=[redacted]; error={ex.Message}");
        }
    }

    private void AppendCount(StringBuilder sb, string label, string sql)
    {
        try
        {
            sb.AppendLine($"{label}: {ExecuteScalarLong(sql)}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"{label}: failed ({ex.Message})");
        }
    }

    private long ExecuteScalarLong(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    private void AppendFolderDepthAndCycleSummary(StringBuilder sb)
    {
        try
        {
            var rows = new List<(string Id, string? ParentId)>();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT id, parent_folder_id FROM topic_folders;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    rows.Add((r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1)));
            }

            var parentMap = rows.ToDictionary(row => row.Id, row => row.ParentId, StringComparer.OrdinalIgnoreCase);
            var maxDepth = 0;
            var cycleCount = 0;
            foreach (var row in rows)
            {
                var depth = 0;
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var currentId = row.Id;
                while (parentMap.TryGetValue(currentId, out var parentId) && !string.IsNullOrWhiteSpace(parentId))
                {
                    if (!visited.Add(currentId) || string.Equals(currentId, parentId, StringComparison.OrdinalIgnoreCase))
                    {
                        cycleCount++;
                        break;
                    }
                    depth++;
                    currentId = parentId;
                    if (depth > 256)
                    {
                        cycleCount++;
                        break;
                    }
                }
                maxDepth = Math.Max(maxDepth, depth);
            }

            sb.AppendLine($"folder_max_depth: {maxDepth}");
            sb.AppendLine($"folder_cycle_candidates: {cycleCount}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"folder_depth_cycle_summary: failed ({ex.Message})");
        }
    }

    private void AppendRecentActivity(StringBuilder sb, int limit)
    {
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"SELECT created_utc, action_type, result
                                FROM activity_logs
                                ORDER BY datetime(created_utc) DESC
                                LIMIT $limit;";
            cmd.Parameters.AddWithValue("$limit", Math.Max(1, Math.Min(limit, 50)));
            using var r = cmd.ExecuteReader();
            var count = 0;
            while (r.Read())
            {
                count++;
                sb.AppendLine($"{r.GetString(0)} | {r.GetString(1)} | [encrypted] | {r.GetString(2)}");
            }
            if (count == 0)
                sb.AppendLine("최근 활동 없음");
        }
        catch (Exception ex)
        {
            sb.AppendLine("recent_activity: failed (" + ex.Message + ")");
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = new[] { "B", "KB", "MB", "GB", "TB" };
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }



    private static string FolderRepairSnapshotDirectory => Path.Combine(VaultPaths.VaultRoot, "repair-snapshots");

    private sealed class FolderIntegrityPreflightResult
    {
        public int OrphanFolders { get; set; }
        public int InvalidMediaFolderLinks { get; set; }
        public int InvalidParentLinks { get; set; }
        public int SelfParentLinks { get; set; }
        public int CycleCandidates { get; set; }
        public int BrokenFolderNames { get; set; }
        public int DuplicateFolderNames { get; set; }
        public int SortOrderProblems { get; set; }

        public bool HasPotentialChanges => OrphanFolders > 0
            || InvalidMediaFolderLinks > 0
            || InvalidParentLinks > 0
            || SelfParentLinks > 0
            || CycleCandidates > 0
            || BrokenFolderNames > 0
            || DuplicateFolderNames > 0
            || SortOrderProblems > 0;

        public string ToSummary()
        {
            if (!HasPotentialChanges)
                return "폴더 구조 자동 복구 예정 없음";

            var parts = new List<string>();
            if (OrphanFolders > 0) parts.Add($"고아 폴더 {OrphanFolders}개");
            if (InvalidMediaFolderLinks > 0) parts.Add($"잘못된 파일 폴더 연결 {InvalidMediaFolderLinks}개");
            if (InvalidParentLinks > 0) parts.Add($"잘못된 상위 폴더 연결 {InvalidParentLinks}개");
            if (SelfParentLinks > 0) parts.Add($"자기 자신 상위 연결 {SelfParentLinks}개");
            if (CycleCandidates > 0) parts.Add($"순환 후보 {CycleCandidates}개");
            if (BrokenFolderNames > 0) parts.Add($"손상된 폴더명 {BrokenFolderNames}개");
            if (DuplicateFolderNames > 0) parts.Add($"중복 폴더명 {DuplicateFolderNames}개");
            if (SortOrderProblems > 0) parts.Add($"정리 필요한 폴더 순서 {SortOrderProblems}개");
            return string.Join(", ", parts);
        }
    }

    private sealed class FolderRepairRow
    {
        public string Id { get; init; } = string.Empty;
        public string TopicId { get; set; } = string.Empty;
        public string? ParentFolderId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; init; }
        public int SortOrder { get; set; }
    }


    private FolderIntegrityPreflightResult InspectFolderIntegrityForRepair()
    {
        var result = new FolderIntegrityPreflightResult();
        result.OrphanFolders = SafeScalarInt(@"SELECT COUNT(*) FROM topic_folders
            WHERE topic_id IS NULL OR topic_id='' OR NOT EXISTS (SELECT 1 FROM topics t WHERE t.id=topic_folders.topic_id);");
        result.InvalidMediaFolderLinks = SafeScalarInt(@"SELECT COUNT(*) FROM media m
            WHERE m.folder_id IS NOT NULL AND NOT EXISTS (
                SELECT 1 FROM topic_folders f WHERE f.id=m.folder_id AND f.topic_id=m.topic_id
            );");
        result.SelfParentLinks = SafeScalarInt("SELECT COUNT(*) FROM topic_folders WHERE parent_folder_id=id;");
        result.InvalidParentLinks = SafeScalarInt(@"SELECT COUNT(*) FROM topic_folders f
            WHERE f.parent_folder_id IS NOT NULL AND NOT EXISTS (
                SELECT 1 FROM topic_folders p WHERE p.id=f.parent_folder_id AND p.topic_id=f.topic_id
            );");

        var rows = LoadFolderRepairPreflightRows(result);
        result.CycleCandidates = CountFolderCycleCandidates(rows);
        result.DuplicateFolderNames = CountDuplicateFolderNameCandidates(rows);
        result.SortOrderProblems = CountFolderSortOrderProblems(rows);
        return result;
    }

    private int SafeScalarInt(string sql)
    {
        try
        {
            return checked((int)ExecuteScalarLong(sql));
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Folder repair preflight count failed", ex);
            return 0;
        }
    }

    private List<FolderRepairRow> LoadFolderRepairPreflightRows(FolderIntegrityPreflightResult result)
    {
        var rows = new List<FolderRepairRow>();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"SELECT id, topic_id, parent_folder_id, name_enc, created_utc, sort_order
                                FROM topic_folders
                                ORDER BY topic_id ASC, parent_folder_id ASC, sort_order ASC, datetime(created_utc) ASC;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = string.Empty;
                try
                {
                    name = DecryptString((byte[])r[3], "folder.name").Trim();
                }
                catch
                {
                    result.BrokenFolderNames++;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    result.BrokenFolderNames++;
                    name = "복구된 폴더";
                }

                rows.Add(new FolderRepairRow
                {
                    Id = r.GetString(0),
                    TopicId = r.IsDBNull(1) ? string.Empty : r.GetString(1),
                    ParentFolderId = r.IsDBNull(2) ? null : r.GetString(2),
                    Name = name,
                    CreatedUtc = DateTime.TryParse(r.GetString(4), out var created) ? created.ToUniversalTime() : DateTime.UtcNow,
                    SortOrder = r.IsDBNull(5) ? 0 : r.GetInt32(5)
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Folder repair preflight row inspection failed", ex);
        }
        return rows;
    }

    private static int CountFolderCycleCandidates(List<FolderRepairRow> rows)
    {
        var cycles = 0;
        var map = rows.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentId = row.Id;
            while (map.TryGetValue(currentId, out var current))
            {
                if (!visited.Add(currentId))
                {
                    cycles++;
                    break;
                }

                if (string.IsNullOrWhiteSpace(current.ParentFolderId))
                    break;
                currentId = current.ParentFolderId;
            }
        }
        return cycles;
    }

    private static int CountDuplicateFolderNameCandidates(List<FolderRepairRow> rows)
    {
        var duplicates = 0;
        foreach (var group in rows.GroupBy(row => row.TopicId + "" + (row.ParentFolderId ?? string.Empty), StringComparer.OrdinalIgnoreCase))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in group.OrderBy(row => row.SortOrder).ThenBy(row => row.CreatedUtc))
            {
                var name = string.IsNullOrWhiteSpace(row.Name) ? "복구된 폴더" : row.Name.Trim();
                if (!used.Add(name))
                    duplicates++;
            }
        }
        return duplicates;
    }

    private static int CountFolderSortOrderProblems(List<FolderRepairRow> rows)
    {
        var problems = 0;
        foreach (var group in rows.GroupBy(row => row.TopicId + "" + (row.ParentFolderId ?? string.Empty), StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderBy(row => row.SortOrder <= 0 ? int.MaxValue : row.SortOrder)
                               .ThenBy(row => row.CreatedUtc)
                               .ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var expected = (i + 1) * 10;
                if (ordered[i].SortOrder != expected)
                    problems++;
            }
        }
        return problems;
    }

    private string? TryCreateFolderRepairSnapshot(string preflightSummary)
    {
        try
        {
            Directory.CreateDirectory(FolderRepairSnapshotDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var dbPath = Path.Combine(FolderRepairSnapshotDirectory, $"catalog-before-folder-repair-{stamp}.db");
            var reportPath = Path.Combine(FolderRepairSnapshotDirectory, $"catalog-before-folder-repair-{stamp}.txt");

            CreateOnlineBackup(dbPath);

            var report = new StringBuilder();
            report.AppendLine("Private Gallery Vault Folder Repair Snapshot");
            report.AppendLine("==========================================");
            report.AppendLine($"CreatedLocal: {DateTime.Now:O}");
            report.AppendLine($"CreatedUtc: {DateTime.UtcNow:O}");
            report.AppendLine("DatabaseSnapshot: [redacted]");
            report.AppendLine($"PreflightSummary: {preflightSummary}");
            report.AppendLine();
            report.AppendLine("This catalog.db snapshot was created automatically before folder integrity repair changed the vault database.");
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);

            CleanupOldFolderRepairSnapshots(FolderRepairSnapshotDirectory, keepPairs: 20);
            AppLogger.Warn("Folder integrity repair snapshot created: " + dbPath + " :: " + preflightSummary);
            return dbPath;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Failed to create folder integrity repair snapshot before automatic repair", ex);
            return null;
        }
    }

    private static void CleanupOldFolderRepairSnapshots(string directory, int keepPairs)
    {
        try
        {
            if (!Directory.Exists(directory))
                return;

            var dbFiles = Directory.EnumerateFiles(directory, "catalog-before-folder-repair-*.db")
                                   .Select(path => new FileInfo(path))
                                   .OrderByDescending(file => file.CreationTimeUtc)
                                   .ToList();
            foreach (var file in dbFiles.Skip(Math.Max(1, keepPairs)))
            {
                try
                {
                    var txtPath = Path.ChangeExtension(file.FullName, ".txt");
                    file.Delete();
                    if (File.Exists(txtPath))
                        File.Delete(txtPath);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static void AppendRecentFolderRepairSnapshots(StringBuilder sb, int limit)
    {
        try
        {
            if (!Directory.Exists(FolderRepairSnapshotDirectory))
            {
                sb.AppendLine("repair_snapshots_recent: none");
                return;
            }

            var files = Directory.EnumerateFiles(FolderRepairSnapshotDirectory, "catalog-before-folder-repair-*.db")
                                 .Select(path => new FileInfo(path))
                                 .OrderByDescending(file => file.CreationTimeUtc)
                                 .Take(Math.Max(1, limit))
                                 .ToList();
            if (files.Count == 0)
            {
                sb.AppendLine("repair_snapshots_recent: none");
                return;
            }

            sb.AppendLine("repair_snapshots_recent:");
            foreach (var file in files)
                sb.AppendLine($"- {file.Name}; size={FormatBytes(file.Length)}; createdUtc={file.CreationTimeUtc:O}");
        }
        catch (Exception ex)
        {
            sb.AppendLine("repair_snapshots_recent: failed (" + ex.Message + ")");
        }
    }

    public FolderIntegrityRepairResult ValidateAndRepairFolderIntegrity(bool writeActivityLog = true)
    {
        var result = new FolderIntegrityRepairResult();
        var preflight = InspectFolderIntegrityForRepair();
        if (preflight.HasPotentialChanges)
        {
            result.BeforeRepairSnapshotPath = TryCreateFolderRepairSnapshot(preflight.ToSummary());
            if (!string.IsNullOrWhiteSpace(result.BeforeRepairSnapshotPath))
                result.BeforeRepairReportPath = Path.ChangeExtension(result.BeforeRepairSnapshotPath, ".txt");
        }

        var now = DateTime.UtcNow.ToString("O");

        using (var deleteOrphans = _connection.CreateCommand())
        {
            deleteOrphans.CommandText = @"DELETE FROM topic_folders
                                          WHERE topic_id IS NULL
                                             OR topic_id=''
                                             OR NOT EXISTS (SELECT 1 FROM topics t WHERE t.id=topic_folders.topic_id);";
            result.OrphanFoldersRemoved = deleteOrphans.ExecuteNonQuery();
        }

        using (var clearInvalidMediaFolder = _connection.CreateCommand())
        {
            clearInvalidMediaFolder.CommandText = @"UPDATE media
                                                    SET folder_id=NULL, updated_utc=$updated
                                                    WHERE folder_id IS NOT NULL
                                                      AND NOT EXISTS (
                                                          SELECT 1 FROM topic_folders f
                                                          WHERE f.id=media.folder_id AND f.topic_id=media.topic_id
                                                      );";
            clearInvalidMediaFolder.Parameters.AddWithValue("$updated", now);
            result.MediaFolderLinksCleared = clearInvalidMediaFolder.ExecuteNonQuery();
        }

        using (var clearSelfParent = _connection.CreateCommand())
        {
            clearSelfParent.CommandText = @"UPDATE topic_folders
                                            SET parent_folder_id=NULL, updated_utc=$updated
                                            WHERE parent_folder_id=id;";
            clearSelfParent.Parameters.AddWithValue("$updated", now);
            result.SelfParentLinksCleared = clearSelfParent.ExecuteNonQuery();
        }

        using (var clearInvalidParent = _connection.CreateCommand())
        {
            clearInvalidParent.CommandText = @"UPDATE topic_folders
                                               SET parent_folder_id=NULL, updated_utc=$updated
                                               WHERE parent_folder_id IS NOT NULL
                                                 AND NOT EXISTS (
                                                     SELECT 1 FROM topic_folders p
                                                     WHERE p.id=topic_folders.parent_folder_id
                                                       AND p.topic_id=topic_folders.topic_id
                                                 );";
            clearInvalidParent.Parameters.AddWithValue("$updated", now);
            result.InvalidParentLinksCleared = clearInvalidParent.ExecuteNonQuery();
        }

        var rows = LoadFolderRepairRows(result);
        result.CyclesBroken += BreakFolderCycles(rows);

        rows = LoadFolderRepairRows(result);
        result.DuplicateFolderNamesRenamed += RenameDuplicateFolderNames(rows);

        rows = LoadFolderRepairRows(result);
        result.SortOrdersNormalized += NormalizeFolderSortOrders(rows);

        if (writeActivityLog && result.HasChanges)
        {
            try
            {
                var detail = result.ToSummary();
                if (!string.IsNullOrWhiteSpace(result.BeforeRepairSnapshotPath))
                    detail += $" | 복구 전 DB 스냅샷: {result.BeforeRepairSnapshotPath}";
                AddActivityLog("folder-repair", "폴더 구조 자동 복구", detail, result: "success", actor: "System");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to write folder repair activity log", ex);
            }
        }

        return result;
    }

    private List<FolderRepairRow> LoadFolderRepairRows(FolderIntegrityRepairResult result)
    {
        var rows = new List<FolderRepairRow>();
        var recoveredNames = new List<(string Id, string Name)>();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT id, topic_id, parent_folder_id, name_enc, created_utc, sort_order
                                FROM topic_folders
                                ORDER BY topic_id ASC, parent_folder_id ASC, sort_order ASC, datetime(created_utc) ASC;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetString(0);
                var name = string.Empty;
                var recoveredName = false;
                try
                {
                    name = DecryptString((byte[])r[3], "folder.name").Trim();
                }
                catch
                {
                    recoveredName = true;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    recoveredName = true;
                    name = "복구된 폴더";
                }

                if (recoveredName)
                    recoveredNames.Add((id, name));

                rows.Add(new FolderRepairRow
                {
                    Id = id,
                    TopicId = r.GetString(1),
                    ParentFolderId = r.IsDBNull(2) ? null : r.GetString(2),
                    Name = name,
                    CreatedUtc = DateTime.TryParse(r.GetString(4), out var created) ? created.ToUniversalTime() : DateTime.UtcNow,
                    SortOrder = r.IsDBNull(5) ? 0 : r.GetInt32(5)
                });
            }
        }

        foreach (var recovered in recoveredNames)
        {
            RenameFolderRaw(recovered.Id, recovered.Name);
            result.FolderNamesRecovered++;
        }

        return rows;
    }

    private int BreakFolderCycles(List<FolderRepairRow> rows)
    {
        var repaired = 0;
        var map = rows.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentId = row.Id;
            while (map.TryGetValue(currentId, out var current))
            {
                if (!visited.Add(currentId))
                {
                    ClearFolderParent(row.Id);
                    row.ParentFolderId = null;
                    repaired++;
                    break;
                }

                if (string.IsNullOrWhiteSpace(current.ParentFolderId))
                    break;
                currentId = current.ParentFolderId;
            }
        }
        return repaired;
    }

    private int RenameDuplicateFolderNames(List<FolderRepairRow> rows)
    {
        var renamed = 0;
        foreach (var group in rows.GroupBy(row => row.TopicId + "" + (row.ParentFolderId ?? string.Empty), StringComparer.OrdinalIgnoreCase))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in group.OrderBy(row => row.SortOrder).ThenBy(row => row.CreatedUtc))
            {
                var baseName = string.IsNullOrWhiteSpace(row.Name) ? "복구된 폴더" : row.Name.Trim();
                var candidate = baseName;
                var suffix = 2;
                while (!used.Add(candidate))
                {
                    candidate = $"{baseName} ({suffix++})";
                }

                if (!string.Equals(candidate, row.Name, StringComparison.Ordinal))
                {
                    RenameFolderRaw(row.Id, candidate);
                    row.Name = candidate;
                    renamed++;
                }
            }
        }
        return renamed;
    }

    private int NormalizeFolderSortOrders(List<FolderRepairRow> rows)
    {
        var changed = 0;
        foreach (var group in rows.GroupBy(row => row.TopicId + "" + (row.ParentFolderId ?? string.Empty), StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderBy(row => row.SortOrder <= 0 ? int.MaxValue : row.SortOrder)
                               .ThenBy(row => row.CreatedUtc)
                               .ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var expected = (i + 1) * 10;
                if (ordered[i].SortOrder == expected)
                    continue;

                SetFolderSortOrderRaw(ordered[i].Id, expected);
                ordered[i].SortOrder = expected;
                changed++;
            }
        }
        return changed;
    }

    private void ClearFolderParent(string folderId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE topic_folders SET parent_folder_id=NULL, updated_utc=$updated WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", folderId);
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private void RenameFolderRaw(string folderId, string name)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE topic_folders SET name_enc=$name, updated_utc=$updated WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", folderId);
        cmd.Parameters.Add("$name", SqliteType.Blob).Value = EncryptString(name, "folder.name");
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private void SetFolderSortOrderRaw(string folderId, int sortOrder)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE topic_folders SET sort_order=$sort, updated_utc=$updated WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", folderId);
        cmd.Parameters.AddWithValue("$sort", sortOrder);
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void UpdateTopicFolderSortOrders(IReadOnlyList<string> orderedIds)
    {
        using var tx = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE topic_folders SET sort_order=$sort, updated_utc=$updated WHERE id=$id;";
        var idParam = cmd.CreateParameter(); idParam.ParameterName = "$id"; cmd.Parameters.Add(idParam);
        var sortParam = cmd.CreateParameter(); sortParam.ParameterName = "$sort"; cmd.Parameters.Add(sortParam);
        var updatedParam = cmd.CreateParameter(); updatedParam.ParameterName = "$updated"; cmd.Parameters.Add(updatedParam);

        var now = DateTime.UtcNow.ToString("O");
        for (var i = 0; i < orderedIds.Count; i++)
        {
            idParam.Value = orderedIds[i];
            sortParam.Value = (i + 1) * 10;
            updatedParam.Value = now;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private void EnsureIndex(string indexName, string tableName, string columns)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"CREATE INDEX IF NOT EXISTS {indexName} ON {tableName}({columns});";
        cmd.ExecuteNonQuery();
    }

    private static string? NormalizeFolderId(string? folderId)
    {
        return string.IsNullOrWhiteSpace(folderId) ? null : folderId.Trim();
    }

    private bool TopicFolderNameExists(string topicId, string? parentFolderId, string name, string? excludeFolderId = null)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(topicId) || string.IsNullOrWhiteSpace(trimmedName))
            return false;

        var normalizedParent = NormalizeFolderId(parentFolderId);
        return GetTopicFolders(topicId, normalizedParent).Any(folder =>
            !string.Equals(folder.Id, excludeFolderId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(folder.Name.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureUniqueTopicFolderName(string topicId, string? parentFolderId, string name, string? excludeFolderId = null)
    {
        if (TopicFolderNameExists(topicId, parentFolderId, name, excludeFolderId))
            throw new InvalidOperationException("같은 위치에 이미 같은 이름의 폴더가 있습니다.");
    }

    public TopicFolder CreateTopicFolder(string topicId, string? parentFolderId, string name)
    {
        if (string.IsNullOrWhiteSpace(topicId))
            throw new InvalidOperationException("주제 정보가 없습니다.");

        var normalizedParent = NormalizeFolderId(parentFolderId);
        ValidateFolderParent(topicId, normalizedParent);

        var trimmedName = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new InvalidOperationException("폴더 이름을 입력해 주세요.");

        EnsureUniqueTopicFolderName(topicId, normalizedParent, trimmedName);

        var now = DateTime.UtcNow;
        var folder = new TopicFolder
        {
            Id = Guid.NewGuid().ToString("N"),
            TopicId = topicId,
            ParentFolderId = normalizedParent,
            Name = trimmedName,
            CreatedUtc = now,
            UpdatedUtc = now,
            SortOrder = GetNextFolderSortOrder(topicId, normalizedParent)
        };

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO topic_folders(id, topic_id, parent_folder_id, name_enc, created_utc, updated_utc, sort_order)
                            VALUES($id, $topic, $parent, $name, $created, $updated, $sort);";
        cmd.Parameters.AddWithValue("$id", folder.Id);
        cmd.Parameters.AddWithValue("$topic", folder.TopicId);
        cmd.Parameters.AddWithValue("$parent", folder.ParentFolderId == null ? DBNull.Value : folder.ParentFolderId);
        cmd.Parameters.Add("$name", SqliteType.Blob).Value = EncryptString(folder.Name, "folder.name");
        cmd.Parameters.AddWithValue("$created", folder.CreatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", folder.UpdatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$sort", folder.SortOrder);
        cmd.ExecuteNonQuery();
        return folder;
    }

    public List<TopicFolder> GetTopicFolders(string topicId, string? parentFolderId = null)
    {
        var list = new List<TopicFolder>();
        if (string.IsNullOrWhiteSpace(topicId))
            return list;

        var normalizedParent = NormalizeFolderId(parentFolderId);
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT f.id, f.topic_id, f.parent_folder_id, f.name_enc, f.created_utc, f.updated_utc, f.sort_order,
                                   (SELECT COUNT(*) FROM topic_folders c WHERE c.parent_folder_id=f.id) AS child_count,
                                   (SELECT COUNT(*) FROM media m WHERE m.folder_id=f.id) AS media_count
                            FROM topic_folders f
                            WHERE f.topic_id=$topic
                              AND ((f.parent_folder_id IS NULL AND $parent IS NULL) OR f.parent_folder_id=$parent)
                            ORDER BY f.sort_order ASC, datetime(f.created_utc) ASC;";
        cmd.Parameters.AddWithValue("$topic", topicId);
        cmd.Parameters.AddWithValue("$parent", normalizedParent == null ? DBNull.Value : normalizedParent);

        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadTopicFolder(r));
        return list;
    }

    public TopicFolder? GetTopicFolderById(string folderId)
    {
        if (string.IsNullOrWhiteSpace(folderId))
            return null;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT f.id, f.topic_id, f.parent_folder_id, f.name_enc, f.created_utc, f.updated_utc, f.sort_order,
                                   (SELECT COUNT(*) FROM topic_folders c WHERE c.parent_folder_id=f.id) AS child_count,
                                   (SELECT COUNT(*) FROM media m WHERE m.folder_id=f.id) AS media_count
                            FROM topic_folders f
                            WHERE f.id=$id;";
        cmd.Parameters.AddWithValue("$id", folderId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadTopicFolder(r) : null;
    }

    public List<string> GetTopicFolderDescendantIds(string folderId, bool includeSelf = true)
    {
        var ids = new List<string>();
        if (string.IsNullOrWhiteSpace(folderId))
            return ids;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = includeSelf
            ? @"WITH RECURSIVE folder_tree(id) AS (
                    SELECT id FROM topic_folders WHERE id=$id
                    UNION ALL
                    SELECT f.id FROM topic_folders f INNER JOIN folder_tree ft ON f.parent_folder_id=ft.id
                )
                SELECT id FROM folder_tree;"
            : @"WITH RECURSIVE folder_tree(id) AS (
                    SELECT id FROM topic_folders WHERE parent_folder_id=$id
                    UNION ALL
                    SELECT f.id FROM topic_folders f INNER JOIN folder_tree ft ON f.parent_folder_id=ft.id
                )
                SELECT id FROM folder_tree;";
        cmd.Parameters.AddWithValue("$id", folderId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            ids.Add(r.GetString(0));
        return ids;
    }

    public void RenameTopicFolder(string folderId, string newName)
    {
        var folder = GetTopicFolderById(folderId) ?? throw new InvalidOperationException("이름을 변경할 폴더를 찾을 수 없습니다.");
        var trimmedName = (newName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new InvalidOperationException("폴더 이름을 입력해 주세요.");

        EnsureUniqueTopicFolderName(folder.TopicId, folder.ParentFolderId, trimmedName, folder.Id);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE topic_folders SET name_enc=$name, updated_utc=$updated WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", folderId);
        cmd.Parameters.Add("$name", SqliteType.Blob).Value = EncryptString(trimmedName, "folder.name");
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void MoveTopicFolder(string folderId, string? newParentFolderId)
    {
        var folder = GetTopicFolderById(folderId) ?? throw new InvalidOperationException("이동할 폴더를 찾을 수 없습니다.");
        var normalizedParent = NormalizeFolderId(newParentFolderId);
        if (string.Equals(folder.Id, normalizedParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("폴더를 자기 자신 안으로 이동할 수 없습니다.");

        if (normalizedParent != null)
        {
            var descendants = GetTopicFolderDescendantIds(folder.Id, includeSelf: false);
            if (descendants.Any(id => string.Equals(id, normalizedParent, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("폴더를 자신의 하위 폴더 안으로 이동할 수 없습니다.");
        }

        ValidateFolderParent(folder.TopicId, normalizedParent);
        EnsureUniqueTopicFolderName(folder.TopicId, normalizedParent, folder.Name, folder.Id);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE topic_folders SET parent_folder_id=$parent, sort_order=$sort, updated_utc=$updated WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", folder.Id);
        cmd.Parameters.AddWithValue("$parent", normalizedParent == null ? DBNull.Value : normalizedParent);
        cmd.Parameters.AddWithValue("$sort", GetNextFolderSortOrder(folder.TopicId, normalizedParent));
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void DeleteTopicFolderMoveContentsToParent(string folderId)
    {
        var folder = GetTopicFolderById(folderId) ?? throw new InvalidOperationException("삭제할 폴더를 찾을 수 없습니다.");
        using var tx = _connection.BeginTransaction();

        using (var moveMedia = _connection.CreateCommand())
        {
            moveMedia.Transaction = tx;
            moveMedia.CommandText = "UPDATE media SET folder_id=$parent, updated_utc=$updated WHERE folder_id=$id;";
            moveMedia.Parameters.AddWithValue("$id", folder.Id);
            moveMedia.Parameters.AddWithValue("$parent", folder.ParentFolderId == null ? DBNull.Value : folder.ParentFolderId);
            moveMedia.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
            moveMedia.ExecuteNonQuery();
        }

        using (var moveChildren = _connection.CreateCommand())
        {
            moveChildren.Transaction = tx;
            moveChildren.CommandText = "UPDATE topic_folders SET parent_folder_id=$parent, updated_utc=$updated WHERE parent_folder_id=$id;";
            moveChildren.Parameters.AddWithValue("$id", folder.Id);
            moveChildren.Parameters.AddWithValue("$parent", folder.ParentFolderId == null ? DBNull.Value : folder.ParentFolderId);
            moveChildren.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
            moveChildren.ExecuteNonQuery();
        }

        using (var delete = _connection.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM topic_folders WHERE id=$id;";
            delete.Parameters.AddWithValue("$id", folder.Id);
            delete.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private void ValidateFolderParent(string topicId, string? parentFolderId)
    {
        if (string.IsNullOrWhiteSpace(parentFolderId))
            return;

        var parent = GetTopicFolderById(parentFolderId);
        if (parent == null || !string.Equals(parent.TopicId, topicId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("선택한 상위 폴더가 현재 주제에 속하지 않습니다.");
    }

    private TopicFolder ReadTopicFolder(SqliteDataReader r)
    {
        return new TopicFolder
        {
            Id = r.GetString(0),
            TopicId = r.GetString(1),
            ParentFolderId = r.IsDBNull(2) ? null : r.GetString(2),
            Name = DecryptString((byte[])r[3], "folder.name"),
            CreatedUtc = DateTime.Parse(r.GetString(4)).ToUniversalTime(),
            UpdatedUtc = DateTime.Parse(r.GetString(5)).ToUniversalTime(),
            SortOrder = r.IsDBNull(6) ? 0 : r.GetInt32(6),
            ChildFolderCount = r.IsDBNull(7) ? 0 : r.GetInt32(7),
            MediaCount = r.IsDBNull(8) ? 0 : r.GetInt32(8)
        };
    }

    public Topic CreateTopic(string name)
    {
        var topic = new Topic
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name.Trim(),
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            SortOrder = GetNextTopicSortOrder()
        };

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO topics(id, name_enc, description_enc, cover_media_id, created_utc, updated_utc, sort_order)
                            VALUES($id, $name, $description, NULL, $created, $updated, $sortOrder);";
        cmd.Parameters.AddWithValue("$id", topic.Id);
        cmd.Parameters.Add("$name", SqliteType.Blob).Value = EncryptString(topic.Name, "topic.name");
        cmd.Parameters.Add("$description", SqliteType.Blob).Value = EncryptString(string.Empty, "topic.description");
        cmd.Parameters.AddWithValue("$created", topic.CreatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", topic.UpdatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$sortOrder", topic.SortOrder);
        cmd.ExecuteNonQuery();
        return topic;
    }

    public void RenameTopic(string topicId, string newName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE topics SET name_enc=$name, updated_utc=$updated WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", topicId);
        cmd.Parameters.Add("$name", SqliteType.Blob).Value = EncryptString(newName.Trim(), "topic.name");
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void SetTopicDescription(string topicId, string description)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE topics SET description_enc=$description, updated_utc=$updated WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", topicId);
        cmd.Parameters.Add("$description", SqliteType.Blob).Value = EncryptString(description.Trim(), "topic.description");
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void SetTopicCoverMedia(string topicId, string? mediaId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE topics SET cover_media_id=$cover, updated_utc=$updated WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", topicId);
        cmd.Parameters.AddWithValue("$cover", string.IsNullOrWhiteSpace(mediaId) ? DBNull.Value : mediaId);
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void DeleteTopic(string topicId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM topics WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", topicId);
        cmd.ExecuteNonQuery();
    }

    public List<Topic> GetTopics()
    {
        var list = new List<Topic>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
SELECT t.id, t.name_enc, t.description_enc, t.cover_media_id, t.created_utc, t.updated_utc, t.sort_order, COUNT(m.id) AS item_count
FROM topics t
LEFT JOIN media m ON m.topic_id = t.id
GROUP BY t.id, t.name_enc, t.description_enc, t.cover_media_id, t.created_utc, t.updated_utc, t.sort_order
ORDER BY t.sort_order ASC, datetime(t.created_utc) ASC;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Topic
            {
                Id = r.GetString(0),
                Name = DecryptString((byte[])r[1], "topic.name"),
                Description = r.IsDBNull(2) ? string.Empty : DecryptString((byte[])r[2], "topic.description"),
                CoverMediaId = r.IsDBNull(3) ? null : r.GetString(3),
                CreatedUtc = DateTime.Parse(r.GetString(4)).ToUniversalTime(),
                UpdatedUtc = DateTime.Parse(r.GetString(5)).ToUniversalTime(),
                SortOrder = r.GetInt32(6),
                ItemCount = r.GetInt32(7)
            });
        }
        return list;
    }

    public void AddMedia(MediaItem item)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO media(
    id, topic_id, folder_id, kind, original_name_enc, extension_enc, object_path, thumb_path, thumb_source_path, source_hash,
    size_bytes, width, height, duration_seconds, favorite, created_utc, updated_utc, sort_order)
VALUES($id, $topic, $folder, $kind, $name, $ext, $object, $thumb, $thumbSource, $sourceHash, $size, $width, $height, $duration, $favorite, $created, $updated, $sortOrder);";
        cmd.Parameters.AddWithValue("$id", item.Id);
        cmd.Parameters.AddWithValue("$topic", item.TopicId);
        cmd.Parameters.AddWithValue("$folder", string.IsNullOrWhiteSpace(item.FolderId) ? DBNull.Value : item.FolderId);
        cmd.Parameters.AddWithValue("$kind", (int)item.Kind);
        cmd.Parameters.Add("$name", SqliteType.Blob).Value = EncryptString(item.OriginalName, "media.name");
        cmd.Parameters.Add("$ext", SqliteType.Blob).Value = EncryptString(item.Extension, "media.ext");
        cmd.Parameters.AddWithValue("$object", item.ObjectPath.Replace('\\', '/'));
        cmd.Parameters.AddWithValue("$thumb", item.ThumbPath.Replace('\\', '/'));
        cmd.Parameters.AddWithValue("$thumbSource", string.IsNullOrWhiteSpace(item.ThumbSourcePath) ? string.Empty : item.ThumbSourcePath.Replace('\\', '/'));
        cmd.Parameters.AddWithValue("$sourceHash", string.IsNullOrWhiteSpace(item.SourceHash) ? string.Empty : item.SourceHash.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$size", item.SizeBytes);
        cmd.Parameters.AddWithValue("$width", item.Width);
        cmd.Parameters.AddWithValue("$height", item.Height);
        cmd.Parameters.AddWithValue("$duration", item.DurationSeconds);
        cmd.Parameters.AddWithValue("$favorite", item.Favorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$created", item.CreatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", item.UpdatedUtc.ToString("O"));
        if (item.SortOrder <= 0)
            item.SortOrder = GetNextMediaSortOrder(item.TopicId, item.FolderId);
        cmd.Parameters.AddWithValue("$sortOrder", item.SortOrder);
        cmd.ExecuteNonQuery();
    }

    public List<MediaItem> GetMedia(string? topicId = null, MediaKind? kind = null)
    {
        var list = new List<MediaItem>();
        using var cmd = _connection.CreateCommand();
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(topicId))
        {
            where.Add("topic_id=$topic");
            cmd.Parameters.AddWithValue("$topic", topicId);
        }
        if (kind.HasValue)
        {
            where.Add("kind=$kind");
            cmd.Parameters.AddWithValue("$kind", (int)kind.Value);
        }
        cmd.CommandText = @"SELECT id, topic_id, kind, original_name_enc, extension_enc, object_path, thumb_path, thumb_source_path, source_hash,
                                  size_bytes, width, height, duration_seconds, favorite, created_utc, updated_utc, sort_order, folder_id
                           FROM media " + (where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "") + " ORDER BY sort_order ASC, datetime(created_utc) DESC;";

        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadMedia(r));
        return list;
    }

    public List<MediaItem> GetMediaInFolder(string topicId, string? folderId = null, MediaKind? kind = null, bool includeDescendants = false)
    {
        var list = new List<MediaItem>();
        if (string.IsNullOrWhiteSpace(topicId))
            return list;

        using var cmd = _connection.CreateCommand();
        var where = new List<string> { "topic_id=$topic" };
        cmd.Parameters.AddWithValue("$topic", topicId);

        var normalizedFolderId = NormalizeFolderId(folderId);
        if (includeDescendants)
        {
            if (normalizedFolderId != null)
            {
                var folderIds = GetTopicFolderDescendantIds(normalizedFolderId, includeSelf: true);
                if (folderIds.Count == 0)
                    return list;

                var names = new List<string>();
                for (var i = 0; i < folderIds.Count; i++)
                {
                    var name = "$folder" + i;
                    names.Add(name);
                    cmd.Parameters.AddWithValue(name, folderIds[i]);
                }
                where.Add("folder_id IN (" + string.Join(",", names) + ")");
            }
        }
        else
        {
            if (normalizedFolderId == null)
                where.Add("folder_id IS NULL");
            else
            {
                where.Add("folder_id=$folder");
                cmd.Parameters.AddWithValue("$folder", normalizedFolderId);
            }
        }

        if (kind.HasValue)
        {
            where.Add("kind=$kind");
            cmd.Parameters.AddWithValue("$kind", (int)kind.Value);
        }

        cmd.CommandText = @"SELECT id, topic_id, kind, original_name_enc, extension_enc, object_path, thumb_path, thumb_source_path, source_hash,
                                  size_bytes, width, height, duration_seconds, favorite, created_utc, updated_utc, sort_order, folder_id
                           FROM media WHERE " + string.Join(" AND ", where) + " ORDER BY sort_order ASC, datetime(created_utc) DESC;";

        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadMedia(r));
        return list;
    }

    public MediaItem? GetMediaById(string id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT id, topic_id, kind, original_name_enc, extension_enc, object_path, thumb_path, thumb_source_path, source_hash,
                                  size_bytes, width, height, duration_seconds, favorite, created_utc, updated_utc, sort_order, folder_id
                           FROM media WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadMedia(r) : null;
    }


    public List<MediaItem> GetMediaBySourceHash(string sourceHash)
    {
        var list = new List<MediaItem>();
        if (string.IsNullOrWhiteSpace(sourceHash))
            return list;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT id, topic_id, kind, original_name_enc, extension_enc, object_path, thumb_path, thumb_source_path, source_hash,
                                  size_bytes, width, height, duration_seconds, favorite, created_utc, updated_utc, sort_order, folder_id
                           FROM media WHERE source_hash=$hash ORDER BY datetime(created_utc) DESC;";
        cmd.Parameters.AddWithValue("$hash", sourceHash.Trim().ToLowerInvariant());
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadMedia(r));
        return list;
    }

    public void MoveMedia(string mediaId, string topicId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE media SET topic_id=$topic, folder_id=NULL, updated_utc=$updated WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", mediaId);
        cmd.Parameters.AddWithValue("$topic", topicId);
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void MoveMediaToFolder(string mediaId, string topicId, string? folderId)
    {
        MoveMediaToFolder(new[] { mediaId }, topicId, folderId);
    }

    public void MoveMediaToFolder(IEnumerable<string> mediaIds, string topicId, string? folderId)
    {
        if (string.IsNullOrWhiteSpace(topicId))
            throw new InvalidOperationException("주제 정보가 없습니다.");

        var normalizedFolderId = NormalizeFolderId(folderId);
        ValidateFolderParent(topicId, normalizedFolderId);

        using var tx = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE media SET topic_id=$topic, folder_id=$folder, sort_order=$sort, updated_utc=$updated WHERE id=$id;";
        var idParam = cmd.CreateParameter(); idParam.ParameterName = "$id"; cmd.Parameters.Add(idParam);
        var topicParam = cmd.CreateParameter(); topicParam.ParameterName = "$topic"; topicParam.Value = topicId; cmd.Parameters.Add(topicParam);
        var folderParam = cmd.CreateParameter(); folderParam.ParameterName = "$folder"; folderParam.Value = normalizedFolderId == null ? DBNull.Value : normalizedFolderId; cmd.Parameters.Add(folderParam);
        var sortParam = cmd.CreateParameter(); sortParam.ParameterName = "$sort"; cmd.Parameters.Add(sortParam);
        var updatedParam = cmd.CreateParameter(); updatedParam.ParameterName = "$updated"; cmd.Parameters.Add(updatedParam);

        var nextSort = GetNextMediaSortOrder(topicId, normalizedFolderId);
        var now = DateTime.UtcNow.ToString("O");
        foreach (var mediaId in mediaIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
        {
            idParam.Value = mediaId;
            sortParam.Value = nextSort;
            updatedParam.Value = now;
            cmd.ExecuteNonQuery();
            nextSort += 10;
        }
        tx.Commit();
    }

    public void SetFavorite(string mediaId, bool favorite)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE media SET favorite=$favorite, updated_utc=$updated WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", mediaId);
        cmd.Parameters.AddWithValue("$favorite", favorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void TouchMedia(string mediaId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE media SET updated_utc=$updated WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", mediaId);
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }


    public void SetThumbnailPaths(string mediaId, string thumbPath, string? thumbSourcePath = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE media SET thumb_path=$thumb, thumb_source_path=$thumbSource, updated_utc=$updated WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", mediaId);
        cmd.Parameters.AddWithValue("$thumb", thumbPath.Replace('\\', '/'));
        cmd.Parameters.AddWithValue("$thumbSource", string.IsNullOrWhiteSpace(thumbSourcePath) ? string.Empty : thumbSourcePath.Replace('\\', '/'));
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void DeleteMedia(string mediaId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM media WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", mediaId);
        cmd.ExecuteNonQuery();
    }

    private MediaItem ReadMedia(SqliteDataReader r)
    {
        return new MediaItem
        {
            Id = r.GetString(0),
            TopicId = r.GetString(1),
            Kind = (MediaKind)r.GetInt32(2),
            OriginalName = DecryptString((byte[])r[3], "media.name"),
            Extension = DecryptString((byte[])r[4], "media.ext"),
            ObjectPath = r.GetString(5),
            ThumbPath = r.GetString(6),
            ThumbSourcePath = r.IsDBNull(7) ? string.Empty : r.GetString(7),
            SourceHash = r.IsDBNull(8) ? string.Empty : r.GetString(8),
            SizeBytes = r.GetInt64(9),
            Width = r.GetInt32(10),
            Height = r.GetInt32(11),
            DurationSeconds = r.GetDouble(12),
            Favorite = r.GetInt32(13) == 1,
            CreatedUtc = DateTime.Parse(r.GetString(14)).ToUniversalTime(),
            UpdatedUtc = DateTime.Parse(r.GetString(15)).ToUniversalTime(),
            SortOrder = r.IsDBNull(16) ? 0 : r.GetInt32(16),
            FolderId = r.FieldCount > 17 && !r.IsDBNull(17) ? r.GetString(17) : null
        };
    }

    public byte[] EncryptString(string text, string aad)
    {
        return CryptoService.EncryptBytes(_masterKey, Encoding.UTF8.GetBytes(text), aad);
    }

    public string DecryptString(byte[] bytes, string aad)
    {
        return Encoding.UTF8.GetString(CryptoService.DecryptBytes(_masterKey, bytes, aad));
    }


    public void AddActivityLog(string actionType, string title, string detail = "", string targetId = "", string targetName = "", string result = "success", string actor = "User")
    {
        var id = Guid.NewGuid().ToString("N");
        var normalizedActor = NormalizeActor(actor);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO activity_logs(id, action_type, title, detail, target_id, target_name, result, actor,
                                                       title_enc, detail_enc, target_name_enc, actor_enc, created_utc)
                            VALUES($id, $action, '[encrypted]', '', $targetId, '', $result, $actor,
                                   $titleEnc, $detailEnc, $targetNameEnc, $actorEnc, $created);";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$action", string.IsNullOrWhiteSpace(actionType) ? "system" : actionType.Trim());
        cmd.Parameters.AddWithValue("$targetId", targetId ?? string.Empty);
        cmd.Parameters.AddWithValue("$result", string.IsNullOrWhiteSpace(result) ? "success" : result.Trim());
        cmd.Parameters.AddWithValue("$actor", normalizedActor);
        cmd.Parameters.AddWithValue("$titleEnc", EncryptActivityLogText(id, "title", title?.Trim() ?? string.Empty));
        cmd.Parameters.AddWithValue("$detailEnc", EncryptActivityLogText(id, "detail", detail?.Trim() ?? string.Empty));
        cmd.Parameters.AddWithValue("$targetNameEnc", EncryptActivityLogText(id, "target_name", targetName?.Trim() ?? string.Empty));
        cmd.Parameters.AddWithValue("$actorEnc", EncryptActivityLogText(id, "actor", normalizedActor));
        cmd.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<ActivityLog> GetActivityLogs(int limit = 300, string? actionType = null)
    {
        var list = new List<ActivityLog>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT id, action_type, title, detail, target_id, target_name, result, actor, created_utc,
                                   title_enc, detail_enc, target_name_enc, actor_enc
                            FROM activity_logs " +
                          (string.IsNullOrWhiteSpace(actionType) ? string.Empty : "WHERE action_type=$action ") +
                          "ORDER BY datetime(created_utc) DESC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", Math.Max(1, Math.Min(limit, 2000)));
        if (!string.IsNullOrWhiteSpace(actionType))
            cmd.Parameters.AddWithValue("$action", actionType.Trim());

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetString(0);
            list.Add(new ActivityLog
            {
                Id = id,
                ActionType = r.GetString(1),
                Title = DecryptActivityLogText(r, 9, id, "title", r.GetString(2)),
                Detail = DecryptActivityLogText(r, 10, id, "detail", r.GetString(3)),
                TargetId = r.GetString(4),
                TargetName = DecryptActivityLogText(r, 11, id, "target_name", r.GetString(5)),
                Result = r.GetString(6),
                Actor = DecryptActivityLogText(r, 12, id, "actor", r.GetString(7)),
                CreatedUtc = DateTime.Parse(r.GetString(8)).ToUniversalTime()
            });
        }
        return list;
    }

    public List<Tag> GetTags()
    {
        var list = new List<Tag>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT t.id, t.name, t.name_enc, t.color, t.created_utc, t.updated_utc, COUNT(mt.media_id) AS item_count
                            FROM tags t
                            LEFT JOIN media_tags mt ON mt.tag_id = t.id
                            GROUP BY t.id, t.name, t.name_enc, t.color, t.created_utc, t.updated_utc;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetString(0);
            var fallbackName = r.GetString(1);
            list.Add(new Tag
            {
                Id = id,
                Name = DecryptTagName(r, 2, id, fallbackName),
                Color = r.GetString(3),
                CreatedUtc = DateTime.Parse(r.GetString(4)).ToUniversalTime(),
                UpdatedUtc = DateTime.Parse(r.GetString(5)).ToUniversalTime(),
                ItemCount = r.GetInt32(6)
            });
        }
        return list.OrderBy(tag => tag.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public Tag CreateTag(string name, string color)
    {
        var displayName = string.IsNullOrWhiteSpace(name) ? "새 태그" : name.Trim();
        var existing = GetTags().FirstOrDefault(tag => string.Equals(tag.Name, displayName, StringComparison.CurrentCultureIgnoreCase));
        if (existing != null)
            return existing;

        var now = DateTime.UtcNow;
        var tag = new Tag
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = displayName,
            Color = string.IsNullOrWhiteSpace(color) ? "#3B82F6" : color.Trim(),
            CreatedUtc = now,
            UpdatedUtc = now
        };

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO tags(id, name, name_enc, color, created_utc, updated_utc)
                            VALUES($id, $placeholder, $nameEnc, $color, $created, $updated);";
        cmd.Parameters.AddWithValue("$id", tag.Id);
        cmd.Parameters.AddWithValue("$placeholder", BuildTagNamePlaceholder(tag.Id));
        cmd.Parameters.AddWithValue("$nameEnc", EncryptTagName(tag.Id, tag.Name));
        cmd.Parameters.AddWithValue("$color", tag.Color);
        cmd.Parameters.AddWithValue("$created", now.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", now.ToString("O"));
        cmd.ExecuteNonQuery();
        return tag;
    }

    public void UpdateTag(string tagId, string name, string color)
    {
        var displayName = string.IsNullOrWhiteSpace(name) ? "새 태그" : name.Trim();
        var duplicate = GetTags().FirstOrDefault(tag => !string.Equals(tag.Id, tagId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(tag.Name, displayName, StringComparison.CurrentCultureIgnoreCase));
        if (duplicate != null)
            throw new InvalidOperationException("이미 같은 이름의 태그가 있습니다.");

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE tags SET name=$placeholder, name_enc=$nameEnc, color=$color, updated_utc=$updated WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", tagId);
        cmd.Parameters.AddWithValue("$placeholder", BuildTagNamePlaceholder(tagId));
        cmd.Parameters.AddWithValue("$nameEnc", EncryptTagName(tagId, displayName));
        cmd.Parameters.AddWithValue("$color", string.IsNullOrWhiteSpace(color) ? "#3B82F6" : color.Trim());
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void DeleteTag(string tagId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM tags WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", tagId);
        cmd.ExecuteNonQuery();
    }

    public void ApplyTagToMedia(string tagId, IEnumerable<string> mediaIds)
    {
        using var tx = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO media_tags(media_id, tag_id) VALUES($media, $tag);";
        var mediaParam = cmd.CreateParameter(); mediaParam.ParameterName = "$media"; cmd.Parameters.Add(mediaParam);
        var tagParam = cmd.CreateParameter(); tagParam.ParameterName = "$tag"; tagParam.Value = tagId; cmd.Parameters.Add(tagParam);
        foreach (var mediaId in mediaIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
        {
            mediaParam.Value = mediaId;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void RemoveTagFromMedia(string tagId, IEnumerable<string> mediaIds)
    {
        using var tx = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM media_tags WHERE media_id=$media AND tag_id=$tag;";
        var mediaParam = cmd.CreateParameter(); mediaParam.ParameterName = "$media"; cmd.Parameters.Add(mediaParam);
        var tagParam = cmd.CreateParameter(); tagParam.ParameterName = "$tag"; tagParam.Value = tagId; cmd.Parameters.Add(tagParam);
        foreach (var mediaId in mediaIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
        {
            mediaParam.Value = mediaId;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public List<Tag> GetTagsForMedia(string mediaId)
    {
        var list = new List<Tag>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT t.id, t.name, t.name_enc, t.color, t.created_utc, t.updated_utc
                            FROM tags t
                            INNER JOIN media_tags mt ON mt.tag_id = t.id
                            WHERE mt.media_id=$media;";
        cmd.Parameters.AddWithValue("$media", mediaId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetString(0);
            var fallbackName = r.GetString(1);
            list.Add(new Tag
            {
                Id = id,
                Name = DecryptTagName(r, 2, id, fallbackName),
                Color = r.GetString(3),
                CreatedUtc = DateTime.Parse(r.GetString(4)).ToUniversalTime(),
                UpdatedUtc = DateTime.Parse(r.GetString(5)).ToUniversalTime()
            });
        }
        return list.OrderBy(tag => tag.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public List<DuplicateGroup> GetDuplicateGroups(int limit = 100)
    {
        var groupRows = new List<(string Hash, int Count, long TotalSize)>();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT source_hash, COUNT(*) AS item_count, SUM(size_bytes) AS total_size
                                FROM media
                                WHERE source_hash <> ''
                                GROUP BY source_hash
                                HAVING COUNT(*) > 1
                                ORDER BY total_size DESC
                                LIMIT $limit;";
            cmd.Parameters.AddWithValue("$limit", Math.Max(1, Math.Min(limit, 500)));
            using var r = cmd.ExecuteReader();
            while (r.Read())
                groupRows.Add((r.GetString(0), r.GetInt32(1), r.GetInt64(2)));
        }

        var groups = new List<DuplicateGroup>();
        foreach (var row in groupRows)
        {
            groups.Add(new DuplicateGroup
            {
                SourceHash = row.Hash,
                Count = row.Count,
                TotalSizeBytes = row.TotalSize,
                Items = GetMediaBySourceHash(row.Hash)
            });
        }
        return groups;
    }


    public void CreateOnlineBackup(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("데이터베이스 백업 경로가 비어 있습니다.", nameof(destinationPath));

        var fullDestination = Path.GetFullPath(destinationPath);
        VaultPaths.EnsureParentDirectory(fullDestination);

        if (File.Exists(fullDestination))
            File.Delete(fullDestination);

        var sourceBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = VaultPaths.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };

        var destinationBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = fullDestination,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };

        SqliteConnection? source = null;
        SqliteConnection? destination = null;

        try
        {
            // Open separate non-pooled SQLite connections and use SQLite's online backup API.
            // Pooling must stay disabled here; otherwise the destination snapshot file can remain
            // locked briefly after Dispose(), and ZipFile may fail with "being used by another process".
            source = new SqliteConnection(sourceBuilder.ToString());
            source.Open();

            using (var busy = source.CreateCommand())
            {
                busy.CommandText = "PRAGMA busy_timeout=5000;";
                busy.ExecuteNonQuery();
            }

            destination = new SqliteConnection(destinationBuilder.ToString());
            destination.Open();

            using (var destinationBusy = destination.CreateCommand())
            {
                destinationBusy.CommandText = "PRAGMA busy_timeout=5000;";
                destinationBusy.ExecuteNonQuery();
            }

            source.BackupDatabase(destination);
        }
        finally
        {
            try { destination?.Close(); } catch { }
            try { source?.Close(); } catch { }
            try
            {
                if (destination != null)
                    SqliteConnection.ClearPool(destination);
                if (source != null)
                    SqliteConnection.ClearPool(source);
            }
            catch
            {
            }
            try { destination?.Dispose(); } catch { }
            try { source?.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
