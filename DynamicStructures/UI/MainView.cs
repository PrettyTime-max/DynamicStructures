using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DynamicStructures.Core;
using Microsoft.Win32;

namespace DynamicStructures.UI;

/// <summary>Весь интерфейс приложения, построенный кодом.</summary>
public sealed class MainView : Grid
{
    private enum Mode { Stack, Postfix, Infix, Queue, List, VisStack }

    private sealed record MenuItem(Mode Mode, string Title, string SamplePath, ListAlgorithm? Algorithm = null);

    /// <summary>Что показать в анимации после первого прогона (замер времени и вывод).</summary>
    private sealed class Plan
    {
        public List<AnimationScript> Scripts { get; } = [];
    }

    private const string Hint = "Выберите пункт в списке и нажмите «Старт».";

    private static readonly MenuItem[] Menu =
    [
        new(Mode.Stack, "Часть 1. Стек: операции из файла", "stack"),
        new(Mode.Postfix, "Часть 1. Стек: постфиксное вычисление", "postfix.txt"),
        new(Mode.Infix, "Часть 1. Стек: инфикс → постфикс", "infix.txt"),
        new(Mode.Queue, "Часть 2. Очередь: Queue<T>, свой список, List<T>", "queue_length"),
        .. ListAlgorithms.All.Select((a, i) => new MenuItem(Mode.List, $"Часть 4. №{i + 1}: {char.ToLowerInvariant(a.Title[0])}{a.Title[1..]}", a.Sample, a)),
        new(Mode.VisStack, "Наглядно: стек", ""),
    ];

    private readonly ComboBox _modes = new() { Width = 440, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _path = Input();
    private const string AllFiles = "Все файлы папки";
    private const string AllExpressions = "Все выражения";

    /// <summary>Пункт списка выбора: файл папки (<paramref name="File"/>) или строка файла (<paramref name="Line"/>).</summary>
    private sealed record Choice(string Title, string? File = null, int Line = 0)
    {
        public override string ToString() => Title;
    }

    private readonly ComboBox _fileSelect = new() { Width = 260, Margin = new Thickness(12, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Что выполнить: всё или что-то одно" };
    private readonly TextBox _params = Input();
    private readonly TextBlock _paramsLabel = new() { Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly StackPanel _paramsPanel = new() { Orientation = Orientation.Horizontal };
    private readonly Button _fileButton = Theme.Pill("Файл…", false);
    private readonly Button _folderButton = Theme.Pill("Папка…", false);
    private readonly Button _start = Theme.Pill("Старт", true);
    private readonly Button _stop = Theme.Pill("Стоп", false);
    private readonly Grid _pathRow = new();
    private readonly Border _progressRow;
    private readonly ProgressBar _progress = new() { Height = 6, Minimum = 0, Maximum = 1, Foreground = Theme.Accent, Background = Theme.SurfaceHigh, BorderThickness = new Thickness(0) };
    private readonly Slider _speed = new() { Minimum = 0.1, Maximum = 3, Value = 1, SmallChange = 0.1, LargeChange = 0.5, Margin = new Thickness(0, 4, 0, 10) };
    private readonly TextBlock _speedLabel = new() { Foreground = Theme.Muted };
    private readonly Grid _stage = new();
    private readonly StructureView _view;
    private readonly TextBox _output = new()
    {
        IsReadOnly = true, FontFamily = Theme.Mono, FontSize = 13, Foreground = Theme.Text, Background = Theme.Background,
        BorderThickness = new Thickness(0), Padding = new Thickness(10), TextWrapping = TextWrapping.NoWrap,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Text = Hint,
    };

    private readonly Dictionary<string, string> _paths = [];
    private CancellationTokenSource? _cts;

    private MenuItem CurrentItem => Menu[Math.Max(_modes.SelectedIndex, 0)];
    private Mode CurrentMode => CurrentItem.Mode;
    private bool IsVisualMode => CurrentMode == Mode.VisStack;
    private bool HasAnimation => CurrentMode is Mode.Stack or Mode.Postfix or Mode.Infix or Mode.VisStack;
    private bool UsesFolder => CurrentMode is Mode.Stack or Mode.Queue;

    public MainView()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());

        var topBar = BuildTopBar();
        _progressRow = BuildProgressRow();

        _stage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        _stage.ColumnDefinitions.Add(new ColumnDefinition());
        var outputPanel = BuildOutputPanel();
        _stage.Children.Add(outputPanel);
        _view = new StructureView(AppendLog, () => _speed.Value);
        SetColumn(_view, 1);
        _stage.Children.Add(_view);

        var content = Theme.Card(_stage, new Thickness(16, 0, 16, 16));
        content.Padding = new Thickness(0);
        SetRow(topBar, 0);
        SetRow(_progressRow, 1);
        SetRow(content, 2);
        Children.Add(topBar);
        Children.Add(_progressRow);
        Children.Add(content);

        foreach (var item in Menu)
            _paths[item.SamplePath] = item.SamplePath.Length > 0 ? Path.Combine(AppContext.BaseDirectory, "samples", item.SamplePath) : "";
        _modes.ItemsSource = Menu.Select(m => m.Title).ToArray();
        _modes.SelectionChanged += (_, _) => OnModeChanged();
        _path.TextChanged += (_, _) =>
        {
            _paths[CurrentItem.SamplePath] = _path.Text;
            RefreshFiles();
        };
        _fileButton.Click += (_, _) => BrowseFile();
        _folderButton.Click += (_, _) => BrowseFolder();
        _start.Click += async (_, _) => await RunAsync();
        _stop.Click += (_, _) => _cts?.Cancel();
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
        for (var i = 0; i < 4; i++) _pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        SetColumn(_fileSelect, 1);
        SetColumn(_fileButton, 2);
        SetColumn(_folderButton, 3);
        SetColumn(_paramsPanel, 4);
        _params.Width = 90;
        _params.Margin = new Thickness(8, 0, 0, 0);
        _paramsPanel.Children.Add(_paramsLabel);
        _paramsPanel.Children.Add(_params);
        _pathRow.Children.Add(_path);
        _pathRow.Children.Add(_fileSelect);
        _pathRow.Children.Add(_fileButton);
        _pathRow.Children.Add(_folderButton);
        _pathRow.Children.Add(_paramsPanel);

        var bar = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_modes, Dock.Left);
        foreach (var button in new[] { _stop, _start }) DockPanel.SetDock(button, Dock.Right);
        bar.Children.Add(_modes);
        bar.Children.Add(_stop);
        bar.Children.Add(_start);
        bar.Children.Add(_pathRow);
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
        _speedLabel.Text = SpeedText();
        _speed.ValueChanged += (_, _) => _speedLabel.Text = SpeedText();

        var body = new DockPanel();
        var title = new TextBlock
        {
            Text = "Вывод", Foreground = Theme.Text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 0, 8),
        };
        foreach (UIElement element in new UIElement[] { title, _speedLabel, _speed }) DockPanel.SetDock(element, Dock.Top);
        body.Children.Add(title);
        body.Children.Add(_speedLabel);
        body.Children.Add(_speed);
        body.Children.Add(new Border { Child = _output, CornerRadius = new CornerRadius(14), ClipToBounds = true, Background = Theme.Background });

        var panel = Theme.Card(body, new Thickness(12));
        panel.Background = Theme.SurfaceHigh;
        return panel;
    }

