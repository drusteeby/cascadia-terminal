// Cascadia Terminal - Windows Terminal scheme -> shell theme preset conversion.

using System.Text;
using RoyalTerminal.Avalonia.App;

namespace CascadiaTerminal.Settings;

/// <summary>
/// Converts Windows Terminal color schemes into the Ghostty-format theme
/// presets consumed by the RoyalTerminal shell.
/// </summary>
public static class WtSchemeConverter
{
    /// <summary>
    /// Builds a stable preset id for a scheme name ("One Half Dark" -> "wt-one-half-dark").
    /// </summary>
    public static string PresetId(string schemeName)
    {
        StringBuilder builder = new("wt-");
        foreach (char c in schemeName.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '-');
        }

        return builder.ToString();
    }

    public static ShellThemePreset ToPreset(WtColorScheme scheme)
    {
        StringBuilder text = new();
        Append(text, "foreground", scheme.Foreground);
        Append(text, "background", scheme.Background);
        Append(text, "cursor", scheme.CursorColor);
        Append(text, "selection-background", scheme.SelectionBackground);
        Append(text, "color0", scheme.Black);
        Append(text, "color1", scheme.Red);
        Append(text, "color2", scheme.Green);
        Append(text, "color3", scheme.Yellow);
        Append(text, "color4", scheme.Blue);
        Append(text, "color5", scheme.Purple);
        Append(text, "color6", scheme.Cyan);
        Append(text, "color7", scheme.White);
        Append(text, "color8", scheme.BrightBlack);
        Append(text, "color9", scheme.BrightRed);
        Append(text, "color10", scheme.BrightGreen);
        Append(text, "color11", scheme.BrightYellow);
        Append(text, "color12", scheme.BrightBlue);
        Append(text, "color13", scheme.BrightPurple);
        Append(text, "color14", scheme.BrightCyan);
        Append(text, "color15", scheme.BrightWhite);

        return new ShellThemePreset(PresetId(scheme.Name), scheme.Name, text.ToString());
    }

    /// <summary>
    /// Converts schemes to presets; later entries win on duplicate names so
    /// user schemes can override built-in ones.
    /// </summary>
    public static IReadOnlyList<ShellThemePreset> ToPresets(IEnumerable<WtColorScheme> schemes)
    {
        Dictionary<string, ShellThemePreset> byId = new(StringComparer.Ordinal);
        List<string> order = [];
        foreach (WtColorScheme scheme in schemes)
        {
            ShellThemePreset preset = ToPreset(scheme);
            if (!byId.ContainsKey(preset.Id))
            {
                order.Add(preset.Id);
            }

            byId[preset.Id] = preset;
        }

        return order.Select(id => byId[id]).ToList();
    }

    private static void Append(StringBuilder builder, string key, string? color)
    {
        if (!string.IsNullOrWhiteSpace(color))
        {
            builder.Append(key).Append(" = ").AppendLine(color.Trim());
        }
    }
}
