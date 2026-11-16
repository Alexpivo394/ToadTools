using System.Security.Cryptography;

namespace Build;

/// <summary>
///     Ключи подписи релизов (ECDSA P-256). Приватный — Base64 PKCS#8 в секрете SIGNING_KEY (Publish:SigningKey),
///     публичный — Base64 X||Y в desktop/ToadTools.Desktop.Core/Security/PublicKey.cs.
/// </summary>
public static class SigningKeys
{
    /// <summary>dotnet run -- keygen &lt;файл&gt;: пишет новую пару ключей в файл (не в репозиторий!).</summary>
    public static void Generate(string file)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var q = key.ExportParameters(false).Q;
        File.WriteAllLines(file,
        [
            "SIGNING_KEY (секрет GitHub Actions и user-secrets Publish:SigningKey, никуда больше):",
            Convert.ToBase64String(key.ExportPkcs8PrivateKey()),
            "",
            "PublicKey.Value (desktop/ToadTools.Desktop.Core/Security/PublicKey.cs):",
            Convert.ToBase64String([..q.X!, ..q.Y!])
        ]);
    }

    /// <summary>Подпись файла: Base64 от подписи P1363 над SHA-256 файла.</summary>
    public static string Sign(string privateKeyPkcs8Base64, string file)
    {
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyPkcs8Base64.Trim()), out _);
        using var stream = File.OpenRead(file);
        var hash = SHA256.HashData(stream);
        return Convert.ToBase64String(key.SignHash(hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
}
