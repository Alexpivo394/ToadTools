#nullable enable
using ToadDesign;
using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToadTools.UI.Models;

namespace ToadTools.UI.ViewModels;

public partial class ToadDialogViewModel : ObservableObject
{
    private string _title = "";
    private string _message = "";
    private Symbol _icon = Symbol.HelpCircle;

    public string Title { get => _title; set => SetProperty(ref _title, value); }
    public string Message { get => _message; set => SetProperty(ref _message, value); }
    public Symbol Icon { get => _icon; set => SetProperty(ref _icon, value); }

    public string? Result { get; private set; }

    public ObservableCollection<DialogButtonModel> Buttons { get; } = new();

    /// <summary>Сигнал вьюхе «закрой окно»</summary>
    public event EventHandler? RequestClose;

    private void Close(string? result)
    {
        Result = result;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    public void BuildButtons(DialogButtons buttons)
    {
        void AddBtn(string? text, Symbol icon)
        {
            Buttons.Add(new DialogButtonModel
            {
                Text = text,
                Icon = icon,
                Command = new RelayCommand(() => Close(text))
            });
        }

        switch (buttons)
        {
            case DialogButtons.OK:
                AddBtn("OK", Symbol.Check);
                break;
            case DialogButtons.OKCancel:
                AddBtn("OK", Symbol.Check);
                AddBtn("Cancel", Symbol.X);
                break;
            case DialogButtons.YesNo:
                AddBtn("Yes", Symbol.Check);
                AddBtn("No", Symbol.X);
                break;
            case DialogButtons.YesNoCancel:
                AddBtn("Yes", Symbol.Check);
                AddBtn("No", Symbol.X);
                AddBtn("Cancel", Symbol.ArrowBackUp);
                break;
            case DialogButtons.RetryCancel:
                AddBtn("Retry", Symbol.Refresh);
                AddBtn("Cancel", Symbol.X);
                break;
            case DialogButtons.RetryAbort:
                AddBtn("Retry", Symbol.Refresh);
                AddBtn("Abort", Symbol.CircleX);
                break;
        }
    }
}
