using System.Windows;
using ClassifierCode.Engine.Library;
using ToadTools.UI.Services;

namespace ClassifierCode.Views;

/// <summary>Список общих папок профилей.</summary>
public sealed partial class ProfileFoldersView
{
    public ProfileFoldersView(IEnumerable<string> folders)
    {
        InitializeComponent();
        ThemePreference.Apply();
        FoldersBox.Text = string.Join(Environment.NewLine, folders);
        PersonalText.Text = "Личная папка: " + ProfileLibrary.DefaultPersonalFolder;
    }

    public IReadOnlyList<string> Folders =>
        FoldersBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
