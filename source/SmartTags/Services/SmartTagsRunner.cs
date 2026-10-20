using System.Diagnostics;
using System.Globalization;
using SmartTags.Layout;
using SmartTags.Models;

namespace SmartTags.Services;

/// <summary>
///     Запуск расстановки марок на виде:
///     сбор элементов → создание марок → измерение их текста → раскладка → установка выносок.
///     Всё в одной группе транзакций: отмена или ошибка откатывают запуск целиком.
/// </summary>
public sealed class SmartTagsRunner(TagView view, SmartTagsSettings settings)
{
    private readonly RunLog _log = new(settings.WriteLog);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TagView _view = view;

    // Больше элементов у одной подписи — выноски сливаются в веник.
    private const int MaxSharedLabel = 4;

    /// <summary>Цели последнего запуска — для диагностики.</summary>
    internal List<TagTarget> LastTargets { get; } = [];

    /// <summary>Раскладка последнего запуска — для диагностики.</summary>
    internal LayoutResult? LastLayout { get; private set; }

    /// <summary>Задача раскладки последнего запуска — для диагностики.</summary>
    internal LayoutRequest? LastRequest { get; private set; }

    public RunResult Run(IRunProgress progress)
    {
        var result = new RunResult();
        var targets = LastTargets;
        LayoutResult? layout = null;
        WriteHeader();

        try
        {
            // Семейства марок открываются только вне транзакций — до начала запуска.
            var metrics = new TagTextMetrics(_view.Document, _log.Writer);
            metrics.Load(settings.Choices.Select(choice => choice.TagType.Id));

            using var transactions = new TransactionRunner(_view.Document, "Умные выноски", _log.Writer);

            if (_view.IsUnlocked3D)
                Lock3DView(transactions, result);

            Stage(progress, "Сбор элементов");
            var collector = new TargetCollector(_view, settings, _log.Writer);
            foreach (var choice in settings.Choices)
            {
                var elements = collector.CollectElements(choice);
                var built = collector.BuildTargets(elements, choice);
                targets.AddRange(built);
                _log.Write($"{choice.Name}: элементов {elements.Count}, целей {built.Count}, марка «{choice.TagType.DisplayName}»");
            }

            var existing = ExistingTags.Collect(_view, _log.Writer);
            var toDelete = ExistingTags.Apply(targets, existing, settings.ExistingMode, _log.Writer);
            var active = targets.Where(target => !target.IsSkipped).ToList();
            CheckCancel(progress);

            Stage(progress, "Создание марок");
            var placer = new TagPlacer(_view, _log.Writer, metrics);
            transactions.Run("Создание марок", () =>
            {
                DeleteTags(toDelete);
                CreateTags(active, placer, progress);
            });
            CheckCancel(progress);

            Stage(progress, "Измерение марок");

            foreach (var target in active.Where(target => target is { IsSkipped: false, ExistingTag: null }))
            {
                if (!placer.Measure(target)) target.MarkSkipped(SkipReason.MeasureFailed);
            }

            MeasureExisting(transactions, placer, existing, active);
            foreach (var target in active.Where(target => target is { IsSkipped: false, Measured: false }))
                target.MarkSkipped(SkipReason.MeasureFailed);
            CheckCancel(progress);

            // Повтор на трассе виден только по тексту марок: одинаковый текст вдоль трассы — одна подпись.
            // Все цели, а не только новые: трасса связна и через стояки, короткие и уже подписанные участки.
            if (settings.RunMode == RunTagMode.Routes)
                RouteFilter.Apply(targets, _view, settings.MinRunLength, _log.Writer);

            if (settings.ShareLabels)
                LabelSharing.Apply(active, _view.Projection, settings.PreferredLeader,
                    MaxSharedLabel, _log.Writer);

            Stage(progress, "Подготовка раскладки");
            var request = BuildRequest(active, existing);
            LastRequest = request;
            CheckCancel(progress);

            Stage(progress, "Раскладка");
            layout = Solve(request, progress);

            Stage(progress, "Размещение марок");
            transactions.Run("Размещение марок", () => ApplyLayout(active, layout, placer, progress));

            // Марки, созданные для пропущенных целей (не измерились, не встали), — убираем.
            var orphans = active
                .Where(target => target.IsSkipped && target.ExistingTag is null && target.Tag is { IsValidObject: true })
                .Select(target => target.Tag!.Id)
                .ToList();
            if (orphans.Count > 0)
                transactions.Run("Очистка", () => _view.Document.Delete(orphans));

            transactions.Commit();
            result.Completed = true;
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true;
            _log.Write("Отменено пользователем, изменения откатываются.");
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            _log.Write("ОШИБКА: " + ex);
        }

        LastLayout = layout;
        FillReport(result, targets, layout);
        result.LayoutStats = layout?.Stats;
        result.ElapsedMs = _clock.ElapsedMilliseconds;
        _log.Write(string.Format(CultureInfo.InvariantCulture,
            "Итого {0} мс: размещено {1}, с конфликтами {2}, пропущено {3}, ошибок {4}",
            result.ElapsedMs, result.Count(ItemStatus.Placed), result.Count(ItemStatus.PlacedWithConflicts),
            result.Count(ItemStatus.Skipped), result.Count(ItemStatus.Failed)));
        result.LogPath = _log.Save();
        return result;
    }

