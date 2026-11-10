using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DynamicStructures.Core;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using Microsoft.Win32;
using SkiaSharp;

namespace DynamicStructures.UI;

/// <summary>Весь интерфейс приложения, построенный кодом.</summary>
public sealed class MainView : Grid
{
    private enum Mode { Stack, Postfix, Infix, Queue, List }

    private sealed record MenuItem(Mode Mode, string Title, string SamplePath, ListAlgorithm? Algorithm = null);

    private static readonly MenuItem[] Menu =
    [
        new(Mode.Stack, "Часть 1. Стек: операции из файла", "stack"),
        new(Mode.Postfix, "Часть 1. Стек: постфиксное вычисление", "postfix.txt"),
        new(Mode.Infix, "Часть 1. Стек: инфикс → постфикс", ""),
        new(Mode.Queue, "Часть 2. Очередь: List<T> против Queue<T>", "queue_length"),
        .. ListAlgorithms.All.Select((a, i) => new MenuItem(Mode.List, $"Часть 4. Список №{i + 1}: {char.ToLowerInvariant(a.Title[0])}{a.Title[1..]}", a.Sample, a)),
    ];

    private readonly ComboBox _modes = new() { Width = 440, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _path = Input();
    private readonly TextBox _formula = Input();
    private readonly TextBox _params = Input();
    private readonly TextBlock _paramsLabel = new() { Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly StackPanel _paramsPanel = new() { Orientation = Orientation.Horizontal };
    private readonly Button _fileButton = Theme.Pill("Файл…", false);
    private readonly Button _folderButton = Theme.Pill("Папка…", false);
    private readonly Button _start = Theme.Pill("Старт", true);
    private readonly Button _stop = Theme.Pill("Стоп", false);
    private readonly Button _clear = Theme.Pill("Очистить график", false);
    private readonly Grid _pathRow = new();
    private readonly Border _progressRow;
    private readonly ProgressBar _progress = new() { Height = 6, Minimum = 0, Maximum = 1, Foreground = Theme.Accent, Background = Theme.SurfaceHigh, BorderThickness = new Thickness(0) };
    private readonly CartesianChart _chart = new() { LegendPosition = LegendPosition.Top, Margin = new Thickness(12, 8, 12, 12) };
    private readonly Border _outputPanel;
    private readonly TextBox _output = new()
    {
        IsReadOnly = true, FontFamily = Theme.Mono, FontSize = 13, Foreground = Theme.Text, Background = Theme.Background,
        BorderThickness = new Thickness(0), Padding = new Thickness(10), TextWrapping = TextWrapping.NoWrap,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Text = "Выберите пункт в списке и нажмите «Старт».",
    };
    private readonly Button _toggle = Theme.Pill("˅", false);

    private readonly Dictionary<string, string> _paths = [];
    private readonly Dictionary<Mode, MeasurementStore> _stores = new()
    {
        [Mode.Stack] = new(Path.Combine(AppContext.BaseDirectory, "results", "stack.csv")),
        [Mode.Queue] = new(Path.Combine(AppContext.BaseDirectory, "results", "queue.csv")),
    };
    private CancellationTokenSource? _cts;
    private bool _outputExpanded = true;

    private MenuItem CurrentItem => Menu[Math.Max(_modes.SelectedIndex, 0)];
    private Mode CurrentMode => CurrentItem.Mode;
    private bool IsChartMode => CurrentMode is Mode.Stack or Mode.Queue;

    public MainView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());

        var topBar = BuildTopBar();
        _progressRow = BuildProgressRow();
        _outputPanel = BuildOutputPanel();

        var content = Theme.Card(new Grid { Children = { _chart, _outputPanel } }, new Thickness(16, 0, 16, 16));
        content.Padding = new Thickness(0);
        SetRow(topBar, 0);
        SetRow(_progressRow, 1);
        SetRow(content, 2);
        Children.Add(topBar);
        Children.Add(_progressRow);
        Children.Add(content);

        foreach (var item in Menu)
            _paths[item.SamplePath] = item.SamplePath.Length > 0 ? Path.Combine(AppContext.BaseDirectory, "samples", item.SamplePath) : "";
        _formula.Text = "(7+8)/(3+2)";
        _modes.ItemsSource = Menu.Select(m => m.Title).ToArray();
        _modes.SelectionChanged += (_, _) => OnModeChanged();
        _path.TextChanged += (_, _) => _paths[CurrentItem.SamplePath] = _path.Text;
        _fileButton.Click += (_, _) => BrowseFile();
        _folderButton.Click += (_, _) => BrowseFolder();
        _start.Click += async (_, _) => await RunAsync();
        _stop.Click += (_, _) => _cts?.Cancel();
        _clear.Click += (_, _) => { _stores[CurrentMode].Clear(); RefreshChart(); };
        _toggle.Click += (_, _) => { _outputExpanded = !_outputExpanded; UpdateOutputLayout(); };
        _stop.IsEnabled = false;
        _modes.SelectedIndex = 0;
    }

