using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicStructures.Core;

namespace DynamicStructures.UI;

public enum Op { Insert, Remove, Peek, IsEmpty, Print }

/// <summary>Анимированная демонстрация стека: операции вызываются у собственного LinkedStack, а элементы плавно влетают и вылетают.</summary>
public sealed class StructureView : Border
{
    private static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.RegisterAttached(
        "HorizontalOffset", typeof(double), typeof(StructureView),
        new PropertyMetadata(0.0, (d, e) => ((ScrollViewer)d).ScrollToHorizontalOffset((double)e.NewValue)));

    private static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.RegisterAttached(
        "VerticalOffset", typeof(double), typeof(StructureView),
        new PropertyMetadata(0.0, (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));

    private readonly Action<string> _log;
    private readonly Func<double> _speed;
    private LinkedStack<string> _stack = new();
    private readonly TextBox _value = new()
    {
        Width = 110, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center,
        Padding = new Thickness(10, 6, 10, 6), ToolTip = "Значение; если пусто, подставится следующее число",
    };
    private readonly WrapPanel _controls = new() { Margin = new Thickness(14, 14, 14, 0) };
    private readonly List<Button> _buttons = [];
    private readonly TextBlock _status = new()
    {
        Foreground = Theme.Text, FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Border _stage = new() { Padding = new Thickness(80, 120, 80, 120) };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly List<Border> _boxes = [];
    private int _highlight = -1;
    private int _hidden = -1;
    private int _counter;

    /// <summary>Писать ли результат каждой операции в окно вывода (при показе файла оно уже содержит полный лог).</summary>
    public bool LogOperations { get; set; } = true;

    public bool ShowControls
    {
        get => _controls.Visibility == Visibility.Visible;
        set => _controls.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public StructureView(Action<string> log, Func<double> speed)
    {
        _log = log;
        _speed = speed;

        _controls.Children.Add(new TextBlock { Text = "Значение:", Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        _controls.Children.Add(_value);
        foreach (var (op, withValue) in new[] { (Op.Insert, true), (Op.Remove, false), (Op.Peek, false), (Op.IsEmpty, false) })
        {
            var button = Theme.Pill(OpName(op), false);
            button.Click += async (_, _) => await ManualAsync(op, withValue);
            _buttons.Add(button);
            _controls.Children.Add(button);
        }
        var reset = Theme.Pill("Очистить", false);
        reset.Click += (_, _) => { Reset(); if (LogOperations) _log("Стек очищен."); };
        _buttons.Add(reset);
        _controls.Children.Add(reset);

        var statusCard = new Border
        {
            Child = _status, MinHeight = 64, Padding = new Thickness(18, 12, 18, 12), Margin = new Thickness(14, 12, 14, 0),
            Background = Theme.SurfaceHigh, BorderBrush = Theme.Accent, BorderThickness = new Thickness(5, 0, 0, 0),
            CornerRadius = new CornerRadius(12),
        };

        _scroll.Content = new Grid
        {
            Children = { _stage, _overlay }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        var body = new DockPanel();
        DockPanel.SetDock(_controls, Dock.Top);
        DockPanel.SetDock(statusCard, Dock.Top);
        body.Children.Add(_controls);
        body.Children.Add(statusCard);
        body.Children.Add(_scroll);
        Child = body;

        Reset();
    }

    public static Op? FromCode(int code) => code switch
    {
        1 => Op.Insert, 2 => Op.Remove, 3 => Op.Peek, 4 => Op.IsEmpty, 5 => Op.Print, _ => null,
    };

    private static string OpName(Op op) => op switch
    {
        Op.Insert => "Push", Op.Remove => "Pop", Op.Peek => "Top", Op.IsEmpty => "isEmpty", _ => "Print",
    };

    private double Speed => Math.Max(0.05, _speed());

    public Task PauseAsync(double milliseconds, CancellationToken ct) => Task.Delay(TimeSpan.FromMilliseconds(milliseconds / Speed), ct);

    public void Reset()
    {
        _stack = new();
        _counter = 0;
        _highlight = _hidden = -1;
        _status.Text = "Стек (LIFO): последним пришёл — первым вышел. Вершина находится в голове списка.";
        Render();
    }

    public void SetStatus(string text) => _status.Text = text;

    public async Task NoteAsync(string text, string step, CancellationToken ct)
    {
        _status.Text = step + text;
        await PauseAsync(700, ct);
    }

    private void SetBusy(bool busy)
    {
        _value.IsEnabled = !busy;
        foreach (var button in _buttons) button.IsEnabled = !busy;
    }

    private async Task ManualAsync(Op op, bool withValue)
    {
        var value = !withValue ? null : _value.Text.Trim().Length > 0 ? _value.Text.Trim() : (++_counter).ToString();
        if (withValue) _value.Clear();
        SetBusy(true);
        try { await ExecuteAsync(op, value, null, "", CancellationToken.None); }
        finally { SetBusy(false); }
    }

    public async Task<string> ExecuteAsync(Op op, string? arg, string? note, string step, CancellationToken ct)
    {
        var description = op == Op.Insert ? $"{OpName(op)}({arg})" : $"{OpName(op)}()";
        _status.Text = step + (note ?? description);
        var result = await PerformAsync(op, arg ?? "", ct);
        var message = $"{description} → {result}";
        if (note is null) _status.Text = step + message;
        if (LogOperations) _log(message);
        await PauseAsync(350, ct);
        return message;
    }

    private async Task<string> PerformAsync(Op op, string value, CancellationToken ct)
    {
        switch (op)
        {
            case Op.Insert:
                _stack.Push(value);
                await AnimateInsertAsync(value, ct);
                return "ok";
            case Op.Remove:
            {
                if (_stack.IsEmpty()) { await PauseAsync(500, ct); return "пусто"; }
                await AnimateRemoveAsync(ct);
                var removed = _stack.Pop();
                _highlight = _hidden = -1;
                Render();
                return removed;
            }
            case Op.Peek:
                if (_stack.IsEmpty()) { await PauseAsync(500, ct); return "пусто"; }
                _highlight = 0;
                Render();
                UpdateLayout();
                CenterOn(_boxes[0]);
                await PauseAsync(800, ct);
                _highlight = -1;
                Render();
                return _stack.Top();
            case Op.IsEmpty:
                await PauseAsync(600, ct);
                return _stack.IsEmpty() ? "да" : "нет";
            default:
                await SweepAsync(ct);
                return _stack.Print();
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        for (var i = 0; i < _stack.Count; i++)
        {
            _highlight = i;
            Render();
            UpdateLayout();
            CenterOn(_boxes[i]);
            await PauseAsync(300, ct);
        }
        _highlight = -1;
        Render();
    }

    private Point PositionOf(FrameworkElement element) => element.TransformToVisual(_overlay).Transform(new Point(0, 0));

    /// <summary>Плавно прокручивает область так, чтобы элемент оказался в центре; если всё помещается — ничего не делает.</summary>
    private void CenterOn(FrameworkElement element)
    {
        var position = PositionOf(element);
        var x = Math.Clamp(position.X + element.ActualWidth / 2 - _scroll.ViewportWidth / 2, 0, Math.Max(0, _scroll.ExtentWidth - _scroll.ViewportWidth));
        var y = Math.Clamp(position.Y + element.ActualHeight / 2 - _scroll.ViewportHeight / 2, 0, Math.Max(0, _scroll.ExtentHeight - _scroll.ViewportHeight));
        var duration = TimeSpan.FromMilliseconds(Math.Max(1, 600 / Speed));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        _scroll.BeginAnimation(HorizontalOffsetProperty, new DoubleAnimation(_scroll.HorizontalOffset, x, duration) { EasingFunction = ease });
        _scroll.BeginAnimation(VerticalOffsetProperty, new DoubleAnimation(_scroll.VerticalOffset, y, duration) { EasingFunction = ease });
    }

    private Border Floating(string text, Border like)
    {
        var box = Box(text, true);
        box.Width = like.ActualWidth;
        box.Height = like.ActualHeight;
        _overlay.Children.Add(box);
        return box;
    }

    private async Task AnimateInsertAsync(string value, CancellationToken ct)
    {
        _hidden = 0;
        _highlight = -1;
        Render();
        UpdateLayout();
        var target = _boxes[0];
        var to = PositionOf(target);
        CenterOn(target);
        var floating = Floating(value, target);
        try { await MoveAsync(floating, new Point(to.X, to.Y - 130), to, 0, 1, ct); }
        finally { _overlay.Children.Remove(floating); _hidden = -1; }
        _highlight = 0;
        Render();
    }

    private async Task AnimateRemoveAsync(CancellationToken ct)
    {
        _highlight = 0;
        _hidden = -1;
        Render();
        UpdateLayout();
        CenterOn(_boxes[0]);
        await PauseAsync(450, ct);
        var box = _boxes[0];
        var from = PositionOf(box);
        var floating = Floating(_stack.Top(), box);
        box.Opacity = 0;
        try { await MoveAsync(floating, from, new Point(from.X, from.Y - 130), 1, 0, ct); }
        finally { _overlay.Children.Remove(floating); }
    }

    private async Task MoveAsync(FrameworkElement element, Point from, Point to, double opacityFrom, double opacityTo, CancellationToken ct)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(1, 750 / Speed));
        var done = new TaskCompletionSource();
        using var registration = ct.Register(() => done.TrySetCanceled());
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        Canvas.SetLeft(element, from.X);
        Canvas.SetTop(element, from.Y);
        element.Opacity = opacityFrom;
        var vertical = new DoubleAnimation(from.Y, to.Y, duration) { EasingFunction = ease };
        vertical.Completed += (_, _) => done.TrySetResult();
        element.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(from.X, to.X, duration) { EasingFunction = ease });
        element.BeginAnimation(Canvas.TopProperty, vertical);
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(opacityFrom, opacityTo, duration));
        await done.Task;
    }

    private static Border Box(string text, bool highlighted) => new()
    {
        Child = new TextBlock
        {
            Text = text, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = highlighted ? Theme.OnAccent : Theme.Text,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 10, 14, 10),
        },
        MinWidth = 170, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2),
        BorderBrush = highlighted ? Theme.AccentHover : Theme.Accent, Background = highlighted ? Theme.Accent : Theme.Surface,
    };

    private void Render()
    {
        _boxes.Clear();
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        if (_stack.IsEmpty())
            panel.Children.Add(new TextBlock { Text = "(пусто)", Foreground = Theme.Muted, FontSize = 18, Margin = new Thickness(0, 0, 0, 8) });

        var i = 0;
        foreach (var item in _stack.Items)
        {
            var box = Box(item, i == _highlight);
            if (i == _hidden) box.Opacity = 0;
            _boxes.Add(box);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new TextBlock
            {
                Text = i == 0 ? "вершина →" : "", Width = 110, Foreground = Theme.Accent, FontWeight = FontWeights.SemiBold, FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 10, 0),
            });
            row.Children.Add(box);
            panel.Children.Add(row);
            i++;
        }

        panel.Children.Add(new Border
        {
            Height = 4, Width = 170, Background = Theme.Muted, CornerRadius = new CornerRadius(2), Margin = new Thickness(120, 2, 0, 0),
        });
        _stage.Child = panel;
    }
}
