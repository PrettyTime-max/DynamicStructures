using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private const double MinZoom = 0.15, MaxZoom = 2.5, MinAutoZoom = 0.25;
    private const string Intro = "Односвязный список: каждый узел хранит значение и ссылку Next на следующий узел. Выберите алгоритм и нажмите «Старт».";

    private static readonly SolidColorBrush[] GreenTones =
    [
        MakeBrush("#059669"),
        MakeBrush("#16A34A"),
        MakeBrush("#0D9488"),
        MakeBrush("#65A30D"),
        MakeBrush("#047857"),
        MakeBrush("#22C55E"),
    ];

    private static SolidColorBrush MakeBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

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
    private readonly ScaleTransform _scaleTransform = new(1, 1);
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _zoomText = new() { Foreground = Theme.Muted, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _autoZoom = Theme.Pill("Авто", false);
    private readonly Dictionary<int, NodeBox> _boxes = [];
    private readonly Dictionary<int, Point> _positions = [];
    private readonly List<UIElement> _decor = [];
    private double _scale = 1.0;
    private bool _autoFit = true;
    private Point? _dragStart;
    private Point _dragOffset;

    public ListAnimationView(Func<double> speed)
    {
        _speed = speed;
        _canvas.LayoutTransform = _scaleTransform;

        _autoZoom.Padding = new Thickness(12, 4, 12, 4);
        _autoZoom.FontSize = 13;
        _autoZoom.ToolTip = "Подогнать масштаб под длину списка";
        _autoZoom.Click += (_, _) =>
        {
            _autoFit = true;
            FitToView();
        };

        var zoomPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12, 0, 0, 0),
            Children = { _zoomText, _autoZoom },
        };
        DockPanel.SetDock(zoomPanel, Dock.Right);

        var statusBody = new DockPanel();
        statusBody.Children.Add(zoomPanel);
        statusBody.Children.Add(new StackPanel { Children = { _status, _nulls } });

        var statusCard = new Border
        {
            Child = statusBody, MinHeight = 64, Padding = new Thickness(18, 12, 18, 12),
            Margin = new Thickness(14, 14, 14, 0), Background = Theme.SurfaceHigh, BorderBrush = Theme.Accent,
            BorderThickness = new Thickness(5, 0, 0, 0), CornerRadius = new CornerRadius(12),
        };
        _scroll.Content = new Grid { Children = { _canvas }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _scroll.SizeChanged += (_, _) => { if (_autoFit) FitToView(); };
        _scroll.PreviewMouseWheel += OnMouseWheelZoom;
        _scroll.PreviewMouseLeftButtonDown += OnMouseDownPan;
        _scroll.PreviewMouseMove += OnMouseMovePan;
        _scroll.PreviewMouseLeftButtonUp += OnMouseUpPan;

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
        _autoFit = true;
        ApplyScale(1.0);
        _status.Text = Intro;
        _nulls.Visibility = Visibility.Collapsed;
    }

    private void ApplyScale(double scale, Point? anchorInViewport = null)
    {
        var oldScale = _scale;
        _scale = Math.Clamp(scale, MinZoom, MaxZoom);
        _scaleTransform.ScaleX = _scaleTransform.ScaleY = _scale;
        _zoomText.Text = $"{Math.Round(_scale * 100)}%";
        _autoZoom.IsEnabled = !_autoFit;

        if (anchorInViewport is not { } anchor || Math.Abs(_scale - oldScale) < 0.0001) return;
        _scroll.BeginAnimation(HorizontalOffsetProperty, null);
        _scroll.BeginAnimation(VerticalOffsetProperty, null);
        UpdateLayout();
        var ratio = _scale / oldScale;
        var targetX = (_scroll.HorizontalOffset + anchor.X) * ratio - anchor.X;
        var targetY = (_scroll.VerticalOffset + anchor.Y) * ratio - anchor.Y;
        _scroll.ScrollToHorizontalOffset(Math.Clamp(targetX, 0, Math.Max(0, _scroll.ExtentWidth - _scroll.ViewportWidth)));
        _scroll.ScrollToVerticalOffset(Math.Clamp(targetY, 0, Math.Max(0, _scroll.ExtentHeight - _scroll.ViewportHeight)));
    }

    private void FitToView()
    {
        if (_canvas.Width <= 0 || _canvas.Height <= 0 || _scroll.ViewportWidth <= 0 || _scroll.ViewportHeight <= 0)
        {
            ApplyScale(1.0);
            return;
        }
        var fitX = _scroll.ViewportWidth * 0.96 / _canvas.Width;
        var fitY = _scroll.ViewportHeight * 0.96 / _canvas.Height;
        ApplyScale(Math.Clamp(Math.Min(fitX, fitY), MinAutoZoom, 1.0));
    }

    private void OnMouseWheelZoom(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        _autoFit = false;
        var factor = e.Delta > 0 ? 1.12 : 1.0 / 1.12;
        ApplyScale(_scale * factor, e.GetPosition(_scroll));
    }

    private void OnMouseDownPan(object sender, MouseButtonEventArgs e)
    {
        if (_scroll.ExtentWidth <= _scroll.ViewportWidth && _scroll.ExtentHeight <= _scroll.ViewportHeight) return;
        _scroll.BeginAnimation(HorizontalOffsetProperty, null);
        _scroll.BeginAnimation(VerticalOffsetProperty, null);
        _dragStart = e.GetPosition(_scroll);
        _dragOffset = new Point(_scroll.HorizontalOffset, _scroll.VerticalOffset);
        _scroll.CaptureMouse();
        _scroll.Cursor = Cursors.SizeAll;
    }

    private void OnMouseMovePan(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start) return;
        var delta = e.GetPosition(_scroll) - start;
        _scroll.ScrollToHorizontalOffset(Math.Clamp(_dragOffset.X - delta.X, 0, Math.Max(0, _scroll.ExtentWidth - _scroll.ViewportWidth)));
        _scroll.ScrollToVerticalOffset(Math.Clamp(_dragOffset.Y - delta.Y, 0, Math.Max(0, _scroll.ExtentHeight - _scroll.ViewportHeight)));
    }

    private void OnMouseUpPan(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null) return;
        _dragStart = null;
        _scroll.ReleaseMouseCapture();
        _scroll.Cursor = null;
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
        _canvas.Width = Pad * 2 + Math.Max(cols - 1, 0) * (BoxW + GapX) + BoxW + 70;
        _canvas.Height = TopPad + (rows - 1) * RowGap + BoxH + 120;
        if (_autoFit) FitToView();

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
        if (_dragStart is not null) return;
        int? focus = frame.Hot.Count > 0 ? frame.Hot.First() : frame.Marks.FirstOrDefault(m => m.Node is not null && m.Name is not ("tail" or "head" or "head L"))?.Node;
        focus ??= frame.Nodes.Count > 0 ? frame.Nodes[0].Id : null;
        var center = focus is { } id && _positions.TryGetValue(id, out var p)
            ? new Point((p.X + BoxW / 2) * _scale, (p.Y + BoxH / 2) * _scale)
            : new Point(_canvas.Width * _scale / 2, _canvas.Height * _scale / 2);
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
        var lanesByRow = new Dictionary<int, List<List<(double Min, double Max)>>>();
        var lastLaneByRow = new Dictionary<int, int>();
        var loopIndex = 0;
        var crossIndex = 0;
        var maxBottom = _canvas.Height;

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
                var tone = GreenTones[crossIndex % GreenTones.Length];
                var thickness = 2.2 + (crossIndex % 3) * 0.45;
                crossIndex++;
                Arrow([new Point(dot.X, below ? p.Y + BoxH : p.Y), new Point(t.X + BoxW * 0.42, below ? t.Y : t.Y + BoxH)], tone, thickness);
            }
            else if (target.Col == node.Col + 1)
            {
                Arrow([dot, new Point(t.X, middle)]);
            }
            else
            {
                if (!lanesByRow.TryGetValue(node.Row, out var lanes))
                {
                    lanes = [];
                    lanesByRow[node.Row] = lanes;
                }
                var entryX = t.X + BoxW * 0.36;
                var minX = Math.Min(dot.X, entryX) - 12;
                var maxX = Math.Max(dot.X, entryX) + 12;
                lastLaneByRow.TryGetValue(node.Row, out var lastLane);
                if (!lastLaneByRow.ContainsKey(node.Row)) lastLane = -1;

                var lane = 0;
                while (true)
                {
                    while (lanes.Count <= lane) lanes.Add([]);
                    var free = lanes[lane].All(r => maxX < r.Min || minX > r.Max);
                    if (free && lane != lastLane) break;
                    lane++;
                }
                lanes[lane].Add((minX, maxX));
                lastLaneByRow[node.Row] = lane;

                var depth = 26 + lane * 20;
                var bottom = p.Y + BoxH;
                var y = bottom + depth;
                if (y + 24 > maxBottom) maxBottom = y + 24;

                var tone = GreenTones[loopIndex % GreenTones.Length];
                var thickness = 2.0 + (loopIndex % 3) * 0.55;
                loopIndex++;
                Arrow([new Point(dot.X, bottom), new Point(dot.X, y), new Point(entryX, y), new Point(entryX, bottom)], tone, thickness);
            }
        }

        if (maxBottom > _canvas.Height)
        {
            _canvas.Height = maxBottom;
            if (_autoFit) FitToView();
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

    private void Arrow(Point[] points, Brush? stroke = null, double thickness = 2.5)
    {
        stroke ??= Theme.AccentHover;
        var figure = new PathFigure { StartPoint = points[0] };
        foreach (var point in points.Skip(1)) figure.Segments.Add(new LineSegment(point, true));
        AddDecor(new WpfPath
        {
            Data = new PathGeometry([figure]), Stroke = stroke, StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round,
        });

        var tip = points[^1];
        var direction = tip - points[^2];
        if (direction.LengthSquared < 0.01) return;
        direction.Normalize();
        var side = new Vector(-direction.Y, direction.X);
        var headLen = 8.5 + thickness * 1.2;
        var headHalf = 4.2 + thickness * 0.65;
        AddDecor(new Polygon
        {
            Points = new PointCollection { tip, tip - direction * headLen + side * headHalf, tip - direction * headLen - side * headHalf },
            Fill = stroke,
        });
    }
}
