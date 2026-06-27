using System.Text;
using Microsoft.Data.Sqlite;
using PrivateGalleryVault.Models;

namespace PrivateGalleryVault.Services;

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
CREATE TABLE IF NOT EXISTS media (
    id TEXT PRIMARY KEY,
    topic_id TEXT NOT NULL,
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
    FOREIGN KEY(topic_id) REFERENCES topics(id) ON DELETE CASCADE
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
    created_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS tags (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
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
        EnsureColumn("media", "thumb_source_path", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("media", "source_hash", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("media", "sort_order", "INTEGER NOT NULL DEFAULT 0");
        EnsureIndex("idx_topics_sort", "topics", "sort_order, created_utc");
        EnsureIndex("idx_media_sort", "media", "topic_id, sort_order, created_utc");
        EnsureIndex("idx_media_source_hash", "media", "source_hash");
        EnsureIndex("idx_activity_created", "activity_logs", "created_utc DESC");
        EnsureIndex("idx_activity_action", "activity_logs", "action_type, created_utc DESC");
        EnsureIndex("idx_media_tags_tag", "media_tags", "tag_id, media_id");
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

    private void EnsureIndex(string indexName, string tableName, string columns)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"CREATE INDEX IF NOT EXISTS {indexName} ON {tableName}({columns});";
        cmd.ExecuteNonQuery();
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
    id, topic_id, kind, original_name_enc, extension_enc, object_path, thumb_path, thumb_source_path, source_hash,
    size_bytes, width, height, duration_seconds, favorite, created_utc, updated_utc, sort_order)
VALUES($id, $topic, $kind, $name, $ext, $object, $thumb, $thumbSource, $sourceHash, $size, $width, $height, $duration, $favorite, $created, $updated, $sortOrder);";
        cmd.Parameters.AddWithValue("$id", item.Id);
        cmd.Parameters.AddWithValue("$topic", item.TopicId);
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
            item.SortOrder = GetNextMediaSortOrder(item.TopicId);
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
                                  size_bytes, width, height, duration_seconds, favorite, created_utc, updated_utc, sort_order
                           FROM media " + (where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "") + " ORDER BY sort_order ASC, datetime(created_utc) DESC;";

        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadMedia(r));
        return list;
    }

    public MediaItem? GetMediaById(string id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT id, topic_id, kind, original_name_enc, extension_enc, object_path, thumb_path, thumb_source_path, source_hash,
                                  size_bytes, width, height, duration_seconds, favorite, created_utc, updated_utc, sort_order
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
                                  size_bytes, width, height, duration_seconds, favorite, created_utc, updated_utc, sort_order
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
        cmd.CommandText = "UPDATE media SET topic_id=$topic, updated_utc=$updated WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", mediaId);
        cmd.Parameters.AddWithValue("$topic", topicId);
        cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
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
            SortOrder = r.IsDBNull(16) ? 0 : r.GetInt32(16)
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
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO activity_logs(id, action_type, title, detail, target_id, target_name, result, actor, created_utc)
                            VALUES($id, $action, $title, $detail, $targetId, $targetName, $result, $actor, $created);";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$action", string.IsNullOrWhiteSpace(actionType) ? "system" : actionType.Trim());
        cmd.Parameters.AddWithValue("$title", title.Trim());
        cmd.Parameters.AddWithValue("$detail", detail?.Trim() ?? string.Empty);
        cmd.Parameters.AddWithValue("$targetId", targetId ?? string.Empty);
        cmd.Parameters.AddWithValue("$targetName", targetName ?? string.Empty);
        cmd.Parameters.AddWithValue("$result", string.IsNullOrWhiteSpace(result) ? "success" : result.Trim());
        cmd.Parameters.AddWithValue("$actor", string.IsNullOrWhiteSpace(actor) ? "User" : actor.Trim());
        cmd.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<ActivityLog> GetActivityLogs(int limit = 300, string? actionType = null)
    {
        var list = new List<ActivityLog>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT id, action_type, title, detail, target_id, target_name, result, actor, created_utc
                            FROM activity_logs " +
                          (string.IsNullOrWhiteSpace(actionType) ? string.Empty : "WHERE action_type=$action ") +
                          "ORDER BY datetime(created_utc) DESC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", Math.Max(1, Math.Min(limit, 2000)));
        if (!string.IsNullOrWhiteSpace(actionType))
            cmd.Parameters.AddWithValue("$action", actionType.Trim());

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ActivityLog
            {
                Id = r.GetString(0),
                ActionType = r.GetString(1),
                Title = r.GetString(2),
                Detail = r.GetString(3),
                TargetId = r.GetString(4),
                TargetName = r.GetString(5),
                Result = r.GetString(6),
                Actor = r.GetString(7),
                CreatedUtc = DateTime.Parse(r.GetString(8)).ToUniversalTime()
            });
        }
        return list;
    }

    public List<Tag> GetTags()
    {
        var list = new List<Tag>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT t.id, t.name, t.color, t.created_utc, t.updated_utc, COUNT(mt.media_id) AS item_count
                            FROM tags t
                            LEFT JOIN media_tags mt ON mt.tag_id = t.id
                            GROUP BY t.id, t.name, t.color, t.created_utc, t.updated_utc
                            ORDER BY LOWER(t.name) ASC;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Tag
            {
                Id = r.GetString(0),
                Name = r.GetString(1),
                Color = r.GetString(2),
                CreatedUtc = DateTime.Parse(r.GetString(3)).ToUniversalTime(),
                UpdatedUtc = DateTime.Parse(r.GetString(4)).ToUniversalTime(),
                ItemCount = r.GetInt32(5)
            });
        }
        return list;
    }

    public Tag CreateTag(string name, string color)
    {
        var now = DateTime.UtcNow;
        var tag = new Tag
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name.Trim(),
            Color = string.IsNullOrWhiteSpace(color) ? "#3B82F6" : color.Trim(),
            CreatedUtc = now,
            UpdatedUtc = now
        };

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"INSERT OR IGNORE INTO tags(id, name, color, created_utc, updated_utc)
                            VALUES($id, $name, $color, $created, $updated);";
        cmd.Parameters.AddWithValue("$id", tag.Id);
        cmd.Parameters.AddWithValue("$name", tag.Name);
        cmd.Parameters.AddWithValue("$color", tag.Color);
        cmd.Parameters.AddWithValue("$created", now.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", now.ToString("O"));
        cmd.ExecuteNonQuery();
        return tag;
    }

    public void UpdateTag(string tagId, string name, string color)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE tags SET name=$name, color=$color, updated_utc=$updated WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", tagId);
        cmd.Parameters.AddWithValue("$name", name.Trim());
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
        cmd.CommandText = @"SELECT t.id, t.name, t.color, t.created_utc, t.updated_utc
                            FROM tags t
                            INNER JOIN media_tags mt ON mt.tag_id = t.id
                            WHERE mt.media_id=$media
                            ORDER BY LOWER(t.name) ASC;";
        cmd.Parameters.AddWithValue("$media", mediaId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Tag
            {
                Id = r.GetString(0),
                Name = r.GetString(1),
                Color = r.GetString(2),
                CreatedUtc = DateTime.Parse(r.GetString(3)).ToUniversalTime(),
                UpdatedUtc = DateTime.Parse(r.GetString(4)).ToUniversalTime()
            });
        }
        return list;
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

    public void Dispose()
    {
        _connection.Dispose();
    }
}
