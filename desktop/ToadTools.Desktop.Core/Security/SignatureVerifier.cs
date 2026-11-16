using System.Security.Cryptography;

namespace ToadTools.Desktop.Core.Security;

/// <summary>
///     Проверка подписи файла релиза. Файл {имя}.sig — Base64 от 64 байт подписи ECDSA P-256 (IEEE P1363) над SHA-256
///     файла. Подписывает сборка релиза приватным ключом из секрета SIGNING_KEY, проверяет — публичный ключ программы.
/// </summary>
public sealed class SignatureVerifier
{
    private readonly ECParameters _publicKey;

    /// <param name="publicKeyQ">Точка публичного ключа: 32 байта X, затем 32 байта Y.</param>
    public SignatureVerifier(byte[] publicKeyQ)
    {
        if (publicKeyQ.Length != 64) throw new ArgumentException("Публичный ключ P-256 — 64 байта (X и Y)", nameof(publicKeyQ));
        _publicKey = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = publicKeyQ.Take(32).ToArray(), Y = publicKeyQ.Skip(32).ToArray() }
        };
    }

    /// <summary>Ключ, зашитый в программу.</summary>
    public static SignatureVerifier Embedded() => new(Convert.FromBase64String(PublicKey.Value));

    /// <summary>Подпись сходится с файлом. Мусор вместо подписи — false, без исключений.</summary>
    public bool Verify(string filePath, string signatureBase64)
    {
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        if (signature.Length != 64) return false;

        byte[] hash;
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(filePath))
        {
            hash = sha.ComputeHash(stream);
        }

        try
        {
            using var key = ECDsa.Create(_publicKey);
            return key.VerifyHash(hash, signature);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
