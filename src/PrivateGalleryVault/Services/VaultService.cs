using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PrivateGalleryVault.Models;

namespace PrivateGalleryVault.Services;

public sealed class VaultService
{
    private const string MasterKeyAad = "PrivateGalleryVault.MasterKey.v1";

    public bool VaultExists() => File.Exists(VaultPaths.MasterFilePath) && File.Exists(VaultPaths.DatabasePath);

    public VaultContext CreateVault(string password)
    {
        if (VaultExists())
            throw new InvalidOperationException("이미 Vault가 존재합니다.");
        ValidatePassword(password);

        VaultPaths.EnsureBaseDirectories();
        var masterKey = RandomNumberGenerator.GetBytes(32);
        WriteWrappedMasterKey(masterKey, password, DateTime.UtcNow);

        var context = new VaultContext(masterKey);
        context.Database.CreateTopic("미분류");
        AppSettingsService.Save(new AppSettings());
        return context;
    }

    public VaultContext Unlock(string password)
    {
        var masterKey = DecryptMasterKey(password);
        return new VaultContext(masterKey);
    }

    public void ChangePassword(string currentPassword, string newPassword)
    {
        ValidatePassword(newPassword);
        var currentMaster = LoadMasterFile();
        var masterKey = DecryptMasterKey(currentPassword, currentMaster);
        try
        {
            WriteWrappedMasterKey(masterKey, newPassword, currentMaster.CreatedUtc);
        }
        finally
        {
            Array.Clear(masterKey, 0, masterKey.Length);
        }
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new InvalidOperationException("비밀번호는 최소 8자 이상이어야 합니다.");
    }

    private static VaultMasterFile LoadMasterFile()
    {
        if (!File.Exists(VaultPaths.MasterFilePath))
            throw new FileNotFoundException("Vault master.json을 찾을 수 없습니다.");

        return JsonSerializer.Deserialize<VaultMasterFile>(File.ReadAllText(VaultPaths.MasterFilePath))
               ?? throw new InvalidOperationException("master.json을 읽을 수 없습니다.");
    }

    private static byte[] DecryptMasterKey(string password)
    {
        return DecryptMasterKey(password, LoadMasterFile());
    }

    private static byte[] DecryptMasterKey(string password, VaultMasterFile master)
    {
        var salt = Convert.FromBase64String(master.SaltBase64);
        var passwordKey = KeyDerivationService.DerivePasswordKey(password, salt, master.Iterations);

        var nonce = Convert.FromBase64String(master.WrapNonceBase64);
        var tag = Convert.FromBase64String(master.WrapTagBase64);
        var cipher = Convert.FromBase64String(master.WrappedMasterKeyBase64);
        var packed = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, packed, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, packed, nonce.Length + tag.Length, cipher.Length);

        try
        {
            return CryptoService.DecryptBytes(passwordKey, packed, MasterKeyAad);
        }
        catch (CryptographicException)
        {
            throw new UnauthorizedAccessException("비밀번호가 올바르지 않거나 Vault가 손상되었습니다.");
        }
        finally
        {
            Array.Clear(passwordKey, 0, passwordKey.Length);
        }
    }

    private static void WriteWrappedMasterKey(byte[] masterKey, string password, DateTime createdUtc)
    {
        var salt = KeyDerivationService.CreateSalt();
        var iterations = KeyDerivationService.DefaultIterations;
        var passwordKey = KeyDerivationService.DerivePasswordKey(password, salt, iterations);
        try
        {
            var encrypted = CryptoService.EncryptBytes(passwordKey, masterKey, MasterKeyAad);
            var master = new VaultMasterFile
            {
                Version = 1,
                Kdf = "PBKDF2-HMACSHA256",
                Iterations = iterations,
                SaltBase64 = Convert.ToBase64String(salt),
                WrapNonceBase64 = Convert.ToBase64String(encrypted.AsSpan(0, 12)),
                WrapTagBase64 = Convert.ToBase64String(encrypted.AsSpan(12, 16)),
                WrappedMasterKeyBase64 = Convert.ToBase64String(encrypted.AsSpan(28)),
                CreatedUtc = createdUtc
            };

            var json = JsonSerializer.Serialize(master, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(VaultPaths.MasterFilePath, json);
        }
        finally
        {
            Array.Clear(passwordKey, 0, passwordKey.Length);
        }
    }
}
