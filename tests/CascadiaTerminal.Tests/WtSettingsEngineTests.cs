// Cascadia Terminal - tests for the Windows Terminal settings engine.

using CascadiaTerminal.Settings;
using RoyalTerminal.Terminal;
using Xunit;

namespace CascadiaTerminal.Tests;

public sealed class WtSettingsParserTests
{
    [Fact]
    public void Parse_AcceptsJsoncWithCommentsAndTrailingCommas()
    {
        const string json = """
            // top comment
            {
                "defaultProfile": "{guid-1}", // trailing comment
                "copyOnSelect": true,
                /* block comment */
                "profiles": {
                    "defaults": { "fontSize": 14, },
                    "list": [
                        { "guid": "{guid-1}", "name": "bash", "commandline": "/bin/bash", },
                    ],
                },
                "schemes": [],
            }
            """;

        WtSettingsDocument settings = WtSettingsParser.Parse(json);

        Assert.Equal("{guid-1}", settings.DefaultProfile);
        Assert.True(settings.CopyOnSelect);
        Assert.Equal(14, settings.ProfileDefaults?.FontSize);
        Assert.Single(settings.Profiles);
        Assert.Equal("/bin/bash", settings.Profiles[0].Commandline);
    }

    [Fact]
    public void Parse_AcceptsLegacyFlatProfilesArray()
    {
        const string json = """
            { "profiles": [ { "name": "legacy", "commandline": "/bin/sh" } ] }
            """;

        WtSettingsDocument settings = WtSettingsParser.Parse(json);

        Assert.Single(settings.Profiles);
        Assert.Equal("legacy", settings.Profiles[0].Name);
    }

    [Fact]
    public void Parse_ReadsNestedFontObject()
    {
        const string json = """
            { "profiles": { "list": [ { "name": "p", "font": { "face": "Fira Code", "size": 13 } } ] } }
            """;

        WtSettingsDocument settings = WtSettingsParser.Parse(json);

        Assert.Equal("Fira Code", settings.Profiles[0].FontFace);
        Assert.Equal(13, settings.Profiles[0].FontSize);
    }

    [Fact]
    public void EffectiveProfiles_AppliesDefaultsAndSkipsHidden()
    {
        const string json = """
            {
                "profiles": {
                    "defaults": { "colorScheme": "Campbell", "fontSize": 11 },
                    "list": [
                        { "name": "a", "commandline": "/bin/bash", "fontSize": 13 },
                        { "name": "b", "commandline": "/bin/zsh", "hidden": true }
                    ]
                }
            }
            """;

        WtSettingsDocument settings = WtSettingsParser.Parse(json);
        IReadOnlyList<WtProfile> profiles = settings.EffectiveProfiles();

        WtProfile only = Assert.Single(profiles);
        Assert.Equal("a", only.Name);
        Assert.Equal("Campbell", only.ColorScheme);
        Assert.Equal(13, only.FontSize);
    }

    [Fact]
    public void BuiltInSchemes_IncludePortedWindowsTerminalSchemes()
    {
        IReadOnlyList<WtColorScheme> schemes = WtSettingsLoader.BuiltInSchemes();

        Assert.Contains(schemes, s => s.Name == "Campbell");
        Assert.Contains(schemes, s => s.Name == "One Half Dark");
        Assert.Contains(schemes, s => s.Name == "Solarized Light");
        Assert.Contains(schemes, s => s.Name == "Tango Dark");
        WtColorScheme campbell = schemes.First(s => s.Name == "Campbell");
        Assert.Equal("#0C0C0C", campbell.Background);
        Assert.Equal("#CCCCCC", campbell.Foreground);
    }

    [Fact]
    public void BuildDefaultSettingsJson_ParsesAndTargetsAnInstalledShell()
    {
        WtSettingsDocument settings = WtSettingsParser.Parse(WtSettingsLoader.BuildDefaultSettingsJson());

        Assert.NotEmpty(settings.Profiles);
        Assert.NotNull(settings.DefaultProfile);
        Assert.Equal("Campbell", settings.ProfileDefaults?.ColorScheme);
        string? commandline = settings.EffectiveProfiles()[0].Commandline;
        Assert.False(string.IsNullOrWhiteSpace(commandline));
        Assert.True(File.Exists(commandline!.Split(' ')[0]));
    }
}

public sealed class WtSchemeConverterTests
{
    [Fact]
    public void PresetId_SlugsSchemeNames()
    {
        Assert.Equal("wt-one-half-dark", WtSchemeConverter.PresetId("One Half Dark"));
        Assert.Equal("wt-campbell-powershell", WtSchemeConverter.PresetId("Campbell Powershell"));
    }

