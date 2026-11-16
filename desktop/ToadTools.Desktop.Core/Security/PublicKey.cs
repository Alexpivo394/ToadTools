namespace ToadTools.Desktop.Core.Security;

/// <summary>
///     Публичный ключ подписи релизов ToadTools (ECDSA P-256, Base64 от X||Y). Пара к секрету SIGNING_KEY; новая пара —
///     dotnet run --project build -- keygen &lt;файл&gt;. Смена ключа: старые программы перестанут принимать новые релизы,
///     поэтому сначала выпускается программа с новым ключом, подписанная старым.
/// </summary>
public static class PublicKey
{
    public const string Value = "b20h/ecvyB3Ug1f+3hK/5ytXTG7y7W2zShtsu1OC04MaNl974wuJIHwTG5Mu+eX60hvblXePa1hRypgglF5ekQ==";
}
