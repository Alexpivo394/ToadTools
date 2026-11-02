namespace ToadTools.Connection;

/// <summary>Токен не принят (отозван, пользователь отключён) или входа не было — нужно войти заново. Подключение не стирается.</summary>
public sealed class ToadFlowAuthRequiredException() : Exception("Подключение к ToadFlow устарело — войдите заново");

/// <summary>Сервер не ответил: нет сети, неверный адрес или тайм-аут.</summary>
public sealed class ToadFlowUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Сервер отказал: текст — из его ответа <c>{ error }</c>.</summary>
public sealed class ToadFlowRequestException(int status, string message, Exception? inner = null) : Exception(message, inner)
{
    public int Status { get; } = status;
}

/// <summary>Вход не удался; текст — для человека.</summary>
public sealed class SignInException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Текст ошибки для человека.</summary>
public static class ToadFlowErrors
{
    /// <summary>
    ///     Ошибки ToadFlow и входа уже написаны для человека; всё прочее — непредвиденное: его текст показывается с
    ///     пометкой, чтобы панель или окно не падали и не зависали.
    /// </summary>
    public static string Describe(Exception exception) => exception switch
    {
        ToadFlowAuthRequiredException or ToadFlowUnavailableException or ToadFlowRequestException or SignInException => exception.Message,
        _ => $"Непредвиденная ошибка: {exception.Message}"
    };
}
