using System.Security.Cryptography;
using System.Text;

namespace PrivateGalleryVault.Services;

public static class CryptoService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static byte[] EncryptBytes(byte[] key, byte[] plain, string? aadText = null)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        var aad = aadText == null ? null : Encoding.UTF8.GetBytes(aadText);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag, aad);

        var result = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, result, NonceSize + TagSize, cipher.Length);
        return result;
    }

    public static byte[] DecryptBytes(byte[] key, byte[] encrypted, string? aadText = null)
    {
        if (encrypted.Length < NonceSize + TagSize)
            throw new CryptographicException("암호화 데이터가 손상되었습니다.");

        var nonce = encrypted.AsSpan(0, NonceSize).ToArray();
        var tag = encrypted.AsSpan(NonceSize, TagSize).ToArray();
        var cipher = encrypted.AsSpan(NonceSize + TagSize).ToArray();
        var plain = new byte[cipher.Length];
        var aad = aadText == null ? null : Encoding.UTF8.GetBytes(aadText);
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain, aad);
        return plain;
    }
}
