using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;

namespace ToadTools.Connection.Notifications;

/// <summary>
///     Всплывашка справа внизу экрана: заголовок, текст, «Открыть» (браузер), у заявки на приёмке — «Подходит», «Закрыть» (прочитано).
///     Не забирает фокус у Revit и закрывается сама через 12 секунд, если над ней нет мыши.
/// </summary>
public static class NotificationToast
{
    private static Window? _current;

    public static void Show(NotificationToastModel model, ToadFlowNotificationSource source)
    {
        _current?.Close();
        var window = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowActivated = false,
            ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height, Width = 360
        };
        var title = new TextBlock { Text = model.Title, FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap };
        var text = new TextBlock { Text = model.Text, Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        Button Make(string caption, Action action, bool primary)
        {
            var button = new Button
            {
                Content = caption, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3), Cursor = System.Windows.Input.Cursors.Hand,
                Background = primary ? new SolidColorBrush(Color.FromRgb(34, 139, 230)) : Brushes.WhiteSmoke,
                Foreground = primary ? Brushes.White : Brushes.Black, BorderThickness = new Thickness(0)
            };
            button.Click += (_, _) => action();
            return button;
        }

        // Сеть — в фоне: ошибка прочтения или приёмки не мешает Revit, уведомление останется непрочитанным в вебе.
        void Quietly(Func<Task> call) => _ = Task.Run(async () =>
        {
            try
            {
                await call();
            }
            catch
            {
                // молча
            }
        });

        void Read() => Quietly(() => source.ReadAsync(model.Ids, CancellationToken.None));

        if (model.CanAccept && NotificationToastModel.RequestIdOf(model.Link) is { } requestId)
            buttons.Children.Add(Make("Подходит", () =>
            {
                Quietly(() => source.AcceptAsync(requestId, CancellationToken.None));
                Read();
                window.Close();
            }, true));
        buttons.Children.Add(Make("Открыть", () =>
        {
            if (source.WebUrl(model.Link) is { } url) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            Read();
            window.Close();
        }, !model.CanAccept));
        buttons.Children.Add(Make("Закрыть", () =>
        {
            Read();
            window.Close();
        }, false));

        window.Content = new Border
        {
            Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(206, 212, 218)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, Opacity = 0.25, ShadowDepth = 2 },
            Child = new StackPanel { Children = { new TextBlock { Text = "ToadFlow", FontSize = 11, Foreground = Brushes.Gray }, title, text, buttons } }
        };
        window.Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            window.Left = area.Right - window.ActualWidth - 8;
            window.Top = area.Bottom - window.ActualHeight - 8;
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        timer.Tick += (_, _) =>
        {
            if (window.IsMouseOver) return;
            timer.Stop();
            window.Close();
        };
        window.Closed += (_, _) =>
        {
            timer.Stop();
            if (_current == window) _current = null;
        };
        _current = window;
        window.Show();
        timer.Start();
    }
}
