using System.Windows;
using System.Windows.Controls;

namespace ParamChecker.ViewModels.Conditions;

public class ConditionTemplateSelector : DataTemplateSelector
{
    // Set from XAML
    public DataTemplate SimpleTemplate { get; set; } = null!;
    public DataTemplate GroupTemplate { get; set; } = null!;

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            SimpleConditionViewModel => SimpleTemplate,
            GroupConditionViewModel => GroupTemplate,
            _ => base.SelectTemplate(item, container)
        };
    }
}