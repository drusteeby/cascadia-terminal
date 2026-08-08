// Cascadia Terminal — unofficial Linux port of the Windows Terminal experience.
// App shell provided by the RoyalTerminal fork; Windows Terminal settings,
// schemes, and keybinding surface ported from microsoft/terminal.

using Avalonia;
using ReactiveUI.Avalonia;

namespace CascadiaTerminal;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI(_ => { })
            .WithInterFont()
            .LogToTrace();
}
