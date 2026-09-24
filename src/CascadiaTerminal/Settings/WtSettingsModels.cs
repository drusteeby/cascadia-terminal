// Cascadia Terminal - Windows Terminal settings.json object model.
// Field names and semantics ported from microsoft/terminal
// (src/cascadia/TerminalSettingsModel).

namespace CascadiaTerminal.Settings;

/// <summary>
/// A Windows Terminal color scheme (settings.json "schemes" entry).
/// </summary>
public sealed record WtColorScheme
{
    public string Name { get; init; } = string.Empty;
    public string? Foreground { get; init; }
    public string? Background { get; init; }
    public string? CursorColor { get; init; }
    public string? SelectionBackground { get; init; }
    public string? Black { get; init; }
    public string? Red { get; init; }
    public string? Green { get; init; }
    public string? Yellow { get; init; }
    public string? Blue { get; init; }
    public string? Purple { get; init; }
    public string? Cyan { get; init; }
    public string? White { get; init; }
    public string? BrightBlack { get; init; }
    public string? BrightRed { get; init; }
    public string? BrightGreen { get; init; }
    public string? BrightYellow { get; init; }
    public string? BrightBlue { get; init; }
    public string? BrightPurple { get; init; }
    public string? BrightCyan { get; init; }
    public string? BrightWhite { get; init; }
}

/// <summary>
/// A Windows Terminal profile (settings.json "profiles.list" entry).
/// </summary>
public sealed record WtProfile
{
    public string? Guid { get; init; }
    public string? Name { get; init; }
    public string? Commandline { get; init; }
    public string? StartingDirectory { get; init; }
    public string? ColorScheme { get; init; }
    public string? FontFace { get; init; }
    public double? FontSize { get; init; }
    public bool? Hidden { get; init; }
    public int? HistorySize { get; init; }
    public string? TabTitle { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    /// <summary>
    /// Overlays non-null values of this profile over <paramref name="defaults"/>.
    /// </summary>
    public WtProfile MergedOver(WtProfile? defaults)
    {
        if (defaults is null)
        {
            return this;
        }

        return new WtProfile
        {
            Guid = Guid ?? defaults.Guid,
            Name = Name ?? defaults.Name,
            Commandline = Commandline ?? defaults.Commandline,
            StartingDirectory = StartingDirectory ?? defaults.StartingDirectory,
            ColorScheme = ColorScheme ?? defaults.ColorScheme,
            FontFace = FontFace ?? defaults.FontFace,
            FontSize = FontSize ?? defaults.FontSize,
            Hidden = Hidden ?? defaults.Hidden,
            HistorySize = HistorySize ?? defaults.HistorySize,
            TabTitle = TabTitle ?? defaults.TabTitle,
            Environment = Environment ?? defaults.Environment,
        };
    }
}

/// <summary>
/// Root Windows Terminal settings document (settings.json).
/// </summary>
public sealed record WtSettingsDocument
{
    public string? DefaultProfile { get; init; }
    public bool CopyOnSelect { get; init; }

    /// <summary>
    /// Cascadia extension: "contextMenu" (default), "app" (only the running
    /// application / multiplexer sees right-clicks), or "none".
    /// </summary>
    public string? RightClick { get; init; }

    public int InitialCols { get; init; } = 120;
    public int InitialRows { get; init; } = 30;
    public string? Theme { get; init; }
    public WtProfile? ProfileDefaults { get; init; }
    public IReadOnlyList<WtProfile> Profiles { get; init; } = [];
    public IReadOnlyList<WtColorScheme> Schemes { get; init; } = [];

    /// <summary>
    /// Gets profiles with the profile defaults applied, hidden entries removed.
    /// </summary>
    public IReadOnlyList<WtProfile> EffectiveProfiles()
    {
        List<WtProfile> result = [];
        foreach (WtProfile profile in Profiles)
        {
            WtProfile merged = profile.MergedOver(ProfileDefaults);
            if (merged.Hidden == true)
            {
                continue;
            }

            result.Add(merged);
        }

        return result;
    }
}
