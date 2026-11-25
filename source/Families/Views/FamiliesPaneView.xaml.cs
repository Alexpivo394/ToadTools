using Families.ViewModels;
using ToadTools.UI.Services;

namespace Families.Views;

/// <summary>Содержимое закрепляемой панели «Семейства». Корень темы ToadDesign — сама панель.</summary>
public sealed partial class FamiliesPaneView
{
    /// <summary>Место списку под карточкой: два семейства.</summary>
    private const double ListReserve = 150;

    /// <summary>Карточка не ниже этого: видны имя и начало картинки, остальное прокручивается.</summary>
    private const double CardMin = 120;

    /// <summary>Отступ, рамка и поля карточки вокруг её прокрутки.</summary>
    private const double CardChrome = 26;

    public FamiliesPaneView(FamiliesPaneViewModel viewModel)
    {
        InitializeComponent();
        ThemePreference.Apply();
        DataContext = viewModel;
        // Пересчёт и при смене размера панели, и когда шапка над списком меняет высоту (компактный размер, предупреждения).
        SizeChanged += (_, _) => FitCard();
        SearchList.SizeChanged += (_, _) => FitCard();
    }

    /// <summary>
    /// Карточка не выше половины панели и оставляет списку место под два семейства: на низком экране шапка
    /// с поиском и фильтрами сама занимает треть высоты, и половина под карточку сжимала список в ноль.
    /// </summary>
    private void FitCard()
    {
        var limit = ActualHeight / 2;
        if (SearchList.IsVisible)
        {
            var listTop = SearchList.TranslatePoint(new System.Windows.Point(), this).Y;
            var status = StatusLine.IsVisible ? StatusLine.ActualHeight + StatusLine.Margin.Top : 0;
            limit = Math.Min(limit, ActualHeight - listTop - ListReserve - status - CardChrome);
        }
        var max = Math.Max(CardMin, limit);
        if (Math.Abs(CardScroll.MaxHeight - max) > 0.5) CardScroll.MaxHeight = max;
    }
}
