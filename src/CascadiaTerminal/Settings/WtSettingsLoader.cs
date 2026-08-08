// Cascadia Terminal - settings.json discovery, first-run generation, and
// merge with the color schemes ported from microsoft/terminal.

using System.Reflection;
using System.Text;

namespace CascadiaTerminal.Settings;

/// <summary>
/// Loads the user's Windows Terminal-format settings.json, generating a
/// Linux-appropriate default on first run, and merges user schemes over the
/// built-in schemes ported from microsoft/terminal.
/// </summary>
public static class WtSettingsLoader
{
    public static string ConfigDirectory
    {
        get
        {
            string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            string baseDir = string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                : xdg;
            return Path.Combine(baseDir, "cascadia-terminal");
        }
    }

    public static string SettingsPath => Path.Combine(ConfigDirectory, "settings.json");

    /// <summary>
    /// Loads settings.json, writing the default document first when missing.
    /// Returns the parsed settings plus all schemes (built-ins ported from
    /// Windows Terminal, then user schemes, user wins by name).
    /// </summary>
    public static (WtSettingsDocument Settings, IReadOnlyList<WtColorScheme> AllSchemes) LoadOrCreate()
    {
        Directory.CreateDirectory(ConfigDirectory);
        if (!File.Exists(SettingsPath))
        {
            File.WriteAllText(SettingsPath, BuildDefaultSettingsJson());
        }

        WtSettingsDocument settings;
        try
        {
            settings = WtSettingsParser.Parse(File.ReadAllText(SettingsPath));
        }
        catch (Exception)
        {
            // A malformed settings.json must never prevent startup
            // (Windows Terminal behaves the same way).
            settings = WtSettingsParser.Parse(BuildDefaultSettingsJson());
        }

        List<WtColorScheme> schemes = [.. BuiltInSchemes()];
        Dictionary<string, int> indexByName = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < schemes.Count; i++)
        {
            indexByName[schemes[i].Name] = i;
        }

        foreach (WtColorScheme scheme in settings.Schemes)
        {
            if (indexByName.TryGetValue(scheme.Name, out int existing))
            {
                schemes[existing] = scheme;
            }
            else
            {
                indexByName[scheme.Name] = schemes.Count;
                schemes.Add(scheme);
            }
        }

        return (settings, schemes);
    }

    /// <summary>
    /// Gets the color schemes ported from microsoft/terminal defaults.json.
    /// </summary>
    public static IReadOnlyList<WtColorScheme> BuiltInSchemes()
    {
        using Stream stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("CascadiaTerminal.Assets.wt-schemes.json")
            ?? throw new InvalidOperationException("Embedded wt-schemes.json asset is missing.");
        using StreamReader reader = new(stream);
        return WtSettingsParser.ParseSchemesArray(reader.ReadToEnd());
    }

    /// <summary>
    /// Builds the first-run settings.json: the Windows Terminal document
    /// shape with profiles for the shells installed on this machine.
    /// </summary>
    public static string BuildDefaultSettingsJson()
    {
        string loginShell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
        List<(string Guid, string Name, string Commandline)> shells = [];

        void AddShell(string guid, string name, string path)
        {
            if (File.Exists(path) && !shells.Any(s => s.Commandline == path))
            {
                shells.Add((guid, name, path));
            }
        }

        AddShell("{07b52e3e-de2c-5db4-bd2d-ba144ed6c273}", Path.GetFileName(loginShell), loginShell);
        AddShell("{2c4de342-38b7-51cf-b940-2309a097f518}", "bash", "/bin/bash");
        AddShell("{46ca431a-3a87-5fb3-83cd-11ececc031d2}", "zsh", "/usr/bin/zsh");
        AddShell("{a5a97cb8-8961-5535-816d-772efe0c6a3f}", "fish", "/usr/bin/fish");
        if (shells.Count == 0)
        {
            shells.Add(("{2c4de342-38b7-51cf-b940-2309a097f518}", "sh", "/bin/sh"));
        }

        StringBuilder json = new();
        json.AppendLine("// Cascadia Terminal settings — the Windows Terminal settings.json format.");
        json.AppendLine("// Documentation: https://aka.ms/terminal-documentation");
        json.AppendLine("{");
        json.AppendLine($"    \"defaultProfile\": \"{shells[0].Guid}\",");
        json.AppendLine("    \"copyOnSelect\": false,");
        json.AppendLine("    \"initialCols\": 120,");
        json.AppendLine("    \"initialRows\": 30,");
        json.AppendLine("    \"theme\": \"dark\",");
        json.AppendLine("    \"profiles\":");
        json.AppendLine("    {");
        json.AppendLine("        \"defaults\":");
        json.AppendLine("        {");
        json.AppendLine("            \"colorScheme\": \"Campbell\",");
        json.AppendLine("            \"fontFace\": \"Cascadia Mono\",");
        json.AppendLine("            \"fontSize\": 12,");
        json.AppendLine("            \"historySize\": 9001,");
        json.AppendLine("            \"startingDirectory\": \"~\"");
        json.AppendLine("        },");
        json.AppendLine("        \"list\":");
        json.AppendLine("        [");
        for (int i = 0; i < shells.Count; i++)
        {
            (string guid, string name, string commandline) = shells[i];
            json.AppendLine("            {");
            json.AppendLine($"                \"guid\": \"{guid}\",");
            json.AppendLine($"                \"name\": \"{name}\",");
            json.AppendLine($"                \"commandline\": \"{commandline}\"");
            json.Append("            }").AppendLine(i < shells.Count - 1 ? "," : string.Empty);
        }

        json.AppendLine("        ]");
        json.AppendLine("    },");
        json.AppendLine("    \"schemes\": []");
        json.AppendLine("}");
        return json.ToString();
    }
}
