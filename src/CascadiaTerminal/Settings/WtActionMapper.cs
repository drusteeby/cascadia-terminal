// Cascadia Terminal - Maps Windows Terminal actions/keybindings onto shell
// keybindings.

using System.Reflection;
using System.Text.Json;
using RoyalTerminal.Avalonia.App;

namespace CascadiaTerminal.Settings;

/// <summary>
/// Parses Windows Terminal "actions" and "keybindings" arrays (from the
/// ported defaults and from the user's settings.json) and converts the
/// supported subset into shell keybindings. User entries override defaults
/// per key chord; a null/"unbound" command removes the chord.
/// </summary>
public static class WtActionMapper
{
    private static readonly JsonDocumentOptions s_documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly (string Keys, string ActionId)[] s_extraDefaults =
    [
        ("alt+shift+d", ShellKeybindingActions.SplitAuto),
        ("ctrl+shift+x", ShellKeybindingActions.TogglePaneZoom),
        ("ctrl+pgup", ShellKeybindingActions.PrevTab),
        ("ctrl+pgdn", ShellKeybindingActions.NextTab),
    ];

    /// <summary>
    /// Builds the effective keybindings: Windows Terminal defaults (ported
    /// from microsoft/terminal) overlaid with the user's settings.json
    /// actions/keybindings.
    /// </summary>
    public static IReadOnlyList<ShellKeybinding> BuildKeybindings(string? userSettingsJson)
    {
        // chord -> (actionId, argument); later writers win.
        Dictionary<string, (string ActionId, string? Argument)?> byChord = new(StringComparer.OrdinalIgnoreCase);

        void Overlay(string json)
        {
            foreach ((string keys, (string, string?)? action) in ParseDocument(json))
            {
                byChord[keys] = action;
            }
        }

        Overlay(ReadDefaultActionsAsset());
        if (!string.IsNullOrWhiteSpace(userSettingsJson))
        {
            try
            {
                Overlay(userSettingsJson);
            }
            catch (JsonException)
            {
                // Malformed user settings never break the default bindings.
            }
        }

        // Cascadia extras: chords Windows Terminal leaves unbound but this
        // port documents. Never override a WT default or a user binding
        // (including explicit unbinds).
        foreach ((string keys, string actionId) in s_extraDefaults)
        {
            if (!byChord.ContainsKey(keys))
            {
                byChord[keys] = (actionId, null);
            }
        }

        List<ShellKeybinding> result = [];
        foreach ((string keys, (string ActionId, string? Argument)? action) in byChord)
        {
            if (action is null)
            {
                continue; // explicitly unbound
            }

            if (ConvertChord(keys) is { } gesture)
            {
                result.Add(new ShellKeybinding(gesture, action.Value.ActionId, action.Value.Argument));
            }
        }

        return result;
    }

    /// <summary>
    /// Parses one settings document's actions/keybindings into
    /// chord -> action entries. Null action means "unbind this chord".
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, (string, string?)?>> ParseDocument(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, s_documentOptions);
        JsonElement root = document.RootElement;

        // id -> command element (for the modern actions+keybindings split).
        Dictionary<string, JsonElement> commandById = new(StringComparer.OrdinalIgnoreCase);
        List<KeyValuePair<string, (string, string?)?>> entries = [];

