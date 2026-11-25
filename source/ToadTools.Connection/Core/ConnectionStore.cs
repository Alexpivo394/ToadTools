using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToadTools.Connection;

/// <summary>
///     Подключение к ToadFlow в файле <c>%AppData%\ToadTools\toadflow.json</c>, общем для всех плагинов. Токен лежит
///     зашифрованным для текущего пользователя Windows (DPAPI): с другого компьютера или под другим пользователем файл не
///     читается, и нужно войти заново.
/// </summary>
public sealed class ConnectionStore(string path)
{
    /// <summary>Имя файла подключения; рядом на время записи появляется <c>toadflow.json.tmp</c>.</summary>
    public const string FileName = "toadflow.json";

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToadTools", FileName);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Хранилище всех плагинов ToadTools.</summary>
    public static ConnectionStore Shared { get; } = new(DefaultPath);

    /// <summary>Подключение сохранено или стёрто.</summary>
    public event EventHandler? Changed;

    public ToadFlowConnection? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            var file = JsonSerializer.Deserialize<StoredConnection>(File.ReadAllText(path), Json);
            if (file is null || string.IsNullOrWhiteSpace(file.Url) || string.IsNullOrWhiteSpace(file.TokenProtected)) return null;
            var token = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(file.TokenProtected), null, DataProtectionScope.CurrentUser));
            return new ToadFlowConnection(file.Url, file.Login ?? "", file.DisplayName ?? "", file.Role ?? "", file.TokenId, token);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or CryptographicException
                                              or FormatException)
        {
            return null; // файл повреждён или с другого компьютера — как будто входа не было
        }
    }

    public void Save(ToadFlowConnection connection)
    {
        var file = new StoredConnection
        {
            Url = connection.Url,
            Login = connection.Login,
            DisplayName = connection.DisplayName,
            Role = connection.Role,
            TokenId = connection.TokenId,
            TokenProtected = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(connection.Token), null, DataProtectionScope.CurrentUser))
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, Json), new UTF8Encoding(false));
        // Замена одним шагом: другой плагин, читающий файл в этот момент, видит старое подключение или новое, но не пустоту.
        if (File.Exists(path)) File.Replace(temp, path, null);
        else File.Move(temp, path);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (File.Exists(path)) File.Delete(path);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class StoredConnection
    {
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("login")] public string? Login { get; set; }
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("role")] public string? Role { get; set; }
        [JsonPropertyName("tokenId")] public Guid TokenId { get; set; }
        [JsonPropertyName("tokenProtected")] public string TokenProtected { get; set; } = "";
    }
}
