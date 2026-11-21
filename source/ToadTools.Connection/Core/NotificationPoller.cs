namespace ToadTools.Connection;

/// <summary>Уведомление ToadFlow (колокольчик) — как его отдаёт <c>/api/notifications</c>.</summary>
public sealed record NotificationDto(long Id, string Kind, string Title, string Text, string? Link, int Count, DateTimeOffset At, bool Read);

/// <summary>Откуда опрос берёт уведомления: сервер ToadFlow текущего подключения.</summary>
public interface INotificationSource
{
    bool IsConnected { get; }

    Task<int> UnreadAsync(CancellationToken ct);

    Task<List<NotificationDto>> LatestAsync(int take, CancellationToken ct);
}

/// <summary>Что показать во всплывашке: одно уведомление или склейка нескольких.</summary>
/// <param name="Link">Путь веба для «Открыть»; null — главная.</param>
/// <param name="Ids">Какие уведомления показаны — «Закрыть» отмечает их прочитанными.</param>
public sealed record NotificationToastModel(string Title, string Text, string? Link, IReadOnlyList<long> Ids, string? Kind)
{
    /// <summary>Заявка на приёмке — во всплывашке есть «Подходит».</summary>
    public bool CanAccept => Kind == "request.review" && RequestIdOf(Link) is not null;

    /// <summary>Id заявки из ссылки <c>/families/requests/{id}</c>.</summary>
    public static Guid? RequestIdOf(string? link)
    {
        const string prefix = "/families/requests/";
        return link is not null && link.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParse(link.Substring(prefix.Length), out var id) ? id : null;
    }
}

/// <summary>
///     Опрос уведомлений для всплывашек в Revit. Первый проход сводит непрочитанное в одну всплывашку («Непрочитанных: N»), дальше
///     показываются только новые (по id); несколько сразу — одной всплывашкой. Сервер без уведомлений (404, ToadFlow старее 3.1) —
///     опрос выключается до перезапуска Revit. Прочие сбои молча пропускаются до следующего раза.
/// </summary>
public sealed class NotificationPoller(INotificationSource source, Action<NotificationToastModel> show)
{
    private const int Take = 10;
    private const int Listed = 3;
    private long? _lastShown;
    private int _lastUnread = -1;

    public bool Disabled { get; private set; }

    public async Task TickAsync(CancellationToken ct)
    {
        if (Disabled || !source.IsConnected) return;
        try
        {
            var unread = await source.UnreadAsync(ct);
            if (unread == _lastUnread && _lastShown is not null) return;
            _lastUnread = unread;
            var latest = unread == 0 && _lastShown is not null ? [] : await source.LatestAsync(Take, ct);
            var top = latest.Count == 0 ? _lastShown ?? 0 : Math.Max(latest.Max(n => n.Id), _lastShown ?? 0);

            if (_lastShown is null)
            {
                _lastShown = top;
                if (unread > 0)
                    show(new NotificationToastModel($"Непрочитанных уведомлений: {unread}", string.Join("\n", latest.Where(n => !n.Read).Take(Listed).Select(n => n.Title)),
                        null, latest.Where(n => !n.Read).Select(n => n.Id).ToList(), null));
                return;
            }

            var fresh = latest.Where(n => !n.Read && n.Id > _lastShown).OrderByDescending(n => n.Id).ToList();
            _lastShown = top;
            if (fresh.Count == 1)
                show(new NotificationToastModel(fresh[0].Title, fresh[0].Text, fresh[0].Link, [fresh[0].Id], fresh[0].Kind));
            else if (fresh.Count > 1)
                show(new NotificationToastModel($"Новых уведомлений: {fresh.Count}", string.Join("\n", fresh.Take(Listed).Select(n => n.Title)), null,
                    fresh.Select(n => n.Id).ToList(), null));
        }
        catch (ToadFlowRequestException exception) when (exception.Status == 404)
        {
            Disabled = true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Сеть, сервер перезапускается, подключение устарело — попробуем в следующий раз, без окон с ошибками.
        }
    }
}
