using System.Security.Cryptography;
using System.Text;
using System.Linq;

namespace PrivateGalleryVault.Services;

public static class KeyDerivationService
{
    public const int SaltSize = 16;
    public const int KeySize = 32;
    public const int DefaultIterations = 300_000;

    public static byte[] CreateSalt()
    {
        return RandomNumberGenerator.GetBytes(SaltSize);
    }

    public static byte[] DerivePasswordKey(string password, byte[] salt, int iterations)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("비밀번호가 비어 있습니다.", nameof(password));

        using var kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256);
        return kdf.GetBytes(KeySize);
    }

    public static bool LooksWeak(string password)
    {
        if (password.Length < 8) return true;
        var hasLetter = password.Any(char.IsLetter);
        var hasDigit = password.Any(char.IsDigit);
        var hasSymbol = password.Any(ch => !char.IsLetterOrDigit(ch));
        return !(hasLetter && hasDigit) || !hasSymbol;
    }
}