    private void Lock3DView(TransactionRunner transactions, RunResult result)
    {
        View3D? locked = null;
        transactions.Run("Блокировка 3D-вида", () => locked = ViewLocker.Lock((View3D)_view.View, _log.Writer));
        if (locked is null)
            throw new InvalidOperationException("Не удалось заблокировать 3D-вид — марки на нём не поставить.");

        result.ViewLocked = true;
        if (locked.Id != _view.View.Id)
            result.LockedCopyId = locked.Id;

        // Даже если вид тот же, после блокировки контекст надо перечитать.
        _view = _view.ForView(locked);
    }

    private void DeleteTags(List<ElementId> toDelete)
    {
        if (toDelete.Count == 0) return;

        try
        {
            _view.Document.Delete(toDelete);
            _log.Write($"Удалено старых марок: {toDelete.Count}");
        }
        catch (Exception ex)
        {
            // Старые марки останутся рядом с новыми — неприятно, но не повод отменять весь запуск.
            _log.Write("Не удалось удалить старые марки: " + ex.Message);
        }
    }

    private void CreateTags(List<TagTarget> active, TagPlacer placer, IRunProgress progress)
    {
        var step = 0;
        foreach (var target in active)
        {
            step++;
            if (target.ExistingTag is not null) continue;

            try
            {
                placer.CreateCollapsed(target);
            }
            catch (Exception ex)
            {
                target.MarkSkipped(SkipReason.CreateFailed, ex.Message);
                _log.Write($"Не создана марка для {target.Label}: {ex.Message}");
            }

            if (step % 20 == 0)
            {
                progress.Report(step, active.Count);
                progress.Pump();
            }
        }
    }

    /// <summary>
    ///     Текст стоящих марок. Рамка марки с выноской включает и выноску, поэтому марки временно
    ///     «сжимаются» к концу выноски, измеряются, и изменения откатываются.
    /// </summary>
    private void MeasureExisting(TransactionRunner transactions, TagPlacer placer, List<ExistingTag> existing,
        List<TagTarget> active)
    {
        var alive = existing.Where(tag => !tag.Deleted && tag.Tag.IsValidObject).ToList();
        if (alive.Count == 0) return;

        var targetOfTag = active
            .Where(target => target.ExistingTag is not null)
            .ToDictionary(target => target.ExistingTag!.Id);

        transactions.RunTemporary("Измерение марок на виде", () =>
        {
            foreach (var tag in alive.Where(tag => tag.Tag.HasLeader && (tag.End ?? tag.Head) is not null))
                TagApi.Collapse(tag.Tag, tag.End ?? tag.Head!);
        }, () =>
        {
            foreach (var tag in alive)
            {
                if (!placer.TryMeasure(tag.Tag, out var text, out var collapsedHead, out var attachHeight)) continue;

                // Смещение текста от головы не меняется — переносим рамку к настоящему положению головы.
                var offset = text.Min - collapsedHead;
                var head = tag.Head is not null ? _view.Projection.ToSheet(tag.Head) : collapsedHead;
                tag.Text = Layout.Geometry.Box2D.FromMin(head + offset, text.Width, text.Height);

                if (!targetOfTag.TryGetValue(tag.Tag.Id, out var target)) continue;

                target.TextWidth = text.Width;
                target.TextHeight = text.Height;
                target.AttachHeight = attachHeight;
                target.TextOffset = offset;
                target.Text = TagPlacer.SafeText(tag.Tag);
                target.Measured = true;
            }
        });
    }

