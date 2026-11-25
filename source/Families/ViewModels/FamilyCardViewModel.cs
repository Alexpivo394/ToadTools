using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Families.Core;

namespace Families.ViewModels;

/// <summary>Вариант семейства в карточке: «Основная» (<see cref="IterationId" /> null) или итерация.</summary>
/// <param name="Hash">Хеш действующей версии варианта: с ним сверяется файл перед загрузкой.</param>
/// <param name="Locked">Загружают только координаторы (семейство или итерация).</param>
public sealed record FamilyVariant(
    Guid? IterationId,
    string Label,
    string? Comment,
    string FullPath,
    Guid? CurrentVersionId,
    string? Hash,
    int? RevitYear,
    FamilyContentDto? Content,
    bool IsFinal,
    string? FinalComment,
    bool Locked)
{
    public override string ToString() => Label;
}

/// <summary>
///     Карточка выбранного семейства: вариант («Основная» или итерация), картинка, типоразмеры с галочками, история версий варианта, путь.
/// </summary>
public sealed partial class FamilyCardViewModel : ObservableObject
{
    public const string BaseLabel = "Основная";
    private static readonly CultureInfo Russian = new("ru-RU");
    private readonly int _revitYear;
    private readonly bool _coordinator;
    private readonly List<FamilyVariant> _all;

