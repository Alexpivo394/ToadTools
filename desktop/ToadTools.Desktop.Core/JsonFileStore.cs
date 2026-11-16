using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ToadTools.Desktop.Core;

/// <summary>
///     Объект в JSON-файле. Запись — во временный файл и замена, чтобы выключение компьютера не оставило половину
///     файла. Нет файла — новый объект; битый файл переименовывается в .bad, и тоже новый объект.
/// </summary>
public class JsonFileStore<T>(string file) where T : new()
{
    private static readonly JsonSerializerSettings Json = new()
    {
        Formatting = Formatting.Indented,
        Converters = [new StringEnumConverter()],
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        ObjectCreationHandling = ObjectCreationHandling.Replace
    };

    public string File { get; } = file;

    public T Load()
    {
        if (!System.IO.File.Exists(File)) return new T();
        try
        {
            return JsonConvert.DeserializeObject<T>(System.IO.File.ReadAllText(File), Json) ?? new T();
        }
        catch (JsonException exception)
        {
            Log.Error($"{File} не читается, беру значения по умолчанию", exception);
            try
            {
                System.IO.File.Copy(File, File + ".bad", true);
                System.IO.File.Delete(File);
            }
            catch (Exception copy) when (copy is IOException or UnauthorizedAccessException)
            {
            }

            return new T();
        }
    }

    public void Save(T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        var temporary = File + ".tmp";
        System.IO.File.WriteAllText(temporary, JsonConvert.SerializeObject(value, Json));
        if (System.IO.File.Exists(File)) System.IO.File.Replace(temporary, File, null);
        else System.IO.File.Move(temporary, File);
    }
}