    private LayoutRequest BuildRequest(List<TagTarget> active, List<ExistingTag> existing)
    {
        var request = new LayoutRequest { Settings = settings.ToLayoutSettings() };
        // На 3D-схеме стороны «сверху/слева» зависят от поворота вида — там марки ставятся по месту.
        if (_view.Kind == ViewKind.ThreeD)
            request.Settings.Side = TagSide.Auto;
        if (settings.UseCrop && _view.Crop is { } crop)
        {
            request.Bounds = crop;
            request.BoundsShape = _view.CropShape;
        }

        var obstacles = new ObstacleCollector(_view, _log.Writer);
        var ownerOf = new Dictionary<ElementId, int>();
        var categories = settings.Choices.Select(choice => choice.Category).Distinct().ToList();
        foreach (var target in active.Where(target => target is { IsSkipped: false, SharedWith: null }))
        {
            var anchors = target.Anchors
                .Select(point => (Point: point, Sheet: _view.Projection.ToSheet(point)))
                .Where(anchor => !settings.UseCrop || _view.IsInsideCrop(anchor.Sheet))
                .ToList();
            if (anchors.Count == 0)
            {
                target.MarkSkipped(SkipReason.OutsideCrop);
                continue;
            }

            // Индексы точек в раскладке должны совпадать с индексами в цели.
            target.Anchors.Clear();
            target.Anchors.AddRange(anchors.Select(anchor => anchor.Point));

            var axis = target.IsRun && target.Axis is not null
                ? _view.Projection.DirectionOnSheet(target.Axis)
                : Layout.Geometry.Vector2D.Zero;
            var item = new LayoutItem
            {
                Label = target.Label,
                Axis = axis,
                TextWidth = target.TextWidth,
                TextHeight = target.TextHeight,
                AttachHeight = target.AttachHeight,
                Footprint = obstacles.Footprint(target),
                // Новые марки ставятся раньше переразмещаемых.
                Priority = target.ExistingTag is null ? 0 : -1,
                // Штучные элементы — одна группа; участки — по категории и направлению: воздуховоды
                // и трубы рядом подписываются каждые своей стопкой.
                Group = !target.IsRun ? 1 : (categories.IndexOf(target.Choice.Category) + 1) * 10 + (Math.Abs(axis.X) >= Math.Abs(axis.Y) ? 2 : 3)
            };
            item.Anchors.AddRange(anchors.Select(anchor => anchor.Sheet));
            item.ExtraAnchors.AddRange(target.Sharing.Select(shared => _view.Projection.ToSheet(shared.Anchors[0])));

            target.LayoutIndex = request.Items.Count;
            request.Items.Add(item);
            foreach (var id in target.ElementIds.Concat(target.Sharing.SelectMany(shared => shared.ElementIds)))
                ownerOf[id] = target.LayoutIndex;
        }

        // Марки общей подписи встают туда же, куда ведущая.
        foreach (var target in active.Where(target => target is { IsSkipped: false, SharedWith: { } }))
        {
            if (target.SharedWith!.IsSkipped)
                target.MarkSkipped(target.SharedWith.Skip, target.SharedWith.SkipDetails);
            else
                target.LayoutIndex = target.SharedWith.LayoutIndex;
        }

        obstacles.AddElements(request, ownerOf);
        obstacles.AddTags(request, existing);
        obstacles.AddAnnotations(request);
        _log.Write($"Задача раскладки: марок {request.Items.Count}, препятствий {request.Obstacles.Count}, " +
                   $"подрезка {request.Bounds?.ToString() ?? "нет"}");
        return request;
    }

    /// <summary>Раскладка — в фоновом потоке, чтобы окно прогресса жило и отмена срабатывала.</summary>
    private LayoutResult Solve(LayoutRequest request, IRunProgress progress)
    {
        using var cancellation = new CancellationTokenSource();
        LayoutProgress? latest = null;
        var engine = new LayoutEngine(_log.Writer);
        var task = Task.Run(() => engine.Solve(request, info => latest = info, cancellation.Token));

        while (!((IAsyncResult)task).AsyncWaitHandle.WaitOne(40))
        {
            if (latest is { } info)
            {
                var detail = info.HardConflicts >= 0
                    ? $"{info.Stage} · {info.Current}% · конфликтов {info.HardConflicts}"
                    : $"{info.Stage} · {info.Current} из {info.Total}";
                progress.Report(info.Current, Math.Max(1, info.Total), detail);
            }

            progress.Pump();
            if (progress.IsCancelled) cancellation.Cancel();
        }

        if (task.IsFaulted)
        {
            var error = task.Exception!.GetBaseException();
            throw new InvalidOperationException("Ошибка раскладки: " + error.Message, error);
        }

        var result = task.Result;
        if (result.Stats.Cancelled) throw new OperationCanceledException();
        return result;
    }