    [Fact]
    public void ToPreset_EmitsGhosttyThemeText()
    {
        WtColorScheme scheme = new()
        {
            Name = "Test",
            Foreground = "#CCCCCC",
            Background = "#0C0C0C",
            CursorColor = "#FFFFFF",
            SelectionBackground = "#264F78",
            Black = "#000000",
            BrightWhite = "#F2F2F2",
        };

        var preset = WtSchemeConverter.ToPreset(scheme);

        Assert.Contains("foreground = #CCCCCC", preset.ThemeText);
        Assert.Contains("background = #0C0C0C", preset.ThemeText);
        Assert.Contains("cursor = #FFFFFF", preset.ThemeText);
        Assert.Contains("selection-background = #264F78", preset.ThemeText);
        Assert.Contains("color0 = #000000", preset.ThemeText);
        Assert.Contains("color15 = #F2F2F2", preset.ThemeText);
    }

    [Fact]
    public void ToPresets_LaterSchemesOverrideByName()
    {
        WtColorScheme original = new() { Name = "Campbell", Background = "#0C0C0C" };
        WtColorScheme overridden = new() { Name = "Campbell", Background = "#111111" };

        var presets = WtSchemeConverter.ToPresets([original, overridden]);

        var preset = Assert.Single(presets);
        Assert.Contains("background = #111111", preset.ThemeText);
    }
}

public sealed class WtProfileMapperTests
{
    [Fact]
    public void SplitCommandline_HonorsQuotes()
    {
        (string? shell, List<string> args) = WtProfileMapper.SplitCommandline(
            "\"/usr/local/bin/my shell\" -l --rc \"a b.rc\"");

        Assert.Equal("/usr/local/bin/my shell", shell);
        Assert.Equal(["-l", "--rc", "a b.rc"], args);
    }

    [Fact]
    public void SplitCommandline_EmptyFallsBackToLoginShell()
    {
        (string? shell, List<string> args) = WtProfileMapper.SplitCommandline(null);

        Assert.False(string.IsNullOrWhiteSpace(shell));
        Assert.Empty(args);
    }

    [Fact]
    public void ExpandStartingDirectory_HandlesTildeHomeAndUserProfile()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(home, WtProfileMapper.ExpandStartingDirectory("~"));
        Assert.Equal(home, WtProfileMapper.ExpandStartingDirectory("%USERPROFILE%"));
        Assert.Equal(Path.Combine(home, "src"), WtProfileMapper.ExpandStartingDirectory("~/src"));
        Assert.Equal(Path.Combine(home, "src"), WtProfileMapper.ExpandStartingDirectory("$HOME/src"));
        Assert.Null(WtProfileMapper.ExpandStartingDirectory(null));
        Assert.Equal("/opt/work", WtProfileMapper.ExpandStartingDirectory("/opt/work"));
    }

    [Fact]
    public void ToSessionProfilesDocument_MapsProfilesAndDefault()
    {
        WtSettingsDocument settings = WtSettingsParser.Parse("""
            {
                "defaultProfile": "{guid-2}",
                "copyOnSelect": true,
                "initialCols": 100,
                "initialRows": 28,
                "profiles": {
                    "list": [
                        { "guid": "{guid-1}", "name": "bash", "commandline": "/bin/bash -l", "historySize": 5000 },
                        { "guid": "{guid-2}", "name": "zsh", "commandline": "/usr/bin/zsh", "startingDirectory": "~" }
                    ]
                }
            }
            """);

        TerminalSessionProfilesDocument document = WtProfileMapper.ToSessionProfilesDocument(settings);

        Assert.Equal(2, document.Profiles.Count);
        Assert.Equal("{guid-2}", document.DefaultProfileId);

        TerminalSessionProfile bash = document.Profiles[0];
        Assert.Equal("/bin/bash", bash.Transport.Pty.ShellPath);
        Assert.Equal(["-l"], bash.Transport.Pty.Arguments);
        Assert.Equal(5000, bash.Layout.ScrollbackLimit);
        Assert.Equal(100, bash.Layout.Columns);
        Assert.Equal(28, bash.Layout.Rows);
        Assert.True(bash.Behavior.CopyOnSelectEnabled);

        TerminalSessionProfile zsh = document.Profiles[1];
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            zsh.Transport.Pty.WorkingDirectory);
    }

    [Fact]
    public void ToSessionProfilesDocument_EmptySettings_FallsBackToLoginShell()
    {
        WtSettingsDocument settings = WtSettingsParser.Parse("{}");

        TerminalSessionProfilesDocument document = WtProfileMapper.ToSessionProfilesDocument(settings);

        TerminalSessionProfile profile = Assert.Single(document.Profiles);
        Assert.False(string.IsNullOrWhiteSpace(profile.Transport.Pty.ShellPath));
        Assert.Equal(document.DefaultProfileId, profile.Id);
    }
}
