// Cascadia Terminal - headless screenshot harness.
// Boots the real app (styles, settings engine, live PTY sessions) on the
// Avalonia headless platform with Skia rendering and captures PNG frames of
// scripted scenarios: startup shell, splits, zoom, tabs.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Avalonia.Threading;
using CascadiaTerminal;
using ReactiveUI.Avalonia;
using RoyalTerminal.Avalonia.App;

string outputDirectory = args.Length > 0 ? args[0] : "shots";
Directory.CreateDirectory(outputDirectory);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHarfBuzz()
    .UseReactiveUI(_ => { })
    .UseHeadless(new AvaloniaHeadlessPlatformOptions
    {
        UseHeadlessDrawing = false,
    })
    .SetupWithoutStarting();

MainWindow window = new(App.BuildShellOptions())
{
    Width = 1280,
    Height = 760,
};
window.Title = "Cascadia Terminal";
window.Show();

Pump(seconds: 3.0); // settle: shell profile load, PTY spawn, first prompt
Capture("01-startup");

// Type a command into the shell so real output is visible.
SendText("echo Cascadia Terminal on $(uname -o) — visit https://github.com/microsoft/terminal\n");
Pump(seconds: 1.5);
Capture("02-shell-output");

Run(() => window.ViewModel!.SplitPaneRightCommand.Execute().Subscribe());
Pump(seconds: 1.5);
Run(() => window.ViewModel!.SplitPaneDownCommand.Execute().Subscribe());
Pump(seconds: 2.0);
Capture("03-three-panes");

Run(() => window.ViewModel!.TogglePaneZoomCommand.Execute().Subscribe());
Pump(seconds: 1.0);
Capture("04-pane-zoomed");

Run(() => window.ViewModel!.TogglePaneZoomCommand.Execute().Subscribe());
Pump(seconds: 0.5);

Run(() => window.ViewModel!.NewTabCommand.Execute().Subscribe());
Pump(seconds: 2.0);
Run(() => window.ViewModel!.DuplicateTabCommand.Execute().Subscribe());
Pump(seconds: 2.0);
Capture("05-three-tabs");

Run(() => window.ViewModel!.ToggleSearchPanelCommand.Execute().Subscribe());
Pump(seconds: 1.0);
Capture("06-search-panel");

Console.WriteLine($"Screenshots written to {Path.GetFullPath(outputDirectory)}");
return 0;

void Run(Action action) => Dispatcher.UIThread.Invoke(action);

void Pump(double seconds)
{
    DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
    while (DateTime.UtcNow < deadline)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(25);
    }

    Dispatcher.UIThread.RunJobs();
}

void Capture(string name)
{
    Dispatcher.UIThread.Invoke(() =>
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("No frame captured.");
        string path = Path.Combine(outputDirectory, name + ".png");
        frame.Save(path);
        Console.WriteLine($"captured {path}");
    });
}

void SendText(string text)
{
    Dispatcher.UIThread.Invoke(() =>
    {
        foreach (char c in text)
        {
            if (c == '\n')
            {
                window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
                window.KeyReleaseQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
            }
            else
            {
                window.KeyTextInput(c.ToString());
            }
        }
    });
}
