using System.Diagnostics;
using System.IO;
using System.Windows;
using Families.Core;

namespace Families.Views;

/// <summary>Вопросы и сообщения поверх Revit, браузер и проводник.</summary>
public sealed class FamilyDialogs : IFamilyDialogs
{
    private const string Title = "Семейства ToadFlow";

    public bool Confirm(string text) => MessageBox.Show(text, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Info(string text) => MessageBox.Show(text, Title);

    public void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Info("Адрес ToadFlow неизвестен — войдите в ToadFlow заново");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Info($"Не получилось открыть браузер: {exception.Message}\n{url}");
        }
    }

    public void ShowInExplorer(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(Path.GetDirectoryName(path))) Process.Start("explorer.exe", $"\"{Path.GetDirectoryName(path)}\"");
            else Info($"Папки «{Path.GetDirectoryName(path)}» не видно — проверьте доступ к библиотеке");
        }
        catch (Exception exception)
        {
            Info($"Не получилось открыть проводник: {exception.Message}");
        }
    }
}
