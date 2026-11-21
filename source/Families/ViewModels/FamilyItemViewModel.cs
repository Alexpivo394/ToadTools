using CommunityToolkit.Mvvm.ComponentModel;
using Families.Core;

namespace Families.ViewModels;

/// <summary>Строка выдачи поиска: картинка подгружается сама, когда строка появилась.</summary>
public sealed partial class FamilyItemViewModel : ObservableObject
{
    public FamilyItemViewModel(FamilyHit hit, int revitYear)
    {
        Hit = hit;
        var parts = new List<string>();
        if (hit.Category.Length > 0) parts.Add(hit.Category);
        if (hit.Section.Length > 0) parts.Add(hit.Section);
        if (hit.TypeCount > 0) parts.Add(Texts.Types(hit.TypeCount));
        Details = string.Join(" · ", parts);
        YearBadge = hit.RevitYear is { } year && year != 2022 ? $"R{year}" : "";
        TooNew = hit.RevitYear is { } saved && saved > revitYear;
        Unreviewed = hit.State == "InReview";
    }

    public FamilyHit Hit { get; }

    public string Name => Hit.Name;

    public string Details { get; }

    /// <summary>Год Revit, если не 2022 (стандарт отдела): «R2024».</summary>
    public string YearBadge { get; }

    /// <summary>Сохранено в Revit новее запущенного: загрузить нельзя.</summary>
    public bool TooNew { get; }

    public bool Unreviewed { get; }

    [ObservableProperty] private byte[]? _preview;

    public async Task LoadPreviewAsync(PreviewCache previews)
    {
        if (Hit.PreviewHash is not { } hash || Preview is not null) return;
        Preview = await previews.GetAsync(Hit.Id, hash);
    }
}

/// <summary>Типоразмер в карточке: галочка «загрузить».</summary>
public sealed partial class TypeChoice(string name, string details) : ObservableObject
{
    public string Name { get; } = name;

    /// <summary>Два-три значения параметров типоразмера — чтобы отличать похожие имена.</summary>
    public string Details { get; } = details;

    [ObservableProperty] private bool _isChosen = true;
}

/// <summary>Слова панели в одном месте.</summary>
public static class Texts
{
    public static string Types(int n) => $"{n} {Plural(n, "типоразмер", "типоразмера", "типоразмеров")}";

    public static string Plural(int n, string one, string few, string many)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        return mod10 == 1 && mod100 != 11 ? one : mod10 is >= 2 and <= 4 && (mod100 < 12 || mod100 > 14) ? few : many;
    }

    public static string TooNew(int year) => $"Сохранено в Revit {year} — загрузится только в Revit {year} и новее";
}
