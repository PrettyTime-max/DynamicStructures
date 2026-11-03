using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using SkiaSharp;

namespace DynamicStructures.UI;

/// <summary>Светлая зелёная палитра в духе Gemini и общие фабрики элементов.</summary>
public static class Theme
{
    public static readonly SolidColorBrush Background = Brush("#F4F9F6");
    public static readonly SolidColorBrush Surface = Brush("#FFFFFF");
    public static readonly SolidColorBrush SurfaceHigh = Brush("#E8F3ED");
    public static readonly SolidColorBrush Stroke = Brush("#D5E7DD");
    public static readonly SolidColorBrush Accent = Brush("#10B981");
    public static readonly SolidColorBrush AccentHover = Brush("#059669");
    public static readonly SolidColorBrush Text = Brush("#16352A");
    public static readonly SolidColorBrush Muted = Brush("#5E7C6D");
    public static readonly SolidColorBrush OnAccent = Brush("#FFFFFF");

    public static readonly SKColor ChartAccent = SKColor.Parse("#059669");
    public static readonly SKColor ChartLime = SKColor.Parse("#84CC16");
    public static readonly SKColor ChartText = SKColor.Parse("#5E7C6D");
    public static readonly SKColor ChartGrid = SKColor.Parse("#E1EDE6");
    public static readonly SKColor ChartBackground = SKColor.Parse("#FFFFFF");

    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static void Apply(Application app)
    {
#pragma warning disable WPF0001 // Fluent-тема WPF помечена как экспериментальная
        app.ThemeMode = ThemeMode.Light;
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

        LiveCharts.Configure(config => config.AddLightTheme());
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
