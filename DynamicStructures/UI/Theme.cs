using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using SkiaSharp;

namespace DynamicStructures.UI;

/// <summary>Тёмная зелёная палитра в духе Gemini и общие фабрики элементов.</summary>
public static class Theme
{
    public static readonly SolidColorBrush Background = Brush("#0D1411");
    public static readonly SolidColorBrush Surface = Brush("#141F1A");
    public static readonly SolidColorBrush SurfaceHigh = Brush("#1C2B24");
    public static readonly SolidColorBrush Stroke = Brush("#273A31");
    public static readonly SolidColorBrush Accent = Brush("#34D399");
    public static readonly SolidColorBrush AccentHover = Brush("#6EE7B7");
    public static readonly SolidColorBrush Text = Brush("#E2F3EA");
    public static readonly SolidColorBrush Muted = Brush("#8AA597");
    public static readonly SolidColorBrush OnAccent = Brush("#05271B");

    public static readonly SKColor ChartAccent = SKColor.Parse("#34D399");
    public static readonly SKColor ChartLime = SKColor.Parse("#BEF264");
    public static readonly SKColor ChartText = SKColor.Parse("#8AA597");
    public static readonly SKColor ChartGrid = SKColor.Parse("#22332B");
    public static readonly SKColor ChartBackground = SKColor.Parse("#0D1411");

    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static void Apply(Application app)
    {
#pragma warning disable WPF0001 // Fluent-тема WPF помечена как экспериментальная
        app.ThemeMode = ThemeMode.Dark;
#pragma warning restore WPF0001

        var accent = Accent.Color;
        foreach (var key in new[]
                 {
                     "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
                     "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush", "AccentTextFillColorTertiaryBrush",
                     "AccentFillColorSelectedTextBackgroundBrush", "SystemFillColorAttentionBrush",
                     "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed",
                     "TextControlSelectionHighlightColor", "ListBoxItemSelectedBackgroundThemeBrush",
                 })
            app.Resources[key] = new SolidColorBrush(accent);
        app.Resources[SystemColors.AccentColorKey] = accent;

        LiveCharts.Configure(config => config.AddDarkTheme());
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public static Border Card(UIElement child, Thickness margin) => new()
    {
        Child = child,
        Margin = margin,
        Padding = new Thickness(14, 10, 14, 10),
        Background = Surface,
        BorderBrush = Stroke,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(22),
    };

    /// <summary>Кнопка-«таблетка» со своим шаблоном: акцентная (зелёная) или тональная.</summary>
    public static Button Pill(string text, bool accent)
    {
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(20));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        template.Triggers.Add(new Trigger
        {
            Property = UIElement.IsMouseOverProperty, Value = true,
            Setters = { new Setter(Border.BackgroundProperty, accent ? AccentHover : Stroke, "Bd") },
        });
        template.Triggers.Add(new Trigger
        {
            Property = UIElement.IsEnabledProperty, Value = false,
            Setters = { new Setter(UIElement.OpacityProperty, 0.35, "Bd") },
        });

        return new Button
        {
            Content = text,
            Template = template,
            Background = accent ? Accent : SurfaceHigh,
            Foreground = accent ? OnAccent : Text,
            FontWeight = accent ? FontWeights.SemiBold : FontWeights.Normal,
            Padding = new Thickness(18, 8, 18, 8),
            Margin = new Thickness(8, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            FocusVisualStyle = null,
        };
    }
}
