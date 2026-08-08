// Cascadia Terminal - JSONC-tolerant parser for Windows Terminal settings.json.

using System.Text.Json;

namespace CascadiaTerminal.Settings;

/// <summary>
/// Parses Windows Terminal settings.json documents. Accepts JSONC (comments
/// and trailing commas), case-insensitive keys, and both the modern
/// ("profiles": { "defaults", "list" }) and legacy ("profiles": [...]) shapes.
/// </summary>
public static class WtSettingsParser
{
    private static readonly JsonDocumentOptions s_documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static WtSettingsDocument Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, s_documentOptions);
        JsonElement root = document.RootElement;

        WtProfile? profileDefaults = null;
        List<WtProfile> profiles = [];
        if (TryGetProperty(root, "profiles", out JsonElement profilesElement))
        {
            if (profilesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in profilesElement.EnumerateArray())
                {
                    profiles.Add(ParseProfile(entry));
                }
            }
            else if (profilesElement.ValueKind == JsonValueKind.Object)
            {
                if (TryGetProperty(profilesElement, "defaults", out JsonElement defaultsElement) &&
                    defaultsElement.ValueKind == JsonValueKind.Object)
                {
                    profileDefaults = ParseProfile(defaultsElement);
                }

                if (TryGetProperty(profilesElement, "list", out JsonElement listElement) &&
                    listElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement entry in listElement.EnumerateArray())
                    {
                        profiles.Add(ParseProfile(entry));
                    }
                }
            }
        }

        List<WtColorScheme> schemes = [];
        if (TryGetProperty(root, "schemes", out JsonElement schemesElement) &&
            schemesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in schemesElement.EnumerateArray())
            {
                WtColorScheme scheme = ParseScheme(entry);
                if (!string.IsNullOrWhiteSpace(scheme.Name))
                {
                    schemes.Add(scheme);
                }
            }
        }

        return new WtSettingsDocument
        {
            DefaultProfile = GetString(root, "defaultProfile"),
            CopyOnSelect = GetBool(root, "copyOnSelect") ?? false,
            InitialCols = GetInt(root, "initialCols") ?? 120,
            InitialRows = GetInt(root, "initialRows") ?? 30,
            Theme = GetString(root, "theme"),
            ProfileDefaults = profileDefaults,
            Profiles = profiles,
            Schemes = schemes,
        };
    }

    public static IReadOnlyList<WtColorScheme> ParseSchemesArray(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, s_documentOptions);
        List<WtColorScheme> schemes = [];
        foreach (JsonElement entry in document.RootElement.EnumerateArray())
        {
            WtColorScheme scheme = ParseScheme(entry);
            if (!string.IsNullOrWhiteSpace(scheme.Name))
            {
                schemes.Add(scheme);
            }
        }

        return schemes;
    }

    private static WtProfile ParseProfile(JsonElement element)
    {
        Dictionary<string, string>? environment = null;
        if (TryGetProperty(element, "environment", out JsonElement envElement) &&
            envElement.ValueKind == JsonValueKind.Object)
        {
            environment = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty property in envElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    environment[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        }

        return new WtProfile
        {
            Guid = GetString(element, "guid"),
            Name = GetString(element, "name"),
            Commandline = GetString(element, "commandline"),
            StartingDirectory = GetString(element, "startingDirectory"),
            ColorScheme = GetString(element, "colorScheme"),
            FontFace = GetString(element, "fontFace") ?? GetNestedFontFace(element),
            FontSize = GetDouble(element, "fontSize") ?? GetNestedFontSize(element),
            Hidden = GetBool(element, "hidden"),
            HistorySize = GetInt(element, "historySize"),
            TabTitle = GetString(element, "tabTitle"),
            Environment = environment,
        };
    }

    private static string? GetNestedFontFace(JsonElement element)
        => TryGetProperty(element, "font", out JsonElement font) && font.ValueKind == JsonValueKind.Object
            ? GetString(font, "face")
            : null;

    private static double? GetNestedFontSize(JsonElement element)
        => TryGetProperty(element, "font", out JsonElement font) && font.ValueKind == JsonValueKind.Object
            ? GetDouble(font, "size")
            : null;

    private static WtColorScheme ParseScheme(JsonElement element) => new()
    {
        Name = GetString(element, "name") ?? string.Empty,
        Foreground = GetString(element, "foreground"),
        Background = GetString(element, "background"),
        CursorColor = GetString(element, "cursorColor"),
        SelectionBackground = GetString(element, "selectionBackground"),
        Black = GetString(element, "black"),
        Red = GetString(element, "red"),
        Green = GetString(element, "green"),
        Yellow = GetString(element, "yellow"),
        Blue = GetString(element, "blue"),
        Purple = GetString(element, "purple"),
        Cyan = GetString(element, "cyan"),
        White = GetString(element, "white"),
        BrightBlack = GetString(element, "brightBlack"),
        BrightRed = GetString(element, "brightRed"),
        BrightGreen = GetString(element, "brightGreen"),
        BrightYellow = GetString(element, "brightYellow"),
        BrightBlue = GetString(element, "brightBlue"),
        BrightPurple = GetString(element, "brightPurple"),
        BrightCyan = GetString(element, "brightCyan"),
        BrightWhite = GetString(element, "brightWhite"),
    };

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, string name)
        => TryGetProperty(element, name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? GetBool(JsonElement element, string name)
        => TryGetProperty(element, name, out JsonElement value) &&
           value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int? GetInt(JsonElement element, string name)
        => TryGetProperty(element, name, out JsonElement value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out int parsed)
            ? parsed
            : null;

    private static double? GetDouble(JsonElement element, string name)
        => TryGetProperty(element, name, out JsonElement value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetDouble(out double parsed)
            ? parsed
            : null;
}
