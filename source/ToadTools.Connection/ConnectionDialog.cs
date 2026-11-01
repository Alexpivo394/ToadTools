using System.Windows.Interop;
using ToadTools.Connection.ViewModels;
using ToadTools.Connection.Views;
using ToadTools.UI.Models;
using ToadTools.UI.Services;

namespace ToadTools.Connection;

/// <summary>Окно «Подключение к ToadFlow» для команды ленты и плагинов.</summary>
public static class ConnectionDialog
{
    /// <param name="owner">Окно-владелец: главное окно Revit или окно плагина.</param>
    /// <returns>true — после закрытия окна подключение есть.</returns>
    public static bool Show(IntPtr owner)
    {
        var window = new ConnectionWindow(new ConnectionViewModel(ToadFlowClient.Shared, ConnectionStore.Shared));
        if (owner != IntPtr.Zero) _ = new WindowInteropHelper(window) { Owner = owner };
        window.ShowDialog();
        return ConnectionStore.Shared.Load() is not null;
    }

    /// <summary>Подключение есть — сразу true; нет — спрашивает «{reason} — войти?» и открывает окно.</summary>
    public static bool EnsureConnected(IntPtr owner, string reason)
    {
        if (ConnectionStore.Shared.Load() is not null) return true;
        var answer = ToadDialogService.Show("ToadFlow", $"{reason} — войти?", DialogButtons.YesNo, DialogIcon.Info);
        return answer == "Yes" && Show(owner);
    }
}
