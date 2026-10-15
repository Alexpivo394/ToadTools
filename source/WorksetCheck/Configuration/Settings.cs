namespace WorksetCheck.Configuration;

public class Settings
{
    public bool DarkTheme { get; set; } = true;

    /// <summary>Последние введённые имена — подставляются, если такие РН есть в модели.</summary>
    public string? LinksPrefix { get; set; }

    public string? GridsWorkset { get; set; }
    public string? OpeningsWorkset { get; set; }
}
