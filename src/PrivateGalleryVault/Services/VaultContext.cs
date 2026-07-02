namespace PrivateGalleryVault.Services;

public sealed class VaultContext : IDisposable
{
    public byte[] MasterKey { get; }
    public DatabaseService Database { get; }
    public MediaVaultService Media { get; }
    public ActivityLogService ActivityLogs { get; }
    public TagService Tags { get; }
    public DuplicateService Duplicates { get; }
    public BackupRestoreService Backups { get; }

    public VaultContext(byte[] masterKey)
    {
        MasterKey = masterKey;
        Database = new DatabaseService(masterKey);
        Database.Initialize();
        Media = new MediaVaultService(masterKey, Database);
        ActivityLogs = new ActivityLogService(Database);
        Tags = new TagService(Database);
        Duplicates = new DuplicateService(Database);
        Backups = new BackupRestoreService(Database);
    }

    public void Dispose()
    {
        Database.Dispose();
        CryptographicZero(MasterKey);
    }

    private static void CryptographicZero(byte[] bytes)
    {
        if (bytes.Length > 0)
            Array.Clear(bytes, 0, bytes.Length);
    }
}
