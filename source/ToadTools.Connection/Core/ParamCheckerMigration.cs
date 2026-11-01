namespace ToadTools.Connection;

/// <summary>
///     Переход ParamChecker на общее подключение: адрес и токен из его старых настроек один раз переносятся в общее
///     хранилище, чтобы уже настроенные машины не потеряли отправку итогов.
/// </summary>
public static class ParamCheckerMigration
{
    /// <param name="me">Кто владелец токена (<c>/api/auth/me</c>) по адресу и токену; null — сервер не ответил или не принял токен.</param>
    /// <returns>true — перенесено, старые поля можно стереть; false — переносить нечего, подключение уже есть или сервер не ответил.</returns>
    public static bool TryMigrate(ConnectionStore store, string url, string token, Func<string, string, UserInfo?> me)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token)) return false;
        if (store.Load() is not null) return false;

        var root = ToadFlowClient.NormalizeUrl(url);
        var user = me(root, token.Trim());
        if (user is null) return false;

        store.Save(new ToadFlowConnection(root, user.Login, user.DisplayName, user.Role, Guid.Empty, token.Trim()));
        return true;
    }
}