        if (TryGet(root, "actions", out JsonElement actions) && actions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in actions.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                bool hasCommand = TryGet(entry, "command", out JsonElement command);
                if (TryGet(entry, "id", out JsonElement idElement) &&
                    idElement.ValueKind == JsonValueKind.String &&
                    hasCommand)
                {
                    commandById[idElement.GetString()!] = command.Clone();
                }

                // Legacy inline form: { "command": ..., "keys": "..." }.
                if (TryGet(entry, "keys", out JsonElement inlineKeys) &&
                    inlineKeys.ValueKind == JsonValueKind.String)
                {
                    entries.Add(new(inlineKeys.GetString()!, hasCommand ? ConvertCommand(command) : null));
                }
            }
        }

        if (TryGet(root, "keybindings", out JsonElement keybindings) && keybindings.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in keybindings.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !TryGet(entry, "keys", out JsonElement keysElement) ||
                    keysElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string keys = keysElement.GetString()!;
                if (TryGet(entry, "id", out JsonElement idElement) &&
                    idElement.ValueKind == JsonValueKind.String &&
                    commandById.TryGetValue(idElement.GetString()!, out JsonElement command))
                {
                    entries.Add(new(keys, ConvertCommand(command)));
                }
                else if (TryGet(entry, "command", out JsonElement inlineCommand))
                {
                    entries.Add(new(keys, ConvertCommand(inlineCommand)));
                }
                else
                {
                    entries.Add(new(keys, null)); // unbound or unknown id
                }
            }
        }

        return entries;
    }

    /// <summary>
    /// Converts a Windows Terminal command (string or object) to a shell
    /// action id + argument. Unsupported actions map to null (skipped).
    /// </summary>
    public static (string ActionId, string? Argument)? ConvertCommand(JsonElement command)
    {
        string? action;
        JsonElement args = default;
        bool hasArgs = false;

        switch (command.ValueKind)
        {
            case JsonValueKind.String:
                action = command.GetString();
                break;
            case JsonValueKind.Object:
                action = TryGet(command, "action", out JsonElement actionElement) &&
                         actionElement.ValueKind == JsonValueKind.String
                    ? actionElement.GetString()
                    : null;
                args = command;
                hasArgs = true;
                break;
            case JsonValueKind.Null:
                return null; // "command": null == unbound
            default:
                return null;
        }

        if (string.Equals(action, "unbound", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return action switch
        {
            "newTab" => (ShellKeybindingActions.NewTab, null),
            "newWindow" => (ShellKeybindingActions.NewWindow, null),
            "closeTab" => (ShellKeybindingActions.CloseTab, null),
            "closePane" => (ShellKeybindingActions.ClosePane, null),
            "duplicateTab" => (ShellKeybindingActions.DuplicateTab, null),
            "nextTab" => (ShellKeybindingActions.NextTab, null),
            "prevTab" => (ShellKeybindingActions.PrevTab, null),
            "switchToTab" => ConvertSwitchToTab(hasArgs ? args : default),
            "splitPane" => ConvertSplitPane(hasArgs ? args : default),
            "moveFocus" => ConvertDirection(hasArgs ? args : default,
                ShellKeybindingActions.MoveFocusLeft,
                ShellKeybindingActions.MoveFocusRight,
                ShellKeybindingActions.MoveFocusUp,
                ShellKeybindingActions.MoveFocusDown),
            "resizePane" => ConvertDirection(hasArgs ? args : default,
                ShellKeybindingActions.ResizePaneLeft,
                ShellKeybindingActions.ResizePaneRight,
                ShellKeybindingActions.ResizePaneUp,
                ShellKeybindingActions.ResizePaneDown),
            "togglePaneZoom" => (ShellKeybindingActions.TogglePaneZoom, null),
            "copy" => (ShellKeybindingActions.Copy, null),
            "paste" => (ShellKeybindingActions.Paste, null),
            "selectAll" => (ShellKeybindingActions.SelectAll, null),
            "find" => (ShellKeybindingActions.Find, null),
            "adjustFontSize" => ConvertAdjustFontSize(hasArgs ? args : default),
            "resetFontSize" => (ShellKeybindingActions.ResetFontSize, null),
            _ => null,
        };
    }

    private static (string, string?)? ConvertSwitchToTab(JsonElement args)
    {
        if (args.ValueKind == JsonValueKind.Object &&
            TryGet(args, "index", out JsonElement index) &&
            index.ValueKind == JsonValueKind.Number &&
            index.TryGetInt32(out int value) &&
            value >= 0)
        {
            return (ShellKeybindingActions.SwitchToTab, value.ToString());
        }

        return null;
    }

    private static (string, string?)? ConvertSplitPane(JsonElement args)
    {
        string split = args.ValueKind == JsonValueKind.Object &&
            TryGet(args, "split", out JsonElement splitElement) &&
            splitElement.ValueKind == JsonValueKind.String
                ? splitElement.GetString() ?? "auto"
                : "auto";

        return split.ToLowerInvariant() switch
        {
            // Windows Terminal: "vertical"/"right" split side-by-side,
            // "horizontal"/"down" split stacked.
            "right" or "vertical" => (ShellKeybindingActions.SplitRight, null),
            "down" or "horizontal" => (ShellKeybindingActions.SplitDown, null),
            _ => (ShellKeybindingActions.SplitAuto, null),
        };
    }

    private static (string, string?)? ConvertDirection(
        JsonElement args,
        string left,
        string right,
        string up,
        string down)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !TryGet(args, "direction", out JsonElement direction) ||
            direction.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return direction.GetString()!.ToLowerInvariant() switch
        {
            "left" => (left, null),
            "right" => (right, null),
            "up" => (up, null),
            "down" => (down, null),
            _ => null,
        };
    }

    private static (string, string?)? ConvertAdjustFontSize(JsonElement args)
    {
        double delta = args.ValueKind == JsonValueKind.Object &&
            TryGet(args, "delta", out JsonElement deltaElement) &&
            deltaElement.ValueKind == JsonValueKind.Number
                ? deltaElement.GetDouble()
                : 1;

        return delta >= 0
            ? (ShellKeybindingActions.IncreaseFontSize, null)
            : (ShellKeybindingActions.DecreaseFontSize, null);
    }

    /// <summary>
    /// Converts a Windows Terminal key chord ("ctrl+shift+t", "alt+f4",
    /// "ctrl+pgdn", "ctrl+plus") to an Avalonia gesture string, or null when
    /// a key has no Avalonia equivalent.
    /// </summary>
    public static string? ConvertChord(string chord)
    {
        if (string.IsNullOrWhiteSpace(chord))
        {
            return null;
        }

        string[] parts = chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        List<string> modifiers = [];
        string? key = null;
        foreach (string part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers.Add("Ctrl");
                    break;
                case "shift":
                    modifiers.Add("Shift");
                    break;
                case "alt":
                    modifiers.Add("Alt");
                    break;
                case "win":
                case "cmd":
                    modifiers.Add("Meta");
                    break;
                default:
                    if (key is not null)
                    {
                        return null; // two non-modifier keys
                    }

                    key = ConvertKeyName(part);
                    break;
            }
        }

        if (key is null)
        {
            return null;
        }

        modifiers.Add(key);
        return string.Join('+', modifiers);
    }

    private static string? ConvertKeyName(string name) => name.ToLowerInvariant() switch
    {
        "plus" or "=" => "OemPlus",
        "minus" or "-" => "OemMinus",
        "," or "comma" => "OemComma",
        "." or "period" => "OemPeriod",
        ";" => "OemSemicolon",
        "'" => "OemQuotes",
        "/" => "OemQuestion",
        "\\" => "OemBackslash",
        "`" => "OemTilde",
        "[" => "OemOpenBrackets",
        "]" => "OemCloseBrackets",
        "space" => "Space",
        "tab" => "Tab",
        "enter" or "return" => "Enter",
        "esc" or "escape" => "Escape",
        "backspace" => "Back",
        "delete" or "del" => "Delete",
        "insert" or "ins" => "Insert",
        "home" => "Home",
        "end" => "End",
        "pgup" or "pageup" => "PageUp",
        "pgdn" or "pagedown" => "PageDown",
        "up" => "Up",
        "down" => "Down",
        "left" => "Left",
        "right" => "Right",
        "0" or "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9" => "D" + name,
        _ when name.Length == 1 && char.IsAsciiLetter(name[0]) => name.ToUpperInvariant(),
        _ when name.Length is 2 or 3 && (name[0] is 'f' or 'F') &&
               int.TryParse(name[1..], out int fn) && fn is >= 1 and <= 24 => "F" + fn,
        _ => null,
    };

    private static string ReadDefaultActionsAsset()
    {
        using Stream stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("CascadiaTerminal.Assets.wt-default-actions.json")
            ?? throw new InvalidOperationException("Embedded wt-default-actions.json asset is missing.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
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
}
