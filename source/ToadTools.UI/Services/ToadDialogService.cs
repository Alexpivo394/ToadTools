#nullable enable
using ToadDesign;
using System.Windows;
using ToadTools.UI.Models;
using ToadTools.UI.ViewModels;
using ToadTools.UI.Views;

namespace ToadTools.UI.Services;

public class ToadDialogService
{
    public static string? Show(string title, string message, DialogButtons buttons, DialogIcon icon = DialogIcon.None, Window? owner = null)
    {
        var vm = new ToadDialogViewModel
        {
            Title = title,
            Message = message,
            Icon = icon switch
            {
                DialogIcon.Info => Symbol.InfoCircle,
                DialogIcon.Warning => Symbol.AlertTriangle,
                DialogIcon.Error => Symbol.CircleX,
                _ => Symbol.HelpCircle
            }
        };
        vm.BuildButtons(buttons);

        var dlg = new ToadDialog
        {
            DataContext = vm,
            Owner = owner ?? Application.Current?.MainWindow
        };

        if (dlg.ShowDialog() == true)
            return vm.Result;

        return null;
    }
}