using CommunityToolkit.Mvvm.ComponentModel;
using Collisions.Core;

namespace Collisions.ViewModels;

/// <summary>Строка списка коллизий. Миниатюра грузится при первом обращении — то есть когда строка показалась на экране.</summary>
public sealed partial class CollisionItemViewModel(ModelCollisionRow row, ThumbnailCache thumbnails) : ObservableObject
{
    public const string MineGroup = "Мои и общие";
    public const string OtherGroup = "Взяла другая сторона";

    private bool _thumbnailRequested;

    [ObservableProperty] private byte[]? _thumbnail;
    [ObservableProperty] private bool _thumbnailFailed;

    /// <summary>Новая для меня: я её ещё не открывал — точка у строки; гаснет, когда карточка открыта.</summary>
    [ObservableProperty] private bool _unseen = row.Unseen;

    public string Dot => Unseen ? "● " : "";

    public ModelCollisionRow Row => row;
    public int Number => row.Collision.Number;
    public string Group => row.MineToFix ? MineGroup : OtherGroup;
    public string Status => StatusLabel(row.Collision.Status);
    public string Depth => $"{row.Collision.DepthMm:0} мм";
    public string Pair => $"{row.Collision.DisciplineA} × {row.Collision.DisciplineB}";
    public string? Level => row.Own.Level;
    public string Own => row.Own.Title;
    public string Other => $"{row.Other.ModelName}: {row.Other.Title}";
    public string? Taken => row.TakenBy is { } who ? $"взял(а) {who}" : null;

    /// <summary>Картинка коллизии; null — ещё рисуется или не получилась (тогда заглушка).</summary>
    public byte[]? Image
    {
        get
        {
            if (!_thumbnailRequested)
            {
                _thumbnailRequested = true;
                _ = LoadThumbnailAsync();
            }

            return Thumbnail;
        }
    }

    partial void OnThumbnailChanged(byte[]? value) => OnPropertyChanged(nameof(Image));

    partial void OnUnseenChanged(bool value) => OnPropertyChanged(nameof(Dot));

    private async Task LoadThumbnailAsync()
    {
        try
        {
            Thumbnail = await thumbnails.GetAsync(row.Collision.Id, row.Collision.ImageVersion, CancellationToken.None);
        }
        catch (Exception)
        {
            ThumbnailFailed = true; // заглушка остаётся; при «Обновить» строка создаётся заново и попробует ещё раз
        }
    }

    /// <summary>Подписи статусов — как в веб-отчёте.</summary>
    public static string StatusLabel(CollisionStatus status) => status switch
    {
        CollisionStatus.New => "новая",
        CollisionStatus.Active => "активная",
        CollisionStatus.Returned => "вернулась",
        CollisionStatus.FixedPending => "исправлена (ждёт)",
        CollisionStatus.Fake => "фиктивная",
        CollisionStatus.Fixed => "исправлена",
        CollisionStatus.GoneByRules => "ушла из-за правил",
        CollisionStatus.Unchecked => "не проверена",
        _ => status.ToString()
    };
}
