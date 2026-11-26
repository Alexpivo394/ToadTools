#nullable enable
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Grid = System.Windows.Controls.Grid;
using Panel = System.Windows.Controls.Panel;
using ToadTools.UI.Services;

namespace ToadTools.UI.Controls;

/// <summary>
///     Разделитель между списком панели (строка над ним) и нижней зоной — карточкой (строка под ним). Сам ставит высоты
///     обеих строк: доля нижней зоны — сохранённая под <see cref="Key"/> или по умолчанию для размера интерфейса; пока
///     <see cref="IsOpen"/> ложно, нижняя строка нулевая, а разделитель скрыт. Перетаскивание запоминает долю, двойной
///     щелчок её забывает. В отличие от разделителя ToadDesign, видна «ручка»: без неё зону никто не находил.
/// </summary>
public sealed class PaneSplitter : GridSplitter
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
        nameof(Key), typeof(string), typeof(PaneSplitter), new PropertyMetadata(null, OnLayoutChanged));

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(PaneSplitter), new PropertyMetadata(true, OnLayoutChanged));

    public static readonly DependencyProperty DefaultShareProperty = DependencyProperty.Register(
        nameof(DefaultShare), typeof(double), typeof(PaneSplitter), new PropertyMetadata(0.4, OnLayoutChanged));

    public static readonly DependencyProperty CompactShareProperty = DependencyProperty.Register(
        nameof(CompactShare), typeof(double), typeof(PaneSplitter), new PropertyMetadata(0.4, OnLayoutChanged));

    public static readonly DependencyProperty BottomMinHeightProperty = DependencyProperty.Register(
        nameof(BottomMinHeight), typeof(double), typeof(PaneSplitter), new PropertyMetadata(100.0, OnLayoutChanged));

    private double _heightAtDragStart = double.NaN;
    private bool _resetInDrag;

    public PaneSplitter()
    {
        Template = BuildTemplate();
        Height = 9;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        ResizeDirection = GridResizeDirection.Rows;
        ResizeBehavior = GridResizeBehavior.PreviousAndNext;
        Focusable = false;
        Cursor = Cursors.SizeNS;
        ToolTip = "Потяните, чтобы поменять высоту; двойной щелчок — как было";

        // Запоминается, только если зона и правда поменяла высоту: простой щелчок — тоже «перетаскивание». Второй щелчок
        // двойного не запоминается вовсе: сброс приходит посреди него, и высота зоны меняется уже от сброса.
        DragStarted += (_, _) => _heightAtDragStart = TryGetRows(out var top, out _) ? top.ActualHeight : double.NaN;
        DragCompleted += (sender, e) =>
        {
            var reset = _resetInDrag;
            _resetInDrag = false;
            if (!reset && !e.Canceled && TryGetRows(out var top, out _) && Math.Abs(top.ActualHeight - _heightAtDragStart) >= 1) Remember();
        };
        MouseDoubleClick += (_, _) =>
        {
            _resetInDrag = true; // этот же щелчок закончится DragCompleted — его пропустить
            Reset();
        };
        Loaded += (_, _) =>
        {
            ThemePreference.Instance.PropertyChanged += OnThemeChanged;
            ApplyLayout();
        };
        Unloaded += (_, _) => ThemePreference.Instance.PropertyChanged -= OnThemeChanged;
    }

    /// <summary>Имя панели в файле долей; пусто — доля не запоминается.</summary>
    public string? Key
    {
        get => (string?)GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    /// <summary>Нижняя зона показана.</summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>Доля нижней зоны по умолчанию в обычном размере интерфейса.</summary>
    public double DefaultShare
    {
        get => (double)GetValue(DefaultShareProperty);
        set => SetValue(DefaultShareProperty, value);
    }

    /// <summary>Доля нижней зоны по умолчанию в компактном размере.</summary>
    public double CompactShare
    {
        get => (double)GetValue(CompactShareProperty);
        set => SetValue(CompactShareProperty, value);
    }

    /// <summary>Нижняя зона не ниже этого, пока открыта.</summary>
    public double BottomMinHeight
    {
        get => (double)GetValue(BottomMinHeightProperty);
        set => SetValue(BottomMinHeightProperty, value);
    }

    /// <summary>Где хранятся доли.</summary>
    private static PaneLayout Layout => PaneLayout.Default;

    private double CurrentShare =>
        (string.IsNullOrEmpty(Key) ? null : Layout.Get(Key!)) ?? PaneLayout.Clamp(ThemePreference.Instance.IsCompact ? CompactShare : DefaultShare);

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PaneSplitter)d).ApplyLayout();

    private void OnThemeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ThemePreference.IsCompact) or null or "") ApplyLayout();
    }

    private bool TryGetRows(out RowDefinition top, out RowDefinition bottom)
    {
        top = bottom = null!;
        if (Parent is not Grid grid) return false;
        var row = Grid.GetRow(this);
        if (row < 1 || row + 1 >= grid.RowDefinitions.Count) return false;
        top = grid.RowDefinitions[row - 1];
        bottom = grid.RowDefinitions[row + 1];
        return true;
    }

    private void ApplyLayout()
    {
        Visibility = IsOpen ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        if (!TryGetRows(out var top, out var bottom)) return;

        var share = CurrentShare;
        top.Height = new GridLength(1 - share, GridUnitType.Star);
        bottom.Height = IsOpen ? new GridLength(share, GridUnitType.Star) : new GridLength(0);
        bottom.MinHeight = IsOpen ? BottomMinHeight : 0;
    }

    private void Remember()
    {
        if (!TryGetRows(out var top, out var bottom)) return;
        var share = PaneLayout.ShareOf(top.ActualHeight, bottom.ActualHeight);
        if (!string.IsNullOrEmpty(Key) && !double.IsNaN(share)) Layout.Set(Key!, share);
        ApplyLayout();
    }

    private void Reset()
    {
        if (!string.IsNullOrEmpty(Key)) Layout.Forget(Key!);
        ApplyLayout();
    }

    /// <summary>Прозрачная полоса во всю ширину, по центру — короткая «ручка»; при наведении и перетаскивании она акцентная.</summary>
    private static ControlTemplate BuildTemplate()
    {
        var grip = new FrameworkElementFactory(typeof(Border), "Grip");
        grip.SetValue(WidthProperty, 36.0);
        grip.SetValue(HeightProperty, 3.0);
        grip.SetValue(Border.CornerRadiusProperty, new CornerRadius(1.5));
        grip.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        grip.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        grip.SetResourceReference(Border.BackgroundProperty, "Toad.Brush.Border");

        var root = new FrameworkElementFactory(typeof(Grid));
        root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        root.AppendChild(grip);

        var template = new ControlTemplate(typeof(PaneSplitter)) { VisualTree = root };
        foreach (var property in new[] { IsMouseOverProperty, IsDraggingProperty })
        {
            var trigger = new Trigger { Property = property, Value = true };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Toad.Brush.Accent"), "Grip"));
            trigger.Setters.Add(new Setter(WidthProperty, 56.0, "Grip"));
            template.Triggers.Add(trigger);
        }
        return template;
    }
}
