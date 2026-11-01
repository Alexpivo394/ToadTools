namespace ToadTools.Connection;

/// <summary>Токен не принят (отозван, пользователь отключён) или входа не было — нужно войти заново. Подключение не стирается.</summary>
public sealed class ToadFlowAuthRequiredException() : Exception("Подключение к ToadFlow устарело — войдите заново");

/// <summary>Сервер не ответил: нет сети, неверный адрес или тайм-аут.</summary>
public sealed class ToadFlowUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Сервер отказал: текст — из его ответа <c>{ error }</c>.</summary>
public sealed class ToadFlowRequestException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>Вход не удался; текст — для человека.</summary>
public sealed class SignInException(string message, Exception? inner = null) : Exception(message, inner);
