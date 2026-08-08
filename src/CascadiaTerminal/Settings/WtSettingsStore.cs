// Cascadia Terminal - settings.json-backed session profile store.
// Makes settings.json the single source of truth: the shell's profile
// catalog loads from it directly, and edits made in the in-app settings
// panel are written back into it.

using System.Text.Json;
using System.Text.Json.Nodes;
using RoyalTerminal.Terminal;

namespace CascadiaTerminal.Settings;

/// <summary>
/// An <see cref="ITerminalSessionProfileStore"/> backed by the Windows
/// Terminal settings.json file.
///
/// Load converts settings.json profiles on the fly. Save round-trips edits
/// back into settings.json: PTY profiles map onto Windows Terminal fields
/// (name, commandline, startingDirectory, colorScheme, fontFace/fontSize,
/// historySize, environment); profiles the WT format cannot express (SSH,
/// telnet, serial, …) are persisted under the "cascadiaExtendedProfiles"
/// extension property so nothing is lost.
/// </summary>
public sealed class WtSettingsStore : ITerminalSessionProfileStore
{
    private const string ExtendedProfilesProperty = "cascadiaExtendedProfiles";

    private static readonly JsonSerializerOptions s_extendedOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly JsonDocumentOptions s_documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _settingsPath;
    private readonly Func<IReadOnlyDictionary<string, string>> _schemeNameByPresetId;
    private readonly object _gate = new();

    public WtSettingsStore(string settingsPath, IEnumerable<WtColorScheme> knownSchemes)
    {
        _settingsPath = settingsPath;
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach (WtColorScheme scheme in knownSchemes)
        {
            map[WtSchemeConverter.PresetId(scheme.Name)] = scheme.Name;
        }

        _schemeNameByPresetId = () => map;
    }

