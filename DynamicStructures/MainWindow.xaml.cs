using System.Windows;
using DynamicStructures.UI;

namespace DynamicStructures;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Background = Theme.Background;
        Content = new MainView();
    }
}
