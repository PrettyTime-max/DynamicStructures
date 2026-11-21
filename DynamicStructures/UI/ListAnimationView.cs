using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DynamicStructures.Core;
using WpfPath = System.Windows.Shapes.Path;

namespace DynamicStructures.UI;

/// <summary>Анимация связного списка: узлы со ссылками Next, подписи указателей и пошаговые пояснения.</summary>
public sealed class ListAnimationView : Border
{
    private static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.RegisterAttached(
        "HorizontalOffset", typeof(double), typeof(ListAnimationView),
        new PropertyMetadata(0.0, (d, e) => ((ScrollViewer)d).ScrollToHorizontalOffset((double)e.NewValue)));

    private static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.RegisterAttached(
        "VerticalOffset", typeof(double), typeof(ListAnimationView),
        new PropertyMetadata(0.0, (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));

    private const double BoxW = 96, BoxH = 56, GapX = 72, RowGap = 170, Pad = 60, TopPad = 80;
    private const string Intro = "Односвязный список: каждый узел хранит значение и ссылку Next на следующий узел. Выберите алгоритм и нажмите «Старт».";

    private sealed class NodeBox : Border
    {
        private readonly TextBlock _text;

        public NodeBox(string value)
        {
            _text = new TextBlock
            {
                Text = value, FontSize = 22, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0),
            };
            var cell = new Border
            {
                Child = new Ellipse { Width = 9, Height = 9, Fill = Theme.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                BorderThickness = new Thickness(1.5, 0, 0, 0), BorderBrush = Theme.Stroke,
            };
            Grid.SetColumn(cell, 1);
            Child = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = new GridLength(26) } },
                Children = { _text, cell },
            };
            Width = BoxW;
            Height = BoxH;
            CornerRadius = new CornerRadius(12);
            BorderThickness = new Thickness(2);
            SetState(0);
        }

        public void SetState(int state)
        {
            (Background, BorderBrush, _text.Foreground) = state switch
            {
                1 => (Theme.Accent, Theme.AccentHover, Theme.OnAccent),
                2 => (Theme.DangerSoft, Theme.Danger, Theme.DangerText),
                _ => (Theme.Surface, Theme.Accent, Theme.Text),
            };
        }
    }

    private readonly Func<double> _speed;
    private readonly TextBlock _status = new()
    {
        Foreground = Theme.Text, FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _nulls = new()
    {
        Foreground = Theme.Muted, FontSize = 16, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed,
    };
    private readonly Canvas _canvas = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Dictionary<int, NodeBox> _boxes = [];
    private readonly Dictionary<int, Point> _positions = [];
    private readonly List<UIElement> _decor = [];

    public ListAnimationView(Func<double> speed)
    {
        _speed = speed;
        var statusCard = new Border
        {
            Child = new StackPanel { Children = { _status, _nulls } }, MinHeight = 64, Padding = new Thickness(18, 12, 18, 12),
            Margin = new Thickness(14, 14, 14, 0), Background = Theme.SurfaceHigh, BorderBrush = Theme.Accent,
            BorderThickness = new Thickness(5, 0, 0, 0), CornerRadius = new CornerRadius(12),
        };
        _scroll.Content = new Grid { Children = { _canvas }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        var body = new DockPanel();
        DockPanel.SetDock(statusCard, Dock.Top);
        body.Children.Add(statusCard);
        body.Children.Add(_scroll);
        Child = body;
        Reset();
    }

    private double Speed => Math.Max(0.05, _speed());

    public Task PauseAsync(double milliseconds, CancellationToken ct) => Task.Delay(TimeSpan.FromMilliseconds(milliseconds / Speed), ct);

    public void Reset()
    {
        _canvas.Children.Clear();
        _boxes.Clear();
        _positions.Clear();
        _decor.Clear();
        _canvas.Width = _canvas.Height = 0;
        _status.Text = Intro;
        _nulls.Visibility = Visibility.Collapsed;
    }

    public async Task PlayAsync(IReadOnlyList<ListFrame> frames, string title, CancellationToken ct)
    {
        Reset();
        _status.Text = $"Анимация: {title}";
        await PauseAsync(1000, ct);
        for (var i = 0; i < frames.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await ShowAsync(frames[i], $"Шаг {i + 1} из {frames.Count}: ", ct);
        }
        await PauseAsync(1200, ct);
    }

    private static Point Position(FrameNode node) => new(Pad + node.Col * (BoxW + GapX), TopPad + node.Row * RowGap);

    private async Task ShowAsync(ListFrame frame, string step, CancellationToken ct)
    {
        _status.Text = step + frame.Note;
        var nulls = frame.Marks.Where(m => m.Node is null).Select(m => $"{m.Name} = null").ToList();
        _nulls.Text = string.Join(",   ", nulls);
        _nulls.Visibility = nulls.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearDecor();

        var span = TimeSpan.FromMilliseconds(Math.Max(1, 650 / Speed));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var ids = frame.Nodes.Select(n => n.Id).ToHashSet();
        var changed = false;

        foreach (var id in _boxes.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            var gone = _boxes[id];
            _boxes.Remove(id);
            _positions.Remove(id);
            changed = true;
            var top = Canvas.GetTop(gone);
            var fade = new DoubleAnimation(1, 0, span);
            fade.Completed += (_, _) => _canvas.Children.Remove(gone);
            gone.BeginAnimation(OpacityProperty, fade);
            gone.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(top, top - 50, span) { EasingFunction = ease });
        }

        var cols = frame.Nodes.GroupBy(n => n.Row).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        var rows = frame.Nodes.Select(n => n.Row).DefaultIfEmpty(0).Max() + 1;
        _canvas.Width = Pad * 2 + Math.Max(cols, 1) * (BoxW + GapX);
        _canvas.Height = TopPad + (rows - 1) * RowGap + BoxH + 120;

        foreach (var node in frame.Nodes)
        {
            var target = Position(node);
            if (_boxes.TryGetValue(node.Id, out var box))
            {
                var old = _positions[node.Id];
                if (old != target)
                {
                    changed = true;
                    Canvas.SetLeft(box, target.X);
                    Canvas.SetTop(box, target.Y);
                    box.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(old.X, target.X, span) { EasingFunction = ease });
                    box.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(old.Y, target.Y, span) { EasingFunction = ease });
                }
            }
            else
            {
                box = new NodeBox(node.Value);
                _boxes[node.Id] = box;
                _canvas.Children.Add(box);
                Canvas.SetLeft(box, target.X);
                Canvas.SetTop(box, target.Y);
                box.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(target.Y - 60, target.Y, span) { EasingFunction = ease });
                box.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, span));
                changed = true;
            }
            _positions[node.Id] = target;
            box.SetState(frame.Hot.Contains(node.Id) ? 1 : frame.Doomed.Contains(node.Id) ? 2 : 0);
        }

        UpdateLayout();
        CenterOn(frame);
        if (changed) await Task.Delay(span + TimeSpan.FromMilliseconds(80), ct);
        DrawDecor(frame);
        await PauseAsync(Math.Clamp(frame.Note.Length * 22, 900, 2600), ct);
    }

    private void CenterOn(ListFrame frame)
    {
        int? focus = frame.Hot.Count > 0 ? frame.Hot.First() : frame.Marks.FirstOrDefault(m => m.Node is not null && m.Name is not ("tail" or "head" or "head L"))?.Node;
        focus ??= frame.Nodes.Count > 0 ? frame.Nodes[0].Id : null;
        var center = focus is { } id && _positions.TryGetValue(id, out var p)
            ? new Point(p.X + BoxW / 2, p.Y + BoxH / 2)
            : new Point(_canvas.Width / 2, _canvas.Height / 2);
        var x = Math.Clamp(center.X - _scroll.ViewportWidth / 2, 0, Math.Max(0, _scroll.ExtentWidth - _scroll.ViewportWidth));
        var y = Math.Clamp(center.Y - _scroll.ViewportHeight / 2, 0, Math.Max(0, _scroll.ExtentHeight - _scroll.ViewportHeight));
        var duration = TimeSpan.FromMilliseconds(Math.Max(1, 600 / Speed));
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        _scroll.BeginAnimation(HorizontalOffsetProperty, new DoubleAnimation(_scroll.HorizontalOffset, x, duration) { EasingFunction = ease });
        _scroll.BeginAnimation(VerticalOffsetProperty, new DoubleAnimation(_scroll.VerticalOffset, y, duration) { EasingFunction = ease });
    }

    private void ClearDecor()
    {
        foreach (var element in _decor) _canvas.Children.Remove(element);
        _decor.Clear();
    }

    private void AddDecor(UIElement element, double left = 0, double top = 0)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        _canvas.Children.Add(element);
        _decor.Add(element);
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Math.Max(1, 250 / Speed))));
    }

    private void DrawDecor(ListFrame frame)
    {
        var byId = frame.Nodes.ToDictionary(n => n.Id);
        foreach (var node in frame.Nodes)
        {
            var p = _positions[node.Id];
            var middle = p.Y + BoxH / 2;
            var dot = new Point(p.X + BoxW - 13, middle);

            if (node.Next is null || !byId.TryGetValue(node.Next.Value, out var target))
            {
                Arrow([dot, new Point(p.X + BoxW + 16, middle)]);
                AddDecor(new TextBlock { Text = "null", Foreground = Theme.Muted, FontSize = 15, FontStyle = FontStyles.Italic }, p.X + BoxW + 20, middle - 11);
                continue;
            }

            var t = _positions[target.Id];
            if (target.Row != node.Row)
            {
                var below = target.Row > node.Row;
                Arrow([new Point(dot.X, below ? p.Y + BoxH : p.Y), new Point(t.X + BoxW / 2, below ? t.Y : t.Y + BoxH)]);
            }
            else if (target.Col == node.Col + 1)
            {
                Arrow([dot, new Point(t.X, middle)]);
            }
            else
            {
                var depth = 36 + 10 * Math.Min(Math.Abs(target.Col - node.Col), 5);
                var bottom = p.Y + BoxH;
                Arrow([new Point(dot.X, bottom), new Point(dot.X, bottom + depth), new Point(t.X + BoxW / 2, bottom + depth), new Point(t.X + BoxW / 2, bottom)]);
            }
        }

        foreach (var group in frame.Marks.Where(m => m.Node is { } id && byId.ContainsKey(id)).GroupBy(m => m.Node!.Value))
        {
            var p = _positions[group.Key];
            var panel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Children =
                {
                    new TextBlock
                    {
                        Text = string.Join(", ", group.Select(m => m.Name)), Foreground = Theme.AccentHover, FontSize = 16, FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock { Text = "▼", Foreground = Theme.AccentHover, FontSize = 12, TextAlignment = TextAlignment.Center },
                },
            };
            AddDecor(new Grid { Width = BoxW + 70, Height = 62, Children = { panel } }, p.X - 35, p.Y - 64);
        }
    }

    private void Arrow(Point[] points)
    {
        var figure = new PathFigure { StartPoint = points[0] };
        foreach (var point in points.Skip(1)) figure.Segments.Add(new LineSegment(point, true));
        AddDecor(new WpfPath
        {
            Data = new PathGeometry([figure]), Stroke = Theme.AccentHover, StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round,
        });

        var tip = points[^1];
        var direction = tip - points[^2];
        if (direction.LengthSquared < 0.01) return;
        direction.Normalize();
        var side = new Vector(-direction.Y, direction.X);
        AddDecor(new Polygon
        {
            Points = new PointCollection { tip, tip - direction * 11 + side * 6, tip - direction * 11 - side * 6 }, Fill = Theme.AccentHover,
        });
    }
}
