using ToadTools.UI.Services;
using System;
using System.Windows;
using ToadDesign;

namespace ToadTools.UI.Views;

/// <summary>Message box of the add-ins: title, icon, message and the buttons of <see cref="Models.DialogButtons" />.</summary>
public partial class ToadDialog : ToadWindow
{
    private const double MaxDialogHeightRatio = 0.8;
    private const double ChromeHeight = 160;

    public ToadDialog()
    {
        InitializeComponent();
        ThemePreference.Apply();

        // A long message scrolls instead of growing the dialog past the screen.
        var workArea = SystemParameters.WorkArea;
        MaxHeight = workArea.Height * MaxDialogHeightRatio;
        MessageScrollViewer.MaxHeight = Math.Max(80, MaxHeight - ChromeHeight);

        Loaded += (_, _) =>
        {
            if (DataContext is ViewModels.ToadDialogViewModel vm)
            {
                vm.RequestClose += (_, _) =>
                {
                    DialogResult = true;
                    Close();
                };
            }
        };
    }
}
