using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DynamicStructures.Core;
using WpfPath = System.Windows.Shapes.Path;

namespace DynamicStructures.UI;

/// <summary>Наглядная анимация примеров части 3: хеш-таблица с цепочками, очередь печати и двоичная куча.</summary>
public sealed class DemoAnimationView : Border
{
    private static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.RegisterAttached(
        "HorizontalOffset", typeof(double), typeof(DemoAnimationView),
        new PropertyMetadata(0.0, (d, e) => ((ScrollViewer)d).ScrollToHorizontalOffset((double)e.NewValue)));

    private static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.RegisterAttached(
        "VerticalOffset", typeof(double), typeof(DemoAnimationView),
        new PropertyMetadata(0.0, (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));

    private const double MinZoom = 0.2, MaxZoom = 2.5, MinAutoZoom = 0.25;

    private readonly Func<double> _speed;
    private readonly TextBlock _status = new()
    {
        Foreground = Theme.Text, FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _zoomText = new() { Foreground = Theme.Muted, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _autoZoom = Theme.Pill("Авто", false);
    private readonly Canvas _canvas = new();
    private readonly ScaleTransform _scaleTransform = new(1, 1);
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };

    private double _scale = 1.0;
    private bool _autoFit = true;
    private Point? _dragStart;
    private Point _dragOffset;

    public DemoAnimationView(Func<double> speed)
    {
        _speed = speed;
        _canvas.LayoutTransform = _scaleTransform;

        _autoZoom.Padding = new Thickness(12, 4, 12, 4);
        _autoZoom.FontSize = 13;
        _autoZoom.ToolTip = "Подогнать масштаб под содержимое";
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
        statusBody.Children.Add(_status);

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
        _canvas.Width = _canvas.Height = 0;
        _autoFit = true;
        ApplyScale(1.0);
        _status.Text = "Выберите пример части 3 и нажмите «Старт».";
    }

    public async Task PlayAsync(IReadOnlyList<DemoFrame> frames, string title, CancellationToken ct)
    {
        Reset();
        _status.Text = $"Анимация: {title}";
        await PauseAsync(900, ct);
        for (var i = 0; i < frames.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            _status.Text = $"Шаг {i + 1} из {frames.Count}: {frames[i].Note}";
            Render(frames[i]);
            if (_autoFit) FitToView();
            await PauseAsync(Math.Clamp(frames[i].Note.Length * 18, 700, 2000), ct);
        }
        await PauseAsync(1000, ct);
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
        var fitX = _scroll.ViewportWidth * 0.95 / _canvas.Width;
        var fitY = _scroll.ViewportHeight * 0.95 / _canvas.Height;
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

    private void Render(DemoFrame frame)
    {
        _canvas.Children.Clear();
        switch (frame)
        {
            case HashTableFrame ht:
                RenderHashTable(ht);
                break;
            case PrintQueueFrame pq:
                RenderPrintQueue(pq);
                break;
            case HeapFrame hp:
                RenderHeap(hp);
                break;
            case QueueFrame q:
                RenderQueue(q);
                break;
        }
    }

    private void Place(UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        _canvas.Children.Add(element);
    }

    private void Arrow(Point from, Point to, Brush? brush = null)
    {
        brush ??= Theme.AccentHover;
        var figure = new PathFigure { StartPoint = from, Segments = { new LineSegment(to, true) } };
        _canvas.Children.Add(new WpfPath
        {
            Data = new PathGeometry([figure]), Stroke = brush, StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        var dir = to - from;
        if (dir.LengthSquared < 0.01) return;
        dir.Normalize();
        var side = new Vector(-dir.Y, dir.X);
        _canvas.Children.Add(new Polygon
        {
            Points = new PointCollection { to, to - dir * 9 + side * 5, to - dir * 9 - side * 5 },
            Fill = brush,
        });
    }

    private void RenderHashTable(HashTableFrame f)
    {
        const double pad = 32, bucketW = 64, rowH = 66, nodeW = 148, nodeH = 44, gapX = 36;
        var maxChain = f.Buckets.Count == 0 ? 0 : f.Buckets.Max(b => b.Count);
        _canvas.Width = pad * 2 + bucketW + 40 + Math.Max(maxChain, 1) * (nodeW + gapX) + 60;
        _canvas.Height = pad * 2 + f.Buckets.Count * rowH;

        for (var i = 0; i < f.Buckets.Count; i++)
        {
            var y = pad + i * rowH;
            var active = i == f.ActiveBucket;
            var bucketBox = new Border
            {
                Width = bucketW, Height = nodeH, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2),
                Background = active ? Theme.SurfaceHigh : Theme.Surface,
                BorderBrush = active ? Theme.AccentHover : Theme.Stroke,
                Child = new TextBlock
                {
                    Text = $"[{i}]", FontSize = 16, FontWeight = FontWeights.Bold,
                    Foreground = active ? Theme.AccentHover : Theme.Text,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Place(bucketBox, pad, y);

            var chain = f.Buckets[i];
            var x = pad + bucketW + gapX;
            var midY = y + nodeH / 2;
            if (chain.Count == 0)
            {
                Arrow(new Point(pad + bucketW, midY), new Point(x - 6, midY), Theme.Muted);
                Place(new TextBlock { Text = "null", Foreground = Theme.Muted, FontSize = 14, FontStyle = FontStyles.Italic }, x, midY - 10);
                continue;
            }

            Arrow(new Point(pad + bucketW, midY), new Point(x, midY), active ? Theme.AccentHover : Theme.Muted);
            for (var j = 0; j < chain.Count; j++)
            {
                var item = chain[j];
                var hit = active && f.ActiveKey == item.Key;
                var bg = hit ? (f.IsDelete ? Theme.DangerSoft : Theme.Accent) : Theme.Surface;
                var bd = hit ? (f.IsDelete ? Theme.Danger : Theme.AccentHover) : Theme.Accent;
                var fg = hit ? (f.IsDelete ? Theme.DangerText : Theme.OnAccent) : Theme.Text;

                var card = new Border
                {
                    Width = nodeW, Height = nodeH, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2),
                    Background = bg, BorderBrush = bd, Padding = new Thickness(8, 0, 8, 0),
                    Child = new TextBlock
                    {
                        Text = $"{item.Key} = {item.Value}", FontSize = 14, FontWeight = FontWeights.SemiBold,
                        Foreground = fg, TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                Place(card, x, y);

                var nextX = x + nodeW + gapX;
                if (j + 1 < chain.Count)
                {
                    Arrow(new Point(x + nodeW, midY), new Point(nextX, midY));
                }
                else
                {
                    Arrow(new Point(x + nodeW, midY), new Point(x + nodeW + 24, midY), Theme.Muted);
                    Place(new TextBlock { Text = "null", Foreground = Theme.Muted, FontSize = 14, FontStyle = FontStyles.Italic }, x + nodeW + 28, midY - 10);
                }
                x = nextX;
            }
        }
    }

    private void RenderPrintQueue(PrintQueueFrame f)
    {
        const double pad = 28, labelW = 115, cardW = 156, cardH = 46, gapX = 16, rowH = 68;
        var maxLen = f.Queues.Max(q => q.Count);
        _canvas.Width = Math.Max(720, pad * 2 + labelW + Math.Max(maxLen, 3) * (cardW + gapX));
        _canvas.Height = pad * 2 + 110 + 3 * rowH + 60;

        // Карточка принтера
        var printerText = f.CurrentJob is null
            ? $"Принтер свободен  •  Такт: {f.Clock}"
            : $"Печатается: {f.CurrentJob}  ({f.Printed} / {f.TotalPages} стр.)  •  Такт: {f.Clock}";
        var printerCard = new Border
        {
            Width = _canvas.Width - pad * 2, Height = 76, CornerRadius = new CornerRadius(14),
            Background = f.CurrentJob is null ? Theme.Surface : Theme.SurfaceHigh,
            BorderBrush = f.CurrentJob is null ? Theme.Stroke : Theme.Accent, BorderThickness = new Thickness(2),
            Padding = new Thickness(18, 12, 18, 12),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = printerText, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text },
                    new ProgressBar
                    {
                        Height = 8, Margin = new Thickness(0, 8, 0, 0),
                        Minimum = 0, Maximum = Math.Max(1, f.TotalPages), Value = f.CurrentJob is null ? 0 : f.Printed,
                        Foreground = Theme.Accent, Background = Theme.Stroke, BorderThickness = new Thickness(0),
                    },
                },
            },
        };
        Place(printerCard, pad, pad);

        string[] names = ["Высокий", "Обычный", "Низкий"];
        for (var p = 0; p < 3; p++)
        {
            var y = pad + 100 + p * rowH;
            Place(new TextBlock
            {
                Text = names[p] + ":", Width = labelW - 10, FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = p == 0 ? Theme.AccentHover : Theme.Text, TextAlignment = TextAlignment.Right,
            }, pad, y + 12);

            var q = f.Queues[p];
            if (q.Count == 0)
            {
                Place(new TextBlock { Text = "(пусто)", FontSize = 14, Foreground = Theme.Muted, FontStyle = FontStyles.Italic }, pad + labelW, y + 13);
                continue;
            }
            for (var i = 0; i < q.Count; i++)
            {
                var x = pad + labelW + i * (cardW + gapX);
                var jobName = q[i].Split(' ')[0];
                var hl = f.HighlightJob == jobName;
                var card = new Border
                {
                    Width = cardW, Height = cardH, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2),
                    Background = hl ? (f.IsCancel ? Theme.DangerSoft : Theme.Accent) : Theme.Surface,
                    BorderBrush = hl ? (f.IsCancel ? Theme.Danger : Theme.AccentHover) : Theme.Accent,
                    Child = new TextBlock
                    {
                        Text = q[i], FontSize = 14, FontWeight = FontWeights.SemiBold,
                        Foreground = hl ? (f.IsCancel ? Theme.DangerText : Theme.OnAccent) : Theme.Text,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                Place(card, x, y);
            }
        }

        var doneText = f.Done.Count == 0 ? "Напечатано: —" : $"Напечатано ({f.Done.Count}): {string.Join(" → ", f.Done)}";
        Place(new TextBlock { Text = doneText, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted }, pad, pad + 100 + 3 * rowH + 12);
    }

    private void RenderHeap(HeapFrame f)
    {
        const double pad = 32, nodeD = 52, levelH = 92;
        var n = f.Heap.Count;
        var levels = n == 0 ? 1 : (int)Math.Floor(Math.Log2(n)) + 1;
        var maxLeaves = Math.Max(4, 1 << Math.Min(levels - 1, 5));
        var treeW = Math.Max(680, maxLeaves * 76.0);
        var arrayCell = 52.0;
        var arrTotal = Math.Max(n, f.Sorted.Count) * (arrayCell + 8);
        _canvas.Width = pad * 2 + Math.Max(treeW, arrTotal);
        var treeH = levels * levelH + 20;
        _canvas.Height = pad * 2 + treeH + 170;

        var centers = new Point[n];
        for (var i = 0; i < n; i++)
        {
            var lvl = (int)Math.Floor(Math.Log2(i + 1));
            var posInLvl = i - ((1 << lvl) - 1);
            var slots = 1 << lvl;
            var x = pad + (_canvas.Width - pad * 2) * (posInLvl + 0.5) / slots;
            var y = pad + lvl * levelH + nodeD / 2;
            centers[i] = new Point(x, y);
        }

        for (var i = 1; i < n; i++)
        {
            var parent = (i - 1) / 2;
            var hotEdge = f.IsSwap && ((i == f.FocusA && parent == f.FocusB) || (i == f.FocusB && parent == f.FocusA));
            _canvas.Children.Add(new Line
            {
                X1 = centers[parent].X, Y1 = centers[parent].Y, X2 = centers[i].X, Y2 = centers[i].Y,
                Stroke = hotEdge ? Theme.Danger : Theme.Stroke, StrokeThickness = hotEdge ? 3.5 : 2,
            });
        }

        for (var i = 0; i < n; i++)
        {
            var isFocus = i == f.FocusA || i == f.FocusB;
            var bg = isFocus ? (f.IsSwap ? Theme.DangerSoft : Theme.Accent) : Theme.Surface;
            var bd = isFocus ? (f.IsSwap ? Theme.Danger : Theme.AccentHover) : Theme.Accent;
            var fg = isFocus ? (f.IsSwap ? Theme.DangerText : Theme.OnAccent) : Theme.Text;

            var circle = new Border
            {
                Width = nodeD, Height = nodeD, CornerRadius = new CornerRadius(nodeD / 2), BorderThickness = new Thickness(2.5),
                Background = bg, BorderBrush = bd,
                Child = new TextBlock
                {
                    Text = f.Heap[i].ToString(), FontSize = 18, FontWeight = FontWeights.Bold, Foreground = fg,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Place(circle, centers[i].X - nodeD / 2, centers[i].Y - nodeD / 2);
        }

        if (n == 0)
            Place(new TextBlock { Text = "Куча пуста", FontSize = 16, Foreground = Theme.Muted, FontStyle = FontStyles.Italic }, _canvas.Width / 2 - 45, pad + 20);

        // Массив кучи
        var arrY = pad + treeH;
        Place(new TextBlock { Text = "Массив кучи:", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted }, pad, arrY);
        for (var i = 0; i < n; i++)
        {
            var isFocus = i == f.FocusA || i == f.FocusB;
            var cell = new Border
            {
                Width = arrayCell, Height = 40, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2),
                Background = isFocus ? (f.IsSwap ? Theme.DangerSoft : Theme.Accent) : Theme.Surface,
                BorderBrush = isFocus ? (f.IsSwap ? Theme.Danger : Theme.AccentHover) : Theme.Stroke,
                Child = new TextBlock
                {
                    Text = f.Heap[i].ToString(), FontSize = 15, FontWeight = FontWeights.SemiBold,
                    Foreground = isFocus ? (f.IsSwap ? Theme.DangerText : Theme.OnAccent) : Theme.Text,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Place(cell, pad + i * (arrayCell + 8), arrY + 24);
        }

        // Отсортированный массив
        var sortY = arrY + 80;
        Place(new TextBlock { Text = "Отсортированный массив:", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted }, pad, sortY);
        for (var i = 0; i < f.Sorted.Count; i++)
        {
            var val = f.Sorted[i];
            var cell = new Border
            {
                Width = arrayCell, Height = 40, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2),
                Background = val is not null ? Theme.SurfaceHigh : Theme.Surface,
                BorderBrush = val is not null ? Theme.Accent : Theme.Stroke,
                Child = new TextBlock
                {
                    Text = val?.ToString() ?? "—", FontSize = 15, FontWeight = FontWeights.SemiBold,
                    Foreground = val is not null ? Theme.Text : Theme.Muted,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Place(cell, pad + i * (arrayCell + 8), sortY + 24);
        }
    }

    private void RenderQueue(QueueFrame f)
    {
        const double pad = 40, labelW = 140, boxW = 104, boxH = 54, gapX = 28;
        var count = f.Items.Count;
        _canvas.Width = Math.Max(680, pad * 2 + labelW * 2 + Math.Max(count, 1) * (boxW + gapX));
        _canvas.Height = 240;

        var y = 80.0;
        var midY = y + boxH / 2;
        Place(new TextBlock
        {
            Text = f.ImplTitle, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Theme.AccentHover,
        }, pad, 34);
        Place(new TextBlock
        {
            Text = "начало (выход) →", Width = labelW - 10, FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = Theme.AccentHover, TextAlignment = TextAlignment.Right,
        }, pad, midY - 10);

        if (count == 0)
        {
            Place(new TextBlock { Text = "(пусто)", FontSize = 18, Foreground = Theme.Muted, FontStyle = FontStyles.Italic }, pad + labelW + 10, midY - 12);
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                var x = pad + labelW + i * (boxW + gapX);
                var hl = i == f.HighlightIndex;
                var bg = hl ? (f.IsRemove ? Theme.DangerSoft : Theme.Accent) : (f.IsShift ? Theme.SurfaceHigh : Theme.Surface);
                var bd = hl ? (f.IsRemove ? Theme.Danger : Theme.AccentHover) : Theme.Accent;
                var fg = hl ? (f.IsRemove ? Theme.DangerText : Theme.OnAccent) : Theme.Text;

                var card = new Border
                {
                    Width = boxW, Height = boxH, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2),
                    Background = bg, BorderBrush = bd, Padding = new Thickness(8, 0, 8, 0),
                    Child = new TextBlock
                    {
                        Text = f.Items[i], FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = fg,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                Place(card, x, y);
                if (i + 1 < count) Arrow(new Point(x + boxW, midY), new Point(x + boxW + gapX, midY));
            }
            var endX = pad + labelW + count * (boxW + gapX) - gapX + 12;
            Place(new TextBlock
            {
                Text = f.IsShift ? "← сдвиг влево" : "← конец (вход)", FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = f.IsShift ? Theme.AccentHover : Theme.Muted,
            }, endX, midY - 10);
        }

        Place(new TextBlock
        {
            Text = $"Элементов в очереди: {count}   •   Элементарных действий: {f.StepsCount}",
            FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted,
        }, pad, y + boxH + 34);
    }
}