    /// <param name="coordinator">Вошёл координатор: ему доступно и то, что «только координаторам».</param>
    public FamilyCardViewModel(FamilyCardDto card, int revitYear, bool coordinator = false)
    {
        Card = card;
        _revitYear = revitYear;
        _coordinator = coordinator;
        Subtitle = string.Join(" · ", new[] { card.Category, card.Section, card.TypeCount > 0 ? Texts.Types(card.TypeCount) : "" }.Where(s => s.Length > 0));
        var current = card.Current;
        Updated = $"Обновлено {card.UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy", Russian)}" + (current?.AuthorName is { } author ? $", {author}" : "");
        Loads = card.LoadCount > 0 ? $"Загружали в проекты: {card.LoadCount}" : "";
        Unreviewed = card.State == "InReview";

        _all = [new FamilyVariant(null, BaseLabel, null, card.FullPath, current?.Id, current?.Hash, card.RevitYear, card.Content, false, null, card.CoordinatorsOnly)];
        foreach (var iteration in (card.Iterations ?? []).Where(i => i.RemovedAt is null && i.CurrentVersionId is not null).OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
            _all.Add(new FamilyVariant(iteration.Id, iteration.IsFinal ? $"{iteration.Name} (завершена)" : iteration.Name, iteration.Comment, iteration.FullPath,
                iteration.CurrentVersionId, iteration.CurrentHash, iteration.RevitYear, iteration.Content, iteration.IsFinal, iteration.FinalComment,
                card.CoordinatorsOnly || iteration.CoordinatorsOnly));
        HasIterations = _all.Count > 1;
        HasFinished = _all.Any(v => v.IsFinal);
        FillVariants();
        _selectedVariant = Variants[0];
        Fill();
    }

    public FamilyCardDto Card { get; }

    public string Name => Card.Name;

    public string Subtitle { get; }

    public string Updated { get; }

    public string Loads { get; }

    public string? Description => Card.Description;

    /// <summary>Есть итерации: показывается выбор варианта.</summary>
    public bool HasIterations { get; }

    /// <summary>Есть завершённые итерации: показывается «показать завершённые».</summary>
    public bool HasFinished { get; }

    /// <summary>«Основная» и незавершённые итерации; с <see cref="ShowFinished" /> — и завершённые.</summary>
    public ObservableCollection<FamilyVariant> Variants { get; } = [];

    [ObservableProperty] private bool _showFinished;

    [ObservableProperty] private FamilyVariant _selectedVariant;

    public string FullPath => SelectedVariant.FullPath;

    /// <summary>Комментарий выбранной итерации и, если она завершена, что в ней проделано.</summary>
    public string VariantNote => string.Join(Environment.NewLine,
        new[] { SelectedVariant.Comment ?? "", SelectedVariant.IsFinal ? $"Итерация завершена: {SelectedVariant.FinalComment}" : "" }.Where(s => s.Length > 0));

    /// <summary>Семейство на проверке — проектировщик его видит, только включив «непроверенные».</summary>
    public bool Unreviewed { get; }

    /// <summary>Почему загрузить нельзя; null — можно.</summary>
    public string? BlockedReason { get; private set; }

    public bool CanLoad => BlockedReason is null;

    /// <summary>Выбранная итерация завершена: перед загрузкой — вопрос.</summary>
    public bool IsFinal => SelectedVariant.IsFinal;

    public ObservableCollection<TypeChoice> Types { get; } = [];

    public string TypesNote { get; private set; } = "";

    public IReadOnlyList<string> History { get; private set; } = [];

    [ObservableProperty] private byte[]? _preview;

    /// <summary>Выбранные типоразмеры; null — все (содержимое не прочитано или выбраны все).</summary>
    public IReadOnlyCollection<string>? ChosenTypes =>
        Types.Count == 0 || Types.All(t => t.IsChosen) ? null : Types.Where(t => t.IsChosen).Select(t => t.Name).ToList();

    public bool NothingChosen => Types.Count > 0 && Types.All(t => !t.IsChosen);

    /// <summary>Выбрать вариант по итерации (null — «Основная»); false — такого нет.</summary>
    public bool Select(Guid? iterationId)
    {
        if (_all.FirstOrDefault(v => v.IterationId == iterationId) is not { } variant) return false;
        if (variant.IsFinal) ShowFinished = true;
        SelectedVariant = Variants.First(v => v.IterationId == iterationId);
        return true;
    }

    partial void OnShowFinishedChanged(bool value)
    {
        var selected = SelectedVariant;
        FillVariants();
        SelectedVariant = Variants.FirstOrDefault(v => v.IterationId == selected.IterationId) ?? Variants[0];
    }

    partial void OnSelectedVariantChanged(FamilyVariant value)
    {
        if (value is null) return;
        Fill();
        OnPropertyChanged(nameof(FullPath));
        OnPropertyChanged(nameof(VariantNote));
        OnPropertyChanged(nameof(BlockedReason));
        OnPropertyChanged(nameof(CanLoad));
        OnPropertyChanged(nameof(IsFinal));
        OnPropertyChanged(nameof(TypesNote));
        OnPropertyChanged(nameof(History));
    }

    private void FillVariants()
    {
        Variants.Clear();
        foreach (var variant in _all.Where(v => ShowFinished || !v.IsFinal)) Variants.Add(variant);
    }

    /// <summary>Типоразмеры, история и что мешает загрузке — по выбранному варианту.</summary>
    private void Fill()
    {
        var variant = SelectedVariant;
        BlockedReason = variant.RevitYear is { } year && year > _revitYear ? Texts.TooNew(year)
            : Card.State == "Archived" ? "Семейство убрано в архив"
            : variant.Locked && !_coordinator ? "Загружают только координаторы"
            : variant.FullPath.Length == 0 ? "Путь к файлу недоступен"
            : null;

        Types.Clear();
        if (variant.Content is { } content)
        {
            foreach (var type in content.Types)
                Types.Add(new TypeChoice(type.Name, string.Join(", ", type.Values.Take(3).Select(v => $"{v.Key}: {v.Value}"))));
            TypesNote = content.TypesFromCatalog ? "Типоразмеры из каталога .txt" : "";
        }
        else
            TypesNote = "Содержимое ещё читается фермой — загрузятся все типоразмеры";

        History = Card.Versions.Where(v => v.IterationId == variant.IterationId).Select(v =>
        {
            var what = string.Join(" — ", new[] { v.Note, v.Comment ?? "" }.Where(s => s.Length > 0));
            var who = v.AuthorName ?? "автор неизвестен";
            var state = v.State switch { "InReview" => " · на проверке", "Rejected" => " · отклонена", _ => "" };
            return $"{v.SeenAt.ToLocalTime().ToString("dd.MM.yyyy", Russian)} · {who}{state}{(what.Length > 0 ? " · " + what : "")}"
                   + (v.Id == variant.CurrentVersionId ? " · действующая" : "");
        }).ToList();
    }

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
