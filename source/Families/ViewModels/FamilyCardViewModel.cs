using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Families.Core;

namespace Families.ViewModels;

/// <summary>Карточка выбранного семейства: картинка, типоразмеры с галочками, история версий, путь.</summary>
public sealed partial class FamilyCardViewModel : ObservableObject
{
    private static readonly CultureInfo Russian = new("ru-RU");

    public FamilyCardViewModel(FamilyCardDto card, int revitYear)
    {
        Card = card;
        Subtitle = string.Join(" · ", new[] { card.Category, card.Section, card.TypeCount > 0 ? Texts.Types(card.TypeCount) : "" }.Where(s => s.Length > 0));
        var current = card.Current;
        Updated = $"Обновлено {card.UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy", Russian)}" + (current?.AuthorName is { } author ? $", {author}" : "");
        Loads = card.LoadCount > 0 ? $"Загружали в проекты: {card.LoadCount}" : "";
        Unreviewed = card.State == "InReview";
        BlockedReason = card.RevitYear is { } year && year > revitYear ? Texts.TooNew(year)
            : card.State == "Archived" ? "Семейство убрано в архив"
            : null;

        if (card.Content is { } content)
        {
            foreach (var type in content.Types)
                Types.Add(new TypeChoice(type.Name, string.Join(", ", type.Values.Take(3).Select(v => $"{v.Key}: {v.Value}"))));
            TypesNote = content.TypesFromCatalog ? "Типоразмеры из каталога .txt" : "";
        }
        else
            TypesNote = "Содержимое ещё читается фермой — загрузятся все типоразмеры";

        History = card.Versions.Select(v =>
        {
            var what = string.Join(" — ", new[] { v.Note, v.Comment ?? "" }.Where(s => s.Length > 0));
            var who = v.AuthorName ?? "автор неизвестен";
            var state = v.State switch { "InReview" => " · на проверке", "Rejected" => " · отклонена", _ => "" };
            return $"{v.SeenAt.ToLocalTime().ToString("dd.MM.yyyy", Russian)} · {who}{state}{(what.Length > 0 ? " · " + what : "")}{(v.IsCurrent ? " · действующая" : "")}";
        }).ToList();
    }

    public FamilyCardDto Card { get; }

    public string Name => Card.Name;

    public string Subtitle { get; }

    public string Updated { get; }

    public string Loads { get; }

    public string? Description => Card.Description;

    public string FullPath => Card.FullPath;

    /// <summary>Семейство на проверке — проектировщик его видит, только включив «непроверенные».</summary>
    public bool Unreviewed { get; }

    /// <summary>Почему загрузить нельзя; null — можно.</summary>
    public string? BlockedReason { get; }

    public bool CanLoad => BlockedReason is null;

    public ObservableCollection<TypeChoice> Types { get; } = [];

    public string TypesNote { get; }

    public IReadOnlyList<string> History { get; }

    [ObservableProperty] private byte[]? _preview;

    /// <summary>Выбранные типоразмеры; null — все (содержимое не прочитано или выбраны все).</summary>
    public IReadOnlyCollection<string>? ChosenTypes =>
        Types.Count == 0 || Types.All(t => t.IsChosen) ? null : Types.Where(t => t.IsChosen).Select(t => t.Name).ToList();

    public bool NothingChosen => Types.Count > 0 && Types.All(t => !t.IsChosen);

    [RelayCommand]
    private void ChooseAll()
    {
        foreach (var type in Types) type.IsChosen = true;
    }

    [RelayCommand]
    private void ChooseNone()
    {
        foreach (var type in Types) type.IsChosen = false;
    }
}
