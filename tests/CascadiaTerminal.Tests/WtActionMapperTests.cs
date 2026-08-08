// Cascadia Terminal - tests for Windows Terminal action/keybinding mapping
// and the settings.json round-trip store.

using CascadiaTerminal.Settings;
using RoyalTerminal.Avalonia.App;
using RoyalTerminal.Terminal;
using Xunit;

namespace CascadiaTerminal.Tests;

public sealed class WtActionMapperTests
{
    [Fact]
    public void ConvertChord_MapsWtKeyNames()
    {
        Assert.Equal("Ctrl+Shift+T", WtActionMapper.ConvertChord("ctrl+shift+t"));
        Assert.Equal("Ctrl+PageDown", WtActionMapper.ConvertChord("ctrl+pgdn"));
        Assert.Equal("Alt+Shift+OemPlus", WtActionMapper.ConvertChord("alt+shift+plus"));
        Assert.Equal("Ctrl+OemComma", WtActionMapper.ConvertChord("ctrl+,"));
        Assert.Equal("Ctrl+D1", WtActionMapper.ConvertChord("ctrl+1"));
        Assert.Equal("F11", WtActionMapper.ConvertChord("f11"));
        Assert.Null(WtActionMapper.ConvertChord("ctrl+badkeyname"));
    }

    [Fact]
    public void BuildKeybindings_DefaultsIncludeCoreWtBindings()
    {
        IReadOnlyList<ShellKeybinding> bindings = WtActionMapper.BuildKeybindings(null);

        Assert.Contains(bindings, b => b.Gesture == "Ctrl+Shift+T" && b.ActionId == ShellKeybindingActions.NewTab);
        Assert.Contains(bindings, b => b.Gesture == "Ctrl+Shift+C" && b.ActionId == ShellKeybindingActions.Copy);
        Assert.Contains(bindings, b => b.Gesture == "Ctrl+Shift+V" && b.ActionId == ShellKeybindingActions.Paste);
        Assert.Contains(bindings, b => b.Gesture == "Ctrl+Shift+F" && b.ActionId == ShellKeybindingActions.Find);
        Assert.Contains(bindings, b => b.Gesture == "Ctrl+Shift+W" && b.ActionId == ShellKeybindingActions.ClosePane);
        Assert.Contains(bindings, b => b.Gesture == "Ctrl+Shift+D" && b.ActionId == ShellKeybindingActions.DuplicateTab);
        // WT defaults: Alt+Shift+- / Alt+Shift+plus are (duplicate) splits.
        Assert.Contains(bindings, b => b.Gesture == "Alt+Shift+OemMinus" && b.ActionId == ShellKeybindingActions.SplitDown);
        Assert.Contains(bindings, b => b.Gesture == "Alt+Shift+OemPlus" && b.ActionId == ShellKeybindingActions.SplitRight);
        // Cascadia extras fill chords WT leaves unbound.
        Assert.Contains(bindings, b => b.Gesture == "Alt+Shift+D" && b.ActionId == ShellKeybindingActions.SplitAuto);
        Assert.Contains(bindings, b => b.Gesture == "Ctrl+Shift+X" && b.ActionId == ShellKeybindingActions.TogglePaneZoom);
        Assert.Contains(bindings, b => b.ActionId == ShellKeybindingActions.SwitchToTab && b.Argument == "0");
    }

    [Fact]
    public void BuildKeybindings_UserOverridesReplaceDefaults()
    {
        const string user = """
            {
                "actions": [
                    { "command": { "action": "splitPane", "split": "down" }, "keys": "ctrl+shift+o" },
                    { "command": "unbound", "keys": "ctrl+shift+w" }
                ]
            }
            """;

        IReadOnlyList<ShellKeybinding> bindings = WtActionMapper.BuildKeybindings(user);

        Assert.Contains(bindings, b => b.Gesture == "Ctrl+Shift+O" && b.ActionId == ShellKeybindingActions.SplitDown);
        Assert.DoesNotContain(bindings, b => b.Gesture == "Ctrl+Shift+W");
    }

    [Fact]
    public void BuildKeybindings_ModernActionsPlusKeybindingsSplit()
    {
        const string user = """
            {
                "actions": [ { "command": { "action": "moveFocus", "direction": "left" }, "id": "User.FocusLeft" } ],
                "keybindings": [ { "id": "User.FocusLeft", "keys": "ctrl+alt+h" } ]
            }
            """;

        IReadOnlyList<ShellKeybinding> bindings = WtActionMapper.BuildKeybindings(user);

        Assert.Contains(bindings, b => b.Gesture == "Ctrl+Alt+H" && b.ActionId == ShellKeybindingActions.MoveFocusLeft);
    }

