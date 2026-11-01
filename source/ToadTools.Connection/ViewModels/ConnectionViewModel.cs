using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ToadTools.Connection.ViewModels;

/// <summary>Окно «Подключение к ToadFlow»: вход паролем, кто вошёл, выход.</summary>
public sealed partial class ConnectionViewModel : ObservableObject
{
    private readonly ToadFlowClient _client;
    private readonly ConnectionStore _store;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _url = "";

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _login = "";

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _password = "";

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _error;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SignInCommand), nameof(SignOutCommand))]
    private bool _isBusy;

    public ConnectionViewModel(ToadFlowClient client, ConnectionStore store)
    {
        _client = client;
        _store = store;
        if (store.Load() is { } connection) Show(connection);
    }

    private bool CanSignIn() => !IsBusy && Url.Trim().Length > 0 && Login.Trim().Length > 0 && Password.Length > 0;

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        IsBusy = true;
        Error = null;
        try
        {
            var connection = await _client.SignInAsync(Url, Login, Password, CancellationToken.None);
            Password = "";
            Show(connection);
        }
        catch (SignInException exception)
        {
            Error = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSignOut() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSignOut))]
    private async Task SignOutAsync()
    {
        IsBusy = true;
        Error = null;
        try
        {
            var revoked = await _client.SignOutAsync(CancellationToken.None);
            IsConnected = false;
            Status = revoked ? "Вы вышли" : "Вы вышли. Сервер недоступен — токен останется в списке токенов на сервере";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Проверка в фоне при открытии окна: токен ещё действует, имя и роль не изменились.</summary>
    public async Task CheckAsync()
    {
        if (_store.Load() is not { } connection) return;
        try
        {
            var me = await _client.MeAsync(CancellationToken.None);
            if (me.DisplayName != connection.DisplayName || me.Role != connection.Role || me.Login != connection.Login)
            {
                connection = connection with { DisplayName = me.DisplayName, Role = me.Role, Login = me.Login };
                _store.Save(connection);
                Show(connection);
            }
        }
        catch (ToadFlowAuthRequiredException exception)
        {
            Error = exception.Message;
        }
        catch (Exception exception) when (exception is ToadFlowUnavailableException or ToadFlowRequestException)
        {
            Error = exception.Message;
        }
    }

    private void Show(ToadFlowConnection connection)
    {
        (Url, Login, IsConnected) = (connection.Url, connection.Login, true);
        Status = $"Вы вошли как {connection.DisplayName} ({Roles.Label(connection.Role)}) на {connection.Url}";
    }
}
