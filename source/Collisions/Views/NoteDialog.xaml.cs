using Collisions.ViewModels;
using ToadTools.UI.Services;

namespace Collisions.Views;

/// <summary>Окно примечания к «Фиктивная…», «Передать…» и «Примечание».</summary>
public sealed partial class NoteDialog
{
    public NoteDialog(NoteViewModel viewModel)
    {
        
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Saved += (_, _) => DialogResult = true;
        Loaded += (_, _) => NoteBox.Focus();
    }
}