    private string SpeedText() => $"Скорость анимации: {_speed.Value:0.0#}×";

    private void AppendLog(string line)
    {
        _output.AppendText(line + Environment.NewLine);
        _output.ScrollToEnd();
    }

    private void OnModeChanged()
    {
        var visual = IsVisualMode;
        _path.Text = _paths[CurrentItem.SamplePath];
        var parameters = CurrentItem.Algorithm?.Parameters ?? "";
        _paramsPanel.Visibility = parameters.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _paramsLabel.Text = $"{parameters} =";
        _params.Text = CurrentItem.Algorithm?.Defaults ?? "";
        _pathRow.Visibility = _start.Visibility = _stop.Visibility = visual ? Visibility.Collapsed : Visibility.Visible;
        _folderButton.Visibility = UsesFolder ? Visibility.Visible : Visibility.Collapsed;
        RefreshFiles();

        var animated = HasAnimation;
        _stage.ColumnDefinitions[0].Width = animated ? new GridLength(340) : new GridLength(1, GridUnitType.Star);
        _stage.ColumnDefinitions[1].Width = animated ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _view.Visibility = _speed.Visibility = _speedLabel.Visibility = animated ? Visibility.Visible : Visibility.Collapsed;
        _view.Reset();
        _view.ShowControls = _view.LogOperations = visual;
        _output.Text = visual ? "" : Hint;
    }

