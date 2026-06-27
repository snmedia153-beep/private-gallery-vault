using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Linq;

namespace PrivateGalleryVault.Services;

public static class FileCryptoService
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PVG1");
    private const int Version = 1;
    private const int ChunkSize = 1024 * 1024;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static void EncryptFile(byte[] key, string sourcePath, string encryptedPath)
    {
        VaultPaths.EnsureParentDirectory(encryptedPath);
        using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(encryptedPath, FileMode.Create, FileAccess.Write, FileShare.None);
        EncryptStream(key, input, output, input.Length);
    }

    public static void EncryptBytesToFile(byte[] key, byte[] data, string encryptedPath)
    {
        VaultPaths.EnsureParentDirectory(encryptedPath);
        using var input = new MemoryStream(data);
        using var output = new FileStream(encryptedPath, FileMode.Create, FileAccess.Write, FileShare.None);
        EncryptStream(key, input, output, data.LongLength);
    }

    public static byte[] DecryptFileToBytes(byte[] key, string encryptedPath)
    {
        using var input = new FileStream(encryptedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new MemoryStream();
        DecryptStream(key, input, output);
        return output.ToArray();
    }

    public static void DecryptFileToPath(byte[] key, string encryptedPath, string outputPath)
    {
        VaultPaths.EnsureParentDirectory(outputPath);
        using var input = new FileStream(encryptedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        DecryptStream(key, input, output);
    }

    private static void EncryptStream(byte[] key, Stream input, Stream output, long originalLength)
    {
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(ChunkSize);
        writer.Write(originalLength);

        var buffer = new byte[ChunkSize];
        using var aes = new AesGcm(key, TagSize);
        while (true)
        {
            var read = input.Read(buffer, 0, buffer.Length);
            if (read <= 0) break;

            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var plain = buffer.AsSpan(0, read);
            var cipher = new byte[read];
            var tag = new byte[TagSize];
            aes.Encrypt(nonce, plain, cipher, tag);

            writer.Write(read);
            writer.Write(nonce);
            writer.Write(tag);
            writer.Write(cipher);
        }
    }

    private static void DecryptStream(byte[] key, Stream input, Stream output)
    {
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        var magic = reader.ReadBytes(4);
        if (!magic.SequenceEqual(Magic))
            throw new CryptographicException("지원하지 않는 보관함 파일 형식입니다.");

        var version = reader.ReadInt32();
        if (version != Version)
            throw new CryptographicException("지원하지 않는 보관함 파일 버전입니다.");

        _ = reader.ReadInt32(); // chunk size
        _ = reader.ReadInt64(); // original length

        using var aes = new AesGcm(key, TagSize);
        while (input.Position < input.Length)
        {
            var plainLen = reader.ReadInt32();
            if (plainLen < 0 || plainLen > ChunkSize)
                throw new CryptographicException("암호화 청크가 손상되었습니다.");

            var nonce = reader.ReadBytes(NonceSize);
            var tag = reader.ReadBytes(TagSize);
            var cipher = reader.ReadBytes(plainLen);
            if (nonce.Length != NonceSize || tag.Length != TagSize || cipher.Length != plainLen)
                throw new CryptographicException("암호화 파일이 중간에 잘렸거나 손상되었습니다.");

            var plain = new byte[plainLen];
            aes.Decrypt(nonce, cipher, tag, plain);
            output.Write(plain, 0, plain.Length);
        }
    }
}