    /// <inheritdoc />
    public ValueTask<TerminalSessionProfilesDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(LoadCore());
        }
    }

    /// <inheritdoc />
    public ValueTask SaveAsync(TerminalSessionProfilesDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (_gate)
        {
            SaveCore(document);
        }

        return ValueTask.CompletedTask;
    }

    private TerminalSessionProfilesDocument LoadCore()
    {
        WtSettingsDocument settings = ReadSettings();
        TerminalSessionProfilesDocument document = WtProfileMapper.ToSessionProfilesDocument(settings);

        foreach (TerminalSessionProfile extended in ReadExtendedProfiles())
        {
            if (!document.Profiles.Any(p => string.Equals(p.Id, extended.Id, StringComparison.Ordinal)))
            {
                document.Profiles.Add(extended);
            }
        }

        return document;
    }

    private void SaveCore(TerminalSessionProfilesDocument document)
    {
        JsonNode root = ReadSettingsNode();
        TerminalSessionProfilesDocument currentMapped = LoadCore();

        JsonObject profilesEnvelope = EnsureProfilesEnvelope(root);
        JsonArray list = profilesEnvelope["list"] as JsonArray ?? [];
        profilesEnvelope["list"] = list;

        List<TerminalSessionProfile> extendedProfiles = [];
        HashSet<string> keptGuids = new(StringComparer.OrdinalIgnoreCase);

        foreach (TerminalSessionProfile profile in document.Profiles)
        {
            bool isPty = string.Equals(profile.Transport.TransportId, TerminalTransportIds.Pty, StringComparison.Ordinal);
            if (!isPty)
            {
                extendedProfiles.Add(profile);
                continue;
            }

            keptGuids.Add(profile.Id);
            TerminalSessionProfile? before = currentMapped.Profiles.FirstOrDefault(p =>
                string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
            JsonObject? entry = FindProfileEntry(list, profile.Id);
            if (entry is null)
            {
                entry = new JsonObject { ["guid"] = profile.Id };
                list.Add(entry);
            }

            WriteProfileFields(entry, profile, before);
        }

        // Remove PTY profiles deleted through the settings surface.
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] is JsonObject candidate &&
                candidate["guid"]?.GetValue<string>() is { } guid &&
                !keptGuids.Contains(guid) &&
                !extendedProfiles.Any(p => string.Equals(p.Id, guid, StringComparison.OrdinalIgnoreCase)))
            {
                list.RemoveAt(i);
            }
        }

        if (!string.IsNullOrWhiteSpace(document.DefaultProfileId))
        {
            root["defaultProfile"] = document.DefaultProfileId;
        }

        if (extendedProfiles.Count > 0)
        {
            root[ExtendedProfilesProperty] = JsonSerializer.SerializeToNode(extendedProfiles, s_extendedOptions);
        }
        else
        {
            (root as JsonObject)?.Remove(ExtendedProfilesProperty);
        }

        // JsonNode rewrites drop comments; preserve at least the header
        // comment block, and keep the full commented original as .bak.
        string header = ReadHeaderComments();
        WriteAtomically(header + root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Reads the leading comment lines of settings.json (before the opening
    /// brace) so rewrites keep the user's file header.
    /// </summary>
    private string ReadHeaderComments()
    {
        try
        {
            List<string> header = [];
            foreach (string line in File.ReadLines(_settingsPath))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.Length == 0)
                {
                    header.Add(line);
                    continue;
                }

                break;
            }

            while (header.Count > 0 && string.IsNullOrWhiteSpace(header[^1]))
            {
                header.RemoveAt(header.Count - 1);
            }

            return header.Count == 0 ? string.Empty : string.Join('\n', header) + '\n';
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Writes WT-expressible fields for a profile, only touching fields whose
    /// effective value changed so load-time fallbacks (like font resolution)
    /// do not churn the user's file.
    /// </summary>
    private void WriteProfileFields(JsonObject entry, TerminalSessionProfile profile, TerminalSessionProfile? before)
    {
        void Set(string key, string? value, bool changed)
        {
            if (!changed)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                entry.Remove(key);
            }
            else
            {
                entry[key] = value;
            }
        }

        bool isNew = before is null;

        Set("name", profile.DisplayName, isNew || profile.DisplayName != before!.DisplayName);

        string commandline = BuildCommandline(profile);
        Set("commandline", commandline, isNew || commandline != BuildCommandline(before!));

        string? workingDirectory = profile.Transport.Pty.WorkingDirectory;
        Set("startingDirectory", workingDirectory, isNew || workingDirectory != before!.Transport.Pty.WorkingDirectory);

        if (isNew || profile.Appearance.ThemePresetId != before!.Appearance.ThemePresetId)
        {
            string? presetId = profile.Appearance.ThemePresetId;
            string? schemeName = presetId is not null && _schemeNameByPresetId().TryGetValue(presetId, out string? name)
                ? name
                : null;
            Set("colorScheme", schemeName, changed: true);
        }

        Set("fontFace", profile.Appearance.FontFamilyName,
            isNew || profile.Appearance.FontFamilyName != before!.Appearance.FontFamilyName);

        if (isNew || Math.Abs(profile.Appearance.FontSize - before!.Appearance.FontSize) > double.Epsilon)
        {
            entry["fontSize"] = profile.Appearance.FontSize;
        }

        if (isNew || profile.Layout.ScrollbackLimit != before!.Layout.ScrollbackLimit)
        {
            entry["historySize"] = profile.Layout.ScrollbackLimit;
        }

        Dictionary<string, string> environment = profile.Transport.Pty.Environment;
        bool environmentChanged = isNew ||
            !DictionariesEqual(environment, before!.Transport.Pty.Environment);
        if (environmentChanged)
        {
            if (environment.Count == 0)
            {
                entry.Remove("environment");
            }
            else
            {
                JsonObject env = [];
                foreach ((string key, string value) in environment)
                {
                    env[key] = value;
                }

                entry["environment"] = env;
            }
        }
    }

    private static bool DictionariesEqual(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
        => a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out string? value) && value == pair.Value);

    private static string BuildCommandline(TerminalSessionProfile profile)
    {
        TerminalSessionPtySettings pty = profile.Transport.Pty;
        if (string.IsNullOrWhiteSpace(pty.ShellPath))
        {
            return string.Empty;
        }

        IEnumerable<string> parts = new[] { pty.ShellPath! }.Concat(pty.Arguments);
        return string.Join(' ', parts.Select(part => part.Contains(' ', StringComparison.Ordinal) ? $"\"{part}\"" : part));
    }

    private static JsonObject EnsureProfilesEnvelope(JsonNode root)
    {
        if (root["profiles"] is JsonObject envelope)
        {
            return envelope;
        }

        // Legacy flat array: rewrap as the modern envelope, keeping entries.
        JsonObject created = [];
        if (root["profiles"] is JsonArray flat)
        {
            JsonArray moved = [];
            foreach (JsonNode? item in flat.ToArray())
            {
                flat.Remove(item!);
                moved.Add(item);
            }

            created["list"] = moved;
        }
        else
        {
            created["list"] = new JsonArray();
        }

        root["profiles"] = created;
        return created;
    }

    private static JsonObject? FindProfileEntry(JsonArray list, string guid)
    {
        foreach (JsonNode? node in list)
        {
            if (node is JsonObject entry &&
                entry["guid"]?.GetValue<string>() is { } entryGuid &&
                string.Equals(entryGuid, guid, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private WtSettingsDocument ReadSettings()
    {
        try
        {
            return WtSettingsParser.Parse(File.ReadAllText(_settingsPath));
        }
        catch
        {
            return WtSettingsParser.Parse(WtSettingsLoader.BuildDefaultSettingsJson());
        }
    }

    private JsonNode ReadSettingsNode()
    {
        try
        {
            using FileStream stream = File.OpenRead(_settingsPath);
            using JsonDocument document = JsonDocument.Parse(stream, s_documentOptions);
            return JsonSerializer.SerializeToNode(document.RootElement) ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private IReadOnlyList<TerminalSessionProfile> ReadExtendedProfiles()
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_settingsPath), s_documentOptions);
            if (document.RootElement.TryGetProperty(ExtendedProfilesProperty, out JsonElement element) &&
                element.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<List<TerminalSessionProfile>>(element.GetRawText(), s_extendedOptions) ?? [];
            }
        }
        catch
        {
            // Extended profiles are best-effort; never block startup.
        }

        return [];
    }

    private void WriteAtomically(string json)
    {
        string directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        string temporary = _settingsPath + ".tmp";
        File.WriteAllText(temporary, json);
        if (File.Exists(_settingsPath))
        {
            File.Replace(temporary, _settingsPath, _settingsPath + ".bak");
        }
        else
        {
            File.Move(temporary, _settingsPath);
        }
    }
}
