namespace ToadTools.Connection.ViewModels;

/// <summary>Действия окна «Настройки» вне приложения: проводник и браузер. Тесты подставляют свою запись вызовов.</summary>
public interface ISettingsShell
{
    /// <summary>Открывает папку в проводнике.</summary>
    void OpenFolder(string path);

    /// <summary>Открывает ссылку в браузере по умолчанию.</summary>
    void OpenUrl(string url);

    /// <summary>Открывает проводник с выделенным файлом.</summary>
    void ShowFile(string path);
}
