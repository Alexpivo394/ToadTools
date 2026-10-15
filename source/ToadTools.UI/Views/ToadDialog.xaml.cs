using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using ToadTools.UI.Services;
using Wpf.Ui.Appearance;

namespace ToadTools.UI.Views;

public partial class ToadDialog : FluentWindow
{
        private const double MaxDialogWidthRatio = 0.7;
        private const double MaxDialogHeightRatio = 0.8;
        private const double ContentHeightPadding = 160;

        public ToadDialog ()
        {
            InitializeComponent();
            
            ApplySizeLimits();
            
            Loaded += (_, __) =>
            {
                if (DataContext is ViewModels.ToadDialogViewModel vm)
                {
                    vm.RequestClose += (_, __) =>
                    {
                        DialogResult = true;
                        Close();
                    };
                }

                Dispatcher.BeginInvoke(ResizeToContent, DispatcherPriority.ContextIdle);
            };
            
            ThemeWatcherService.Initialize();
            // Тема — та, что выбрана в окне плагина: иначе диалог перекрасил бы открытые окна в тёмную.
            ThemeWatcherService.ApplyTheme(ThemeWatcherService.CurrentTheme);
            ThemeWatcherService.Watch(this);
        }

        private void ApplySizeLimits()
        {
            var workArea = SystemParameters.WorkArea;

            MaxWidth = Math.Max(MinWidth, workArea.Width * MaxDialogWidthRatio);
            MaxHeight = Math.Max(MinHeight, workArea.Height * MaxDialogHeightRatio);

            MessageScrollViewer.MaxHeight = Math.Max(80, MaxHeight - ContentHeightPadding);
        }

        private void ResizeToContent()
        {
            ApplySizeLimits();

            var width = RootGrid.ActualWidth > 0 ? RootGrid.ActualWidth : ActualWidth;
            if (double.IsNaN(width) || width <= 0)
            {
                width = MinWidth;
            }

            RootGrid.Measure(new Size(width, double.PositiveInfinity));
            var contentHeight = RootGrid.DesiredSize.Height;
            var chromeHeight = Math.Max(0, ActualHeight - RootGrid.ActualHeight);
            var desiredHeight = Math.Ceiling(contentHeight + chromeHeight);

            Height = Math.Min(MaxHeight, Math.Max(MinHeight, desiredHeight));
            CenterInWorkArea();
        }

        private void CenterInWorkArea()
        {
            var workArea = SystemParameters.WorkArea;
            Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
            Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        }
        
        private void RootGrid_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
}
