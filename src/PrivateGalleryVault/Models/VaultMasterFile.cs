namespace PrivateGalleryVault.Models;

public sealed class VaultMasterFile
{
    public int Version { get; set; } = 1;
    public string Kdf { get; set; } = "PBKDF2-HMACSHA256";
    public int Iterations { get; set; } = 300_000;
    public string SaltBase64 { get; set; } = string.Empty;
    public string WrapNonceBase64 { get; set; } = string.Empty;
    public string WrapTagBase64 { get; set; } = string.Empty;
    public string WrappedMasterKeyBase64 { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
