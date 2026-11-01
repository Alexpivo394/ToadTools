namespace ToadTools.Connection;

/// <summary>Подключение к ToadFlow: адрес, кто вошёл и личный токен этого компьютера.</summary>
/// <param name="Url">Адрес без хвостового <c>/</c>, со схемой: <c>http://toadflow:8080</c>.</param>
/// <param name="Role">Роль на сервере: <c>Viewer</c>, <c>Operator</c>, <c>Admin</c>, <c>Designer</c>.</param>
/// <param name="TokenId">Id токена на сервере; пусто — токен перенесён из настроек ParamChecker.</param>
public sealed record ToadFlowConnection(string Url, string Login, string DisplayName, string Role, Guid TokenId, string Token);

/// <summary>Пользователь ToadFlow (<c>/api/auth/me</c>).</summary>
/// <param name="Disciplines">Разделы профиля: по ним веб и плагин прячут действия с чужими коллизиями.</param>
public sealed record UserInfo(Guid Id, string Login, string DisplayName, string Role, string[] Disciplines)
{
    public bool IsViewer => Role == Roles.Viewer;
    public bool IsDesigner => Role == Roles.Designer;

    /// <summary>Координатор или администратор.</summary>
    public bool IsStaff => Role is Roles.Operator or Roles.Admin;
}

/// <summary>Роли ToadFlow и их названия.</summary>
public static class Roles
{
    public const string Viewer = "Viewer";
    public const string Operator = "Operator";
    public const string Admin = "Admin";
    public const string Designer = "Designer";

    public static string Label(string role) => role switch
    {
        Viewer => "Наблюдатель",
        Operator => "Координатор",
        Admin => "Администратор",
        Designer => "Проектировщик",
        _ => role
    };
}