    [Fact]
    public void BuildKeybindings_MalformedUserJsonKeepsDefaults()
    {
        IReadOnlyList<ShellKeybinding> bindings = WtActionMapper.BuildKeybindings("{ not json !!");

        Assert.Contains(bindings, b => b.Gesture == "Ctrl+Shift+T");
    }
}

public sealed class WtSettingsStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _settingsPath;

    public WtSettingsStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "ct-store-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _settingsPath = Path.Combine(_directory, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private WtSettingsStore CreateStore() => new(_settingsPath, WtSettingsLoader.BuiltInSchemes());

    [Fact]
    public async Task Load_ConvertsSettingsJsonProfiles()
    {
        File.WriteAllText(_settingsPath, """
            {
                "defaultProfile": "{g1}",
                "profiles": { "list": [
                    { "guid": "{g1}", "name": "bash", "commandline": "/bin/bash", "colorScheme": "Campbell" }
                ] }
            }
            """);

        TerminalSessionProfilesDocument document = await CreateStore().LoadAsync();

        TerminalSessionProfile profile = Assert.Single(document.Profiles);
        Assert.Equal("{g1}", profile.Id);
        Assert.Equal("wt-campbell", profile.Appearance.ThemePresetId);
    }

    [Fact]
    public async Task Save_RoundTripsEditsIntoSettingsJson()
    {
        File.WriteAllText(_settingsPath, """
            {
                "defaultProfile": "{g1}",
                "keepMe": true,
                "profiles": { "list": [
                    { "guid": "{g1}", "name": "bash", "commandline": "/bin/bash", "customField": 42 }
                ] }
            }
            """);

        WtSettingsStore store = CreateStore();
        TerminalSessionProfilesDocument document = await store.LoadAsync();
        TerminalSessionProfile edited = document.Profiles[0] with
        {
            DisplayName = "renamed-bash",
            Layout = document.Profiles[0].Layout with { ScrollbackLimit = 4321 },
        };
        document.Profiles[0] = edited;

        await store.SaveAsync(document);

        string json = File.ReadAllText(_settingsPath);
        WtSettingsDocument reparsed = WtSettingsParser.Parse(json);
        Assert.Equal("renamed-bash", reparsed.Profiles[0].Name);
        Assert.Equal(4321, reparsed.Profiles[0].HistorySize);
        // Unknown fields survive the round trip.
        Assert.Contains("\"keepMe\"", json);
        Assert.Contains("\"customField\"", json);

        TerminalSessionProfilesDocument reloaded = await store.LoadAsync();
        Assert.Equal("renamed-bash", reloaded.Profiles[0].DisplayName);
        Assert.Equal(4321, reloaded.Profiles[0].Layout.ScrollbackLimit);
    }

    [Fact]
    public async Task Save_PersistsNonPtyProfilesAsExtendedProfiles()
    {
        File.WriteAllText(_settingsPath, """
            { "profiles": { "list": [ { "guid": "{g1}", "name": "bash", "commandline": "/bin/bash" } ] } }
            """);

        WtSettingsStore store = CreateStore();
        TerminalSessionProfilesDocument document = await store.LoadAsync();
        document.Profiles.Add(new TerminalSessionProfile
        {
            Id = "ssh-1",
            DisplayName = "prod box",
            Transport = new TerminalSessionTransportProfile
            {
                TransportId = TerminalTransportIds.Ssh,
                Ssh = new TerminalSessionSshSettings { Host = "prod.example.com", Port = 2222 },
            },
        });

        await store.SaveAsync(document);
        TerminalSessionProfilesDocument reloaded = await store.LoadAsync();

        Assert.Equal(2, reloaded.Profiles.Count);
        TerminalSessionProfile ssh = reloaded.Profiles.First(p => p.Id == "ssh-1");
        Assert.Equal("prod box", ssh.DisplayName);
        Assert.Equal("prod.example.com", ssh.Transport.Ssh.Host);
        Assert.Equal(2222, ssh.Transport.Ssh.Port);
    }

    [Fact]
    public async Task Save_RemovesDeletedPtyProfiles()
    {
        File.WriteAllText(_settingsPath, """
            { "profiles": { "list": [
                { "guid": "{g1}", "name": "bash", "commandline": "/bin/bash" },
                { "guid": "{g2}", "name": "zsh", "commandline": "/usr/bin/zsh" }
            ] } }
            """);

        WtSettingsStore store = CreateStore();
        TerminalSessionProfilesDocument document = await store.LoadAsync();
        document.Profiles.RemoveAll(p => p.Id == "{g2}");

        await store.SaveAsync(document);
        TerminalSessionProfilesDocument reloaded = await store.LoadAsync();

        TerminalSessionProfile remaining = Assert.Single(reloaded.Profiles);
        Assert.Equal("{g1}", remaining.Id);
    }
}
