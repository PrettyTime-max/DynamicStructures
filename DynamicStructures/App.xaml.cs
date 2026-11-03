using System.Windows;
using DynamicStructures.UI;

namespace DynamicStructures;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Theme.Apply(this);
        base.OnStartup(e);
    }
}