    private static TextBox Input() => new()
    {
        FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 6, 10, 6),
        Margin = new Thickness(12, 0, 0, 0),
    };

    private Border BuildTopBar()
    {
        _pathRow.ColumnDefinitions.Add(new ColumnDefinition());
        _pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        SetColumn(_fileButton, 1);
        SetColumn(_folderButton, 2);
        SetColumn(_paramsPanel, 3);
        _params.Width = 90;
        _params.Margin = new Thickness(8, 0, 0, 0);
        _paramsPanel.Children.Add(_paramsLabel);
        _paramsPanel.Children.Add(_params);
        _pathRow.Children.Add(_path);
        _pathRow.Children.Add(_fileButton);
        _pathRow.Children.Add(_folderButton);
        _pathRow.Children.Add(_paramsPanel);

        var bar = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_modes, Dock.Left);
        foreach (var button in new[] { _clear, _stop, _start }) DockPanel.SetDock(button, Dock.Right);
        bar.Children.Add(_modes);
        bar.Children.Add(_clear);
        bar.Children.Add(_stop);
        bar.Children.Add(_start);
        bar.Children.Add(new Grid { Children = { _pathRow, _formula } });
        return Theme.Card(bar, new Thickness(16, 16, 16, 12));
    }

    private Border BuildProgressRow()
    {
        var panel = new StackPanel();
        panel.Children.Add(_progress);
        panel.Children.Add(new TextBlock
        {
            Text = "Выключите ВПН для стабильной работы приложения",
            Foreground = Theme.Muted, FontStyle = FontStyles.Italic, Margin = new Thickness(2, 8, 0, 0),
        });
        var row = Theme.Card(panel, new Thickness(16, 0, 16, 12));
        row.Visibility = Visibility.Collapsed;
        return row;
    }

    private Border BuildOutputPanel()
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _toggle.Padding = new Thickness(14, 4, 14, 4);
        _toggle.ToolTip = "Свернуть / развернуть";
        DockPanel.SetDock(_toggle, Dock.Right);
        header.Children.Add(_toggle);
        header.Children.Add(new TextBlock
        {
            Text = "Вывод", Foreground = Theme.Text, FontSize = 15, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0),
        });

        var body = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);
        body.Children.Add(new Border { Child = _output, CornerRadius = new CornerRadius(14), ClipToBounds = true, Background = Theme.Background });

        _output.Height = 320;
        var panel = Theme.Card(body, new Thickness(16));
        panel.Width = 540;
        panel.HorizontalAlignment = HorizontalAlignment.Right;
        panel.VerticalAlignment = VerticalAlignment.Bottom;
        panel.Background = Theme.SurfaceHigh;
        panel.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.15 };
        return panel;
    }

    private void OnModeChanged()
    {
        var mode = CurrentMode;
        _path.Text = _paths[CurrentItem.SamplePath];
        var parameters = CurrentItem.Algorithm?.Parameters ?? "";
        _paramsPanel.Visibility = parameters.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _paramsLabel.Text = $"{parameters} =";
        _params.Text = CurrentItem.Algorithm?.Defaults ?? "";
        _pathRow.Visibility = mode == Mode.Infix ? Visibility.Collapsed : Visibility.Visible;
        _formula.Visibility = mode == Mode.Infix ? Visibility.Visible : Visibility.Collapsed;
        _folderButton.Visibility = _clear.Visibility = _chart.Visibility = IsChartMode ? Visibility.Visible : Visibility.Collapsed;
        RefreshChart();
        UpdateOutputLayout();
    }

    private void UpdateOutputLayout()
    {
        _output.Visibility = _outputExpanded ? Visibility.Visible : Visibility.Collapsed;
        _toggle.Content = _outputExpanded ? "˅" : "^";
    }

    private void BrowseFile()
    {
        var dialog = new OpenFileDialog { Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы|*.*" };
        if (dialog.ShowDialog() == true) _path.Text = dialog.FileName;
    }

    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Папка с наборами input.txt" };
        if (dialog.ShowDialog() == true) _path.Text = dialog.FolderName;
    }

    private async Task RunAsync()
    {
        var item = CurrentItem;
        var path = _path.Text.Trim().Trim('"');
        var formula = _formula.Text;
        var parameters = _params.Text;
        var log = new StringBuilder();
        _cts = new CancellationTokenSource();
        var progress = new Progress<double>(value => _progress.Value = value);

        SetRunning(true);
        try
        {
            await Task.Run(() => Execute(item, path, formula, parameters, log, progress, _cts.Token));
        }
        catch (OperationCanceledException)
        {
            log.AppendLine("Остановлено пользователем.");
        }
        catch (Exception ex)
        {
            log.AppendLine($"Ошибка: {ex.Message}").AppendLine("Выполнение остановлено.");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetRunning(false);
        }

        _output.Text = log.ToString();
        _output.ScrollToHome();
        if (IsChartMode) RefreshChart();
    }

    private void Execute(MenuItem item, string path, string formula, string parameters, StringBuilder log, IProgress<double> progress, CancellationToken ct)
    {
        var mode = item.Mode;
        switch (mode)
        {
            case Mode.Stack or Mode.Queue:
                var files = Runner.ResolveFiles(path);
                var results = Runner.Measure(files, mode == Mode.Stack ? Runner.Stack : Runner.Queues, log, progress, ct);
                _stores[mode].Merge(results);
                break;
            case Mode.Postfix:
                log.Append(Expressions.EvaluateFile(path));
                break;
            case Mode.Infix:
                var postfix = Expressions.InfixToPostfix(formula);
                log.AppendLine($"Инфиксная запись:   {formula}")
                   .AppendLine($"Постфиксная запись: {postfix}")
                   .AppendLine()
                   .Append(Expressions.Describe(Expressions.EvaluatePostfix(postfix)));
                break;
            case Mode.List:
                log.Append(ListAlgorithms.Run(item.Algorithm!, path, parameters));
                break;
        }
    }

    private void SetRunning(bool running)
    {
        _start.IsEnabled = !running;
        _stop.IsEnabled = running;
        _modes.IsEnabled = _pathRow.IsEnabled = _formula.IsEnabled = _clear.IsEnabled = !running;
        _progress.Value = 0;
        _progress.IsIndeterminate = running && !IsChartMode;
        _progressRow.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        _chart.Opacity = running ? 0.35 : 1;
        _chart.IsHitTestVisible = !running;
        if (running) return;
        _outputExpanded = true;
        UpdateOutputLayout();
    }

    /// <summary>Перерисовывает график из сохранённых замеров: по оси X — наборы, отсортированные по числу операций.</summary>
    private void RefreshChart()
    {
        if (!IsChartMode) return;
        var items = _stores[CurrentMode].Items;
        var files = items.GroupBy(m => m.File)
            .Select(g => (Name: g.Key, g.First().Operations))
            .OrderBy(f => f.Operations).ThenBy(f => f.Name)
            .ToList();

        (string Name, Func<Measurement, bool> Match, Func<Measurement, double> Value, SKColor Color)[] lines =
            CurrentMode == Mode.Stack
                ? [("Считывание", _ => true, m => m.ReadMs, Theme.ChartLime), ("Выполнение", _ => true, m => m.ExecMs, Theme.ChartAccent)]
                : [("Queue<T>", m => m.Series == "Queue<T>", m => m.ExecMs, Theme.ChartAccent),
                   ("List<T>", m => m.Series == "List<T>", m => m.ExecMs, Theme.ChartLime)];

        _chart.Series = lines.Select(line => (ISeries)new LineSeries<double>
        {
            Name = line.Name,
            Values = files.Select(f => items.FirstOrDefault(m => m.File == f.Name && line.Match(m)) is { } m ? line.Value(m) : 0).ToArray(),
            Stroke = new SolidColorPaint(line.Color, 3),
            GeometryStroke = new SolidColorPaint(line.Color, 3),
            GeometryFill = new SolidColorPaint(Theme.ChartBackground),
            GeometrySize = 10,
            Fill = null,
            LineSmoothness = 0.3,
        }).ToArray();

        var text = new SolidColorPaint(Theme.ChartText);
        _chart.XAxes = [new Axis { Name = "Наборы операций (файл, кол-во операций)", NamePaint = text, LabelsPaint = text, TextSize = 12, Labels = files.Select(f => $"{f.Name}\n{f.Operations}").ToArray() }];
        _chart.YAxes = [new Axis { Name = "Время, мс", NamePaint = text, LabelsPaint = text, TextSize = 12, MinLimit = 0, SeparatorsPaint = new SolidColorPaint(Theme.ChartGrid, 1) }];
        _chart.LegendTextPaint = text;
    }
}
