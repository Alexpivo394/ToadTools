using System.Security.Cryptography;
using Shouldly;
using ToadTools.Desktop.Core.Security;

namespace ToadTools.Desktop.Tests.Security;

public class SignatureVerifierTests
{
    [Test]
    public void ValidSignature_Verifies()
    {
        using var temp = new TempFolder();
        using var key = TestKeys.Create();
        var file = temp.Combine("ToadTools-4.0.3.zip");
        File.WriteAllBytes(file, [1, 2, 3, 4, 5]);

        new SignatureVerifier(TestKeys.PublicQ(key)).Verify(file, TestKeys.Sign(key, file)).ShouldBeTrue();
    }

    [Test]
    public void ChangedFile_DoesNotVerify()
    {
        using var temp = new TempFolder();
        using var key = TestKeys.Create();
        var file = temp.Combine("a.zip");
        File.WriteAllBytes(file, [1, 2, 3, 4, 5]);
        var signature = TestKeys.Sign(key, file);
        File.WriteAllBytes(file, [1, 2, 3, 4, 6]);

        new SignatureVerifier(TestKeys.PublicQ(key)).Verify(file, signature).ShouldBeFalse();
    }

    [Test]
    public void OtherKey_DoesNotVerify()
    {
        using var temp = new TempFolder();
        using var key = TestKeys.Create();
        using var other = TestKeys.Create();
        var file = temp.Combine("a.zip");
        File.WriteAllBytes(file, [1, 2, 3]);

        new SignatureVerifier(TestKeys.PublicQ(key)).Verify(file, TestKeys.Sign(other, file)).ShouldBeFalse();
    }

    [Test]
    [Arguments("не base64")]
    [Arguments("")]
    [Arguments("AAAA")]
    public void Garbage_DoesNotVerify(string signature)
    {
        using var temp = new TempFolder();
        using var key = TestKeys.Create();
        var file = temp.Combine("a.zip");
        File.WriteAllBytes(file, [1, 2, 3]);

        new SignatureVerifier(TestKeys.PublicQ(key)).Verify(file, signature).ShouldBeFalse();
    }

    [Test]
    public void SignatureFileWithNewline_Verifies()
    {
        using var temp = new TempFolder();
        using var key = TestKeys.Create();
        var file = temp.Combine("a.zip");
        File.WriteAllBytes(file, [9, 9, 9]);

        new SignatureVerifier(TestKeys.PublicQ(key)).Verify(file, TestKeys.Sign(key, file) + "\r\n").ShouldBeTrue();
    }

    [Test]
    public void EmbeddedPublicKey_Is64Bytes() => Convert.FromBase64String(PublicKey.Value).Length.ShouldBe(64);
}

/// <summary>Ключи ECDSA P-256 для тестов: как у сборки релиза, только на месте.</summary>
public static class TestKeys
{
    public static ECDsa Create() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static byte[] PublicQ(ECDsa key)
    {
        var parameters = key.ExportParameters(false);
        return [..parameters.Q.X!, ..parameters.Q.Y!];
    }

    public static string Sign(ECDsa key, string file)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(file);
        return Convert.ToBase64String(key.SignHash(sha.ComputeHash(stream)));
    }
}
