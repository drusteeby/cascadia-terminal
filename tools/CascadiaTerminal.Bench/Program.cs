// Cascadia Terminal - throughput/startup benchmark harness.
// Boots the real app headlessly with a live PTY and measures:
//  - startup: process start -> first rendered frame with a live shell
//  - throughput: shell-reported wall time for VT workloads, where the
//    terminal's PTY read rate gates the shell via flow control (the same
//    method works inside any terminal, enabling apples-to-apples numbers).

using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CascadiaTerminal;
using ReactiveUI.Avalonia;
using RoyalTerminal.Avalonia.App;

Stopwatch startup = Stopwatch.StartNew();
string resultPath = args.Length > 0 ? args[0] : "bench-results.txt";

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
window.Show();

// Wait for a live shell: type a probe command and wait for its side effect.
List<string> results = [];
Pump(seconds: 2.0);
File.Delete("/tmp/ct-bench-ready.txt");
SendText("touch /tmp/ct-bench-ready.txt\n");
Stopwatch readyGuard = Stopwatch.StartNew();
while (!File.Exists("/tmp/ct-bench-ready.txt") && readyGuard.Elapsed < TimeSpan.FromSeconds(30))
{
    Pump(seconds: 0.2);
    if (readyGuard.Elapsed > TimeSpan.FromSeconds(8) && readyGuard.Elapsed.TotalSeconds % 4 < 0.3)
    {
        SendText("touch /tmp/ct-bench-ready.txt\n"); // retry if the first chars raced the shell
    }
}

startup.Stop();
results.Add($"startup_to_live_shell_ms={startup.ElapsedMilliseconds}");
results.Add($"shell_ready={File.Exists("/tmp/ct-bench-ready.txt")}");
File.WriteAllLines(resultPath, results);

// Workloads run inside the shell; their wall time is written to a marker
// file by the shell itself so terminal read-rate gates the measurement.
RunWorkload("seq_1M_plain", "TIMEFORMAT=%R; { time seq 1 1000000; } 2> /tmp/ct-bench-time.txt");
RunWorkload("colored_escape_flood", "TIMEFORMAT=%R; { time bash -c 'for i in $(seq 1 3000); do printf \"\\e[3%dm%s\\e[0m\\n\" $((i % 8)) \"Lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor $i ================================================\"; done'; } 2> /tmp/ct-bench-time.txt");
RunWorkload("cat_10MB_binaryish", "head -c 10000000 /dev/urandom | base64 > /tmp/ct-bench-data.txt; TIMEFORMAT=%R; { time cat /tmp/ct-bench-data.txt; } 2> /tmp/ct-bench-time.txt; rm -f /tmp/ct-bench-data.txt");

File.WriteAllLines(resultPath, results);
Console.WriteLine(string.Join('\n', results));
return 0;

void RunWorkload(string name, string command)
{
    File.Delete("/tmp/ct-bench-time.txt");
    SendText(command + "\n");

    // Wait until the shell writes the timing file (workload complete).
    Stopwatch guard = Stopwatch.StartNew();
    while (guard.Elapsed < TimeSpan.FromSeconds(120))
    {
        Pump(seconds: 0.25);
        try
        {
            if (File.Exists("/tmp/ct-bench-time.txt"))
            {
                string text = File.ReadAllText("/tmp/ct-bench-time.txt").Trim();
                if (text.Length > 0)
                {
                    Pump(seconds: 0.3); // let rendering settle
                    results.Add($"{name}_seconds={text.Replace(',', '.')}");
                    File.WriteAllLines(resultPath, results);
                    return;
                }
            }
        }
        catch (IOException)
        {
        }
    }

    results.Add($"{name}_seconds=TIMEOUT");
    File.WriteAllLines(resultPath, results);
}

void Pump(double seconds)
{
    DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
    while (DateTime.UtcNow < deadline)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(15);
    }

    Dispatcher.UIThread.RunJobs();
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