    private void ApplyLayout(List<TagTarget> active, LayoutResult layout, TagPlacer placer, IRunProgress progress)
    {
        var step = 0;
        foreach (var target in active)
        {
            step++;
            if (target.IsSkipped || target.LayoutIndex < 0) continue;

            if (layout.Placements[target.LayoutIndex] is not { } placement)
            {
                target.MarkSkipped(SkipReason.PlaceFailed, "нет подходящего положения");
                continue;
            }

            try
            {
                if (!placer.Apply(target, placement)) target.MarkSkipped(SkipReason.PlaceFailed);
            }
            catch (Exception ex)
            {
                target.MarkSkipped(SkipReason.PlaceFailed, ex.Message);
                _log.Write($"Не размещена марка {target.Label}: {ex.Message}");
            }

            if (step % 25 == 0)
            {
                progress.Report(step, active.Count);
                progress.Pump();
            }
        }
    }

    private void FillReport(RunResult result, List<TagTarget> targets, LayoutResult? layout)
    {
        foreach (var target in targets)
        {
            var ids = target.ElementIds.ToList();
            ItemReport report;
            if (!result.Completed)
            {
                var reason = result.Cancelled ? ItemReport.Describe(SkipReason.Cancelled) : result.Error ?? string.Empty;
                report = new ItemReport(target.Label, ids, ItemStatus.Skipped, reason);
            }
            else if (target.IsSkipped)
            {
                var failed = target.Skip is SkipReason.CreateFailed or SkipReason.MeasureFailed or SkipReason.PlaceFailed;
                var reason = ItemReport.Describe(target.Skip);
                if (!string.IsNullOrWhiteSpace(target.SkipDetails)) reason += ": " + target.SkipDetails;
                report = new ItemReport(target.Label, ids, failed ? ItemStatus.Failed : ItemStatus.Skipped, reason);
            }
            else if (layout is not null && target.LayoutIndex >= 0 &&
                     layout.Statuses[target.LayoutIndex] == PlacementStatus.PlacedWithConflicts)
            {
                report = new ItemReport(target.Label, ids, ItemStatus.PlacedWithConflicts,
                    DescribeConflicts(layout, target.LayoutIndex));
            }
            else
            {
                report = new ItemReport(target.Label, ids, ItemStatus.Placed, string.Empty);
            }

            result.Items.Add(report);
        }
    }

    private static string DescribeConflicts(LayoutResult layout, int item)
    {
        var kinds = layout.Conflicts
            .Where(conflict => conflict.IsHard && (conflict.Item == item || conflict.OtherItem == item))
            .GroupBy(conflict => conflict.Kind)
            .Select(group => group.Count() > 1
                ? $"{Conflict.Describe(group.Key)} ×{group.Count()}"
                : Conflict.Describe(group.Key));
        return string.Join(", ", kinds);
    }

    private void WriteHeader()
    {
        _log.Write("=== Умные выноски ===");
        _log.Write("Вид: " + _view.Description);
        foreach (var choice in settings.Choices)
            _log.Write($"Категория {choice.Name} ({choice.Category}), марка «{choice.TagType.DisplayName}»");
        _log.Write(string.Format(CultureInfo.InvariantCulture,
            "Режим {0}, только выбранные: {1}, мин. участок {2} мм, стояки {3}, линейные {4}, " +
            "выноска {5}/{6} мм (мин. {7}), зазор {8}, качество {9}, подрезка {10}, общие подписи {11}, сторона {12}",
            settings.ExistingMode, settings.OnlyElements?.Count.ToString() ?? "нет", settings.MinRunLength,
            settings.TagVerticalRuns, settings.RunMode, settings.PreferredLeader, settings.MaxLeader,
            settings.MinLeader, settings.Gap, settings.Quality, _view.Crop?.ToString() ?? "нет", settings.ShareLabels,
            settings.Side));
    }

    private void Stage(IRunProgress progress, string stage)
    {
        _log.Write("--- " + stage + $" ({_clock.ElapsedMilliseconds} мс)");
        progress.Stage(stage);
        progress.Pump();
    }

    private static void CheckCancel(IRunProgress progress)
    {
        progress.Pump();
        if (progress.IsCancelled) throw new OperationCanceledException();
    }
}
