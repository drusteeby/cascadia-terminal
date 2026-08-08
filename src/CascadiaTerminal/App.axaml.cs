// Cascadia Terminal - Avalonia application setup: loads Windows Terminal
// settings.json and boots the RoyalTerminal shell with the ported profiles,
// schemes, and defaults.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CascadiaTerminal.Settings;
using RoyalTerminal.Avalonia.App;
using RoyalTerminal.Terminal;

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
            MainWindowShellOptions options = BuildShellOptions();
            MainWindow mainWindow = new(options);
            mainWindow.Title = "Cascadia Terminal";
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static MainWindowShellOptions BuildShellOptions()
    {
        (WtSettingsDocument settings, IReadOnlyList<WtColorScheme> allSchemes) = WtSettingsLoader.LoadOrCreate();

        // Convert WT profiles into the shell's session profile document and
        // persist it to the app's own store so the shell profile catalog,
        // launcher, and new-tab menu reflect settings.json.
        TerminalSessionProfilesDocument profileDocument = WtProfileMapper.ToSessionProfilesDocument(settings);
        string profileStorePath = Path.Combine(WtSettingsLoader.ConfigDirectory, "session-profiles.json");
        JsonFileTerminalSessionProfileStore profileStore = new(profileStorePath);
        profileStore.SaveAsync(profileDocument).AsTask().GetAwaiter().GetResult();

        // Default scheme: the default profile's colorScheme, falling back to
        // Windows Terminal's classic Campbell.
        WtProfile? defaultProfile = settings.EffectiveProfiles().FirstOrDefault(p =>
            settings.DefaultProfile is not null &&
            string.Equals(p.Guid, settings.DefaultProfile, StringComparison.OrdinalIgnoreCase));
        string defaultScheme = defaultProfile?.ColorScheme
            ?? settings.ProfileDefaults?.ColorScheme
            ?? "Campbell";

        return new MainWindowShellOptions
        {
            ShowMacOsTitleBarLogos = false,
            AdditionalThemePresets = WtSchemeConverter.ToPresets(allSchemes),
            DefaultThemePresetId = WtSchemeConverter.PresetId(defaultScheme),
            SessionProfileStore = profileStore,
        };
    }
}