    /// <summary>
    /// Список «что выполнить»: файлы папки с наборами (стек, очередь) или отдельные выражения файла (постфикс, инфикс).
    /// Первый пункт всегда «всё».
    /// </summary>
    private void RefreshFiles()
    {
        var path = _path.Text.Trim().Trim('"');
        var choices = new List<Choice>();
        try
        {
            if (UsesFolder && Directory.Exists(path))
            {
                choices.Add(new(AllFiles));
                choices.AddRange(Directory.GetFiles(path, "*.txt").Select(Path.GetFileName).OfType<string>()
                    .Order(StringComparer.OrdinalIgnoreCase).Select(name => new Choice(name, name)));
            }
            else if (CurrentMode is Mode.Postfix or Mode.Infix && File.Exists(path))
            {
                choices.Add(new(AllExpressions));
                choices.AddRange(Expressions.ReadLines(path).Select(l =>
                    new Choice($"{l.Line}: {(l.Text.Length > 32 ? l.Text[..32] + "…" : l.Text)}", null, l.Line)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        if (choices.Count < 2)
        {
            _fileSelect.Visibility = Visibility.Collapsed;
            return;
        }
        var previous = (_fileSelect.SelectedItem as Choice)?.Title;
        _fileSelect.ItemsSource = choices;
        _fileSelect.SelectedItem = choices.FirstOrDefault(c => c.Title == previous) ?? choices[0];
        _fileSelect.Visibility = Visibility.Visible;
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

    /// <summary>Первый прогон (замеры, вывод) идёт в фоне, затем на экране проигрывается второй — анимация.</summary>
    private async Task RunAsync()
    {
        var item = CurrentItem;
        var path = _path.Text.Trim().Trim('"');
        var line = 0;
        if (_fileSelect.Visibility == Visibility.Visible && _fileSelect.SelectedItem is Choice choice)
        {
            if (choice.File is not null) path = Path.Combine(path, choice.File);
            line = choice.Line;
        }
        var parameters = _params.Text;
        var log = new StringBuilder();
        var plan = new Plan();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new Progress<double>(value => _progress.Value = value);

        SetRunning(true);
        try
        {
            await Task.Run(() =>
            {
                try { Execute(item, path, line, parameters, log, plan, progress, token); }
                catch (OperationCanceledException) { log.AppendLine("Остановлено пользователем."); }
                catch (Exception ex) when (ex is FormatException or ArithmeticException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                {
                    log.AppendLine($"Ошибка: {ex.Message}").AppendLine("Выполнение остановлено.");
                }
            });

            _output.Text = log.ToString();
            _output.ScrollToHome();
            _progressRow.Visibility = Visibility.Collapsed;
            if (!token.IsCancellationRequested) await AnimateAsync(item, plan, token);
        }
        catch (OperationCanceledException)
        {
            _output.AppendText(Environment.NewLine + "Анимация остановлена пользователем.");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetRunning(false);
        }
    }

    private static void Execute(MenuItem item, string path, int line, string parameters, StringBuilder log, Plan plan, IProgress<double> progress, CancellationToken ct)
    {
        var mode = item.Mode;
        switch (mode)
        {
            case Mode.Queue:
                Runner.Measure(Runner.ResolveFiles(path), Runner.Queues, log, progress, ct);
                break;
            case Mode.Stack:
                var files = Runner.ResolveFiles(path);
                Runner.Measure(files, Runner.Stack, log, progress, ct);
                try
                {
                    var ops = Runner.Parse(File.ReadAllText(files[0]));
                    var shown = Math.Min(ops.Length, Runner.MaxAnimatedOperations);
                    plan.Scripts.Add(new($"{Path.GetFileName(files[0])}: первые {shown} из {ops.Length} операций", ops[..shown]));
                }
                catch (FormatException) { }
                break;
            case Mode.Postfix:
                log.Append(Expressions.EvaluateFile(path, plan.Scripts, line));
                break;
            case Mode.Infix:
                log.Append(Expressions.ConvertFile(path, plan.Scripts, line));
                break;
            case Mode.List:
                log.Append(ListAlgorithms.Run(item.Algorithm!, path, parameters));
                break;
        }
    }

    private async Task AnimateAsync(MenuItem item, Plan plan, CancellationToken ct)
    {
        var view = _view;
        foreach (var script in plan.Scripts)
        {
            view.Reset();
            view.SetStatus($"Анимация: {script.Title}");
            await view.PauseAsync(1000, ct);
            for (var i = 0; i < script.Ops.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var operation = script.Ops[i];
                var step = $"Шаг {i + 1} из {script.Ops.Count}: ";
                if (operation.Code == 6) await view.NoteAsync(operation.Note ?? "", step, ct);
                else if (StructureView.FromCode(operation.Code) is { } op) await view.ExecuteAsync(op, operation.Argument, operation.Note, step, ct);
            }
            await view.PauseAsync(1200, ct);
        }
    }

    private void SetRunning(bool running)
    {
        _start.IsEnabled = !running;
        _stop.IsEnabled = running;
        _modes.IsEnabled = _pathRow.IsEnabled = !running;
        _progress.Value = 0;
        _progress.IsIndeterminate = running && !UsesFolder;
        _progressRow.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }
}
