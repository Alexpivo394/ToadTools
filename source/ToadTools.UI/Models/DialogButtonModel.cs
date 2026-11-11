using ToadDesign;
using System.Windows.Input;

namespace ToadTools.UI.Models;


public class DialogButtonModel
{
    public string? Text { get; set; } = "";
    public Symbol Icon { get; set; }
    public ICommand? Command { get; set; }
}