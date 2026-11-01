using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using Collisions.Core;
using Collisions.ViewModels;

namespace Collisions.Views;

/// <summary>Окна карточки поверх главного окна Revit.</summary>
public sealed class CardDialogs(IntPtr owner) : ICardDialogs
{
    public string? AskNote(string title)
    {
        var model = new NoteViewModel(title);
        return Show(model) ? model.Text : null;
    }

    public PassChoice? AskPass(IReadOnlyList<SideOption> options)
    {
        var model = new NoteViewModel("Передать", options);
        return Show(model) && model.Side is { } side ? new PassChoice(side, model.Text) : null;
    }

    public void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Не получилось открыть браузер: {exception.Message}\n{url}", "Коллизии ToadFlow");
        }
    }

    private bool Show(NoteViewModel model)
    {
        var dialog = new NoteDialog(model);
        if (owner != IntPtr.Zero) _ = new WindowInteropHelper(dialog) { Owner = owner };
        return dialog.ShowDialog() == true;
    }
}
