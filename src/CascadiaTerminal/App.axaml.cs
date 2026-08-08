// Cascadia Terminal - Avalonia application setup: loads Windows Terminal
// settings.json and boots the RoyalTerminal shell with the ported profiles,
// schemes, keybindings, and defaults. settings.json is the single source of
// truth: profile edits round-trip back into it, and external edits
// hot-reload into every open window.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CascadiaTerminal.Settings;
using RoyalTerminal.Avalonia.App;

namespace CascadiaTerminal;

public class App : Application
{
    private static readonly List<WeakReference<MainWindow>> s_windows = [];
    private static FileSystemWatcher? s_settingsWatcher;
    private static DispatcherTimer? s_reloadDebounce;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateShellWindow();
            StartSettingsWatcher();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Creates a fully wired shell window; used for the first window and by
    /// the "new window" action.
    /// </summary>
    internal static MainWindow CreateShellWindow(string? initialProfileId = null, string? initialWorkingDirectory = null)
    {
        MainWindowShellOptions options = BuildShellOptions();
        options.NewWindowFactory = () => CreateShellWindow().Show();
        options.MoveTabToNewWindowFactory = (profileId, workingDirectory) =>
            CreateShellWindow(profileId, workingDirectory).Show();
        options.OpenSettingsAction = OpenSettingsFileInEditor;
        options.InitialProfileId = initialProfileId;
        options.InitialWorkingDirectory = initialWorkingDirectory;

        MainWindow window = new(options);
        window.Title = "Cascadia Terminal";
        s_windows.Add(new WeakReference<MainWindow>(window));
        return window;
    }

    /// <summary>
    /// Opens settings.json in the desktop's default editor (Ctrl+, /
    /// "openSettings"), creating it first when missing.
    /// </summary>
    private static void OpenSettingsFileInEditor()
    {
        try
        {
            if (!File.Exists(WtSettingsLoader.SettingsPath))
            {
                WtSettingsLoader.LoadOrCreate();
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xdg-open",
                ArgumentList = { WtSettingsLoader.SettingsPath },
                UseShellExecute = false,
            });
        }
        catch (Exception)
        {
            // Opening an editor is best-effort.
        }
    }

    internal static MainWindowShellOptions BuildShellOptions()
    {
        (WtSettingsDocument settings, IReadOnlyList<WtColorScheme> allSchemes) = WtSettingsLoader.LoadOrCreate();

        // settings.json-backed store: loads convert on the fly, saves from
        // the in-app settings panel round-trip back into settings.json.
        WtSettingsStore profileStore = new(WtSettingsLoader.SettingsPath, allSchemes);

        // Default scheme: the default profile's colorScheme, falling back to
        // Windows Terminal's classic Campbell.
        WtProfile? defaultProfile = settings.EffectiveProfiles().FirstOrDefault(p =>
            settings.DefaultProfile is not null &&
            string.Equals(p.Guid, settings.DefaultProfile, StringComparison.OrdinalIgnoreCase));
        string defaultScheme = defaultProfile?.ColorScheme
            ?? settings.ProfileDefaults?.ColorScheme
            ?? "Campbell";

        string? userSettingsJson = null;
        try
        {
            userSettingsJson = File.ReadAllText(WtSettingsLoader.SettingsPath);
        }
        catch (IOException)
        {
            // Keybindings fall back to the ported Windows Terminal defaults.
        }

        return new MainWindowShellOptions
        {
            ShowMacOsTitleBarLogos = false,
            AdditionalThemePresets = WtSchemeConverter.ToPresets(allSchemes),
            DefaultThemePresetId = WtSchemeConverter.PresetId(defaultScheme),
            SessionProfileStore = profileStore,
            Keybindings = WtActionMapper.BuildKeybindings(userSettingsJson),
            ConfirmCloseAllTabs = true,
            // Terminal-first chrome: the tool rail stays one click away
            // (View > Show Left Panel) but does not compete with the prompt.
            ShowLeftPanelOnStartup = false,
        };
    }

    /// <summary>
    /// Watches settings.json and re-applies profiles and keybindings to all
    /// open windows when it changes (Windows Terminal-style hot reload).
    /// </summary>
    private static void StartSettingsWatcher()
    {
        try
        {
            s_settingsWatcher = new FileSystemWatcher(WtSettingsLoader.ConfigDirectory)
            {
                Filter = "settings.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };

            s_reloadDebounce = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400),
            };
            s_reloadDebounce.Tick += (_, _) =>
            {
                s_reloadDebounce!.Stop();
                ReloadSettingsIntoOpenWindows();
            };

            FileSystemEventHandler onChanged = (_, _) => Dispatcher.UIThread.Post(() =>
            {
                s_reloadDebounce!.Stop();
                s_reloadDebounce.Start();
            });
            s_settingsWatcher.Changed += onChanged;
            s_settingsWatcher.Created += onChanged;
            s_settingsWatcher.Renamed += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                s_reloadDebounce!.Stop();
                s_reloadDebounce.Start();
            });
            s_settingsWatcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            // Hot reload is best-effort; the app works without the watcher.
        }
    }

    private static void ReloadSettingsIntoOpenWindows()
    {
        string? userSettingsJson = null;
        try
        {
            userSettingsJson = File.ReadAllText(WtSettingsLoader.SettingsPath);
        }
        catch (IOException)
        {
            return; // mid-write; the debounce will fire again on next change
        }

        IReadOnlyList<ShellKeybinding> keybindings = WtActionMapper.BuildKeybindings(userSettingsJson);

        // Rebuild theme presets so scheme edits re-theme running terminals.
        (WtSettingsDocument settings, IReadOnlyList<WtColorScheme> allSchemes) = WtSettingsLoader.LoadOrCreate();
        WtProfile? defaultProfile = settings.EffectiveProfiles().FirstOrDefault(p =>
            settings.DefaultProfile is not null &&
            string.Equals(p.Guid, settings.DefaultProfile, StringComparison.OrdinalIgnoreCase));
        string defaultScheme = defaultProfile?.ColorScheme
            ?? settings.ProfileDefaults?.ColorScheme
            ?? "Campbell";
        IReadOnlyList<ShellThemePreset> presets = WtSchemeConverter.ToPresets(allSchemes);
        string defaultPresetId = WtSchemeConverter.PresetId(defaultScheme);

        s_windows.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (WeakReference<MainWindow> reference in s_windows)
        {
            if (!reference.TryGetTarget(out MainWindow? window))
            {
                continue;
            }

            window.ApplyKeybindings(keybindings);
            window.UpdateThemePresets(presets, defaultPresetId);
            // Refresh the profile catalog from the settings.json-backed store.
            window.ViewModel?.RefreshSessionLauncherCommand.Execute().Subscribe();
        }
    }
}
