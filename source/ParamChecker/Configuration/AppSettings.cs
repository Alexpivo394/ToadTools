namespace ParamChecker.Configuration;

public class AppSettings
{
    public bool IsDarkTheme { get; set; } = true;
    public string? LogFilePath { get; set; } = "";
    public string ReportFilePath { get; set; } = "";
    public string ReportsPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    public bool UpdateGeneralReport { get; set; } = false;
}
