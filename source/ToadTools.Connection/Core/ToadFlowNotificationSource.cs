namespace ToadTools.Connection;

/// <summary>Уведомления текущего подключения ToadFlow; без подключения — молчит.</summary>
public sealed class ToadFlowNotificationSource(ToadFlowClient client, ConnectionStore store) : INotificationSource
{
    private sealed record Count(int Unread);

    public bool IsConnected => store.Load() is not null;

    public async Task<int> UnreadAsync(CancellationToken ct) => (await client.GetAsync<Count>("/api/notifications/count", ct)).Unread;

    public Task<List<NotificationDto>> LatestAsync(int take, CancellationToken ct) => client.GetAsync<List<NotificationDto>>($"/api/notifications?take={take}", ct);

    public async Task ReadAsync(IEnumerable<long> ids, CancellationToken ct)
    {
        foreach (var id in ids) await client.PostAsync<object>($"/api/notifications/{id}/read", null, ct);
    }

    public Task AcceptAsync(Guid requestId, CancellationToken ct) => client.PostAsync<object>($"/api/families/requests/{requestId}/accept", null, ct);

    public string? WebUrl(string? link) => client.WebUrl(link ?? "/");
}

/// <summary>Показывать ли всплывающие уведомления ToadFlow в Revit — флажок в окне подключения, по умолчанию да.</summary>
public static class NotificationSettings
{
    private static readonly string OffFlag = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToadTools", "notifications-off");

    public static bool Enabled
    {
        get => !System.IO.File.Exists(OffFlag);
        set
        {
            if (value)
            {
                if (System.IO.File.Exists(OffFlag)) System.IO.File.Delete(OffFlag);
            }
            else
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(OffFlag)!);
                System.IO.File.WriteAllText(OffFlag, "");
            }
        }
    }
}
