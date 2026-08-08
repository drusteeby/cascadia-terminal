// Cascadia Terminal - Avalonia application setup.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RoyalTerminal.Avalonia.App;

namespace CascadiaTerminal;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow mainWindow = new(new MainWindowShellOptions
            {
                ShowMacOsTitleBarLogos = false,
            });
            mainWindow.Title = "Cascadia Terminal";
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
