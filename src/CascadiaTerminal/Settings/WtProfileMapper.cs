// Cascadia Terminal - Maps Windows Terminal profiles onto RoyalTerminal
// session profiles.

using RoyalTerminal.Terminal;

namespace CascadiaTerminal.Settings;

/// <summary>
/// Converts a parsed Windows Terminal settings document into the session
/// profile document consumed by the RoyalTerminal shell.
/// </summary>
public static class WtProfileMapper
{
    public static TerminalSessionProfilesDocument ToSessionProfilesDocument(WtSettingsDocument settings)
    {
        List<TerminalSessionProfile> profiles = [];
        string? defaultProfileId = null;

        foreach (WtProfile profile in settings.EffectiveProfiles())
        {
            TerminalSessionProfile mapped = MapProfile(profile, settings);
            profiles.Add(mapped);
            if (!string.IsNullOrWhiteSpace(settings.DefaultProfile) &&
                string.Equals(profile.Guid, settings.DefaultProfile, StringComparison.OrdinalIgnoreCase))
            {
                defaultProfileId = mapped.Id;
            }
        }

        if (profiles.Count == 0)
        {
            profiles.Add(MapProfile(FallbackShellProfile(), settings));
        }

        return new TerminalSessionProfilesDocument
        {
            DefaultProfileId = defaultProfileId ?? profiles[0].Id,
            Profiles = profiles,
        };
    }

    /// <summary>
    /// Builds a profile for the user's login shell when settings.json has no
    /// usable profiles.
    /// </summary>
    public static WtProfile FallbackShellProfile()
    {
        string shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
        return new WtProfile
        {
            Guid = "{default-shell}",
            Name = Path.GetFileName(shell),
            Commandline = shell,
            StartingDirectory = "~",
        };
    }

    private static TerminalSessionProfile MapProfile(WtProfile profile, WtSettingsDocument settings)
    {
        (string? shellPath, List<string> arguments) = SplitCommandline(profile.Commandline);
        Dictionary<string, string> environment = new(StringComparer.Ordinal);
        if (profile.Environment is not null)
        {
            foreach ((string key, string value) in profile.Environment)
            {
                environment[key] = value;
            }
        }

        return new TerminalSessionProfile
        {
            Id = ProfileId(profile),
            DisplayName = profile.Name ?? "Profile",
            Layout = new TerminalSessionLayoutSettings
            {
                Columns = settings.InitialCols,
                Rows = settings.InitialRows,
                ScrollbackLimit = Math.Max(profile.HistorySize ?? 9001, 100),
            },
            Appearance = new TerminalSessionAppearanceSettings
            {
                FontFamilyName = ResolveFontFace(profile.FontFace),
                FontSize = profile.FontSize ?? 12.0,
            },
            Behavior = new TerminalSessionBehaviorSettings
            {
                CopyOnSelectEnabled = settings.CopyOnSelect,
            },
            Transport = new TerminalSessionTransportProfile
            {
                TransportId = TerminalTransportIds.Pty,
                Pty = new TerminalSessionPtySettings
                {
                    ShellPath = shellPath,
                    Arguments = arguments,
                    WorkingDirectory = ExpandStartingDirectory(profile.StartingDirectory),
                    Environment = environment,
                },
            },
        };
    }

    /// <summary>
    /// Windows Terminal identifies profiles by GUID; fall back to a slug of
    /// the name for profiles without one.
    /// </summary>
    public static string ProfileId(WtProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.Guid))
        {
            return profile.Guid.Trim();
        }

        return "wt-profile-" + (profile.Name ?? "unnamed").Trim().ToLowerInvariant().Replace(' ', '-');
    }

    /// <summary>
    /// Splits a commandline into executable and arguments, honoring double
    /// quotes (matching Windows Terminal's commandline handling closely
    /// enough for POSIX shells).
    /// </summary>
    public static (string? ShellPath, List<string> Arguments) SplitCommandline(string? commandline)
    {
        if (string.IsNullOrWhiteSpace(commandline))
        {
            string shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
            return (shell, []);
        }

        List<string> tokens = [];
        System.Text.StringBuilder current = new();
        bool inQuotes = false;
        foreach (char c in commandline.Trim())
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        if (tokens.Count == 0)
        {
            string shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
            return (shell, []);
        }

        return (tokens[0], tokens.Skip(1).ToList());
    }

    /// <summary>
    /// Expands "~", "$HOME", and the Windows "%USERPROFILE%" placeholder to
    /// the user's home directory.
    /// </summary>
    public static string? ExpandStartingDirectory(string? startingDirectory)
    {
        if (string.IsNullOrWhiteSpace(startingDirectory))
        {
            return null;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string value = startingDirectory.Trim();
        if (value is "~" || value.Equals("%USERPROFILE%", StringComparison.OrdinalIgnoreCase) ||
            value is "$HOME")
        {
            return home;
        }

        if (value.StartsWith("~/", StringComparison.Ordinal))
        {
            return Path.Combine(home, value[2..]);
        }

        if (value.StartsWith("$HOME/", StringComparison.Ordinal))
        {
            return Path.Combine(home, value["$HOME/".Length..]);
        }

        if (value.StartsWith("%USERPROFILE%", StringComparison.OrdinalIgnoreCase))
        {
            string remainder = value["%USERPROFILE%".Length..].TrimStart('\\', '/');
            return remainder.Length == 0 ? home : Path.Combine(home, remainder.Replace('\\', '/'));
        }

        return value;
    }

    /// <summary>
    /// Maps Windows-only font names shipped in ported settings to fonts that
    /// exist on typical Linux systems, preferring Cascadia when installed.
    /// </summary>
    public static string ResolveFontFace(string? fontFace)
    {
        string requested = string.IsNullOrWhiteSpace(fontFace) ? "Cascadia Mono" : fontFace.Trim();
        if (InstalledFontNames.Value.Contains(requested))
        {
            return requested;
        }

        foreach (string candidate in (string[])["Cascadia Mono", "Cascadia Code", "JetBrains Mono", "Fira Code", "Ubuntu Mono", "DejaVu Sans Mono", "Noto Sans Mono"])
        {
            if (InstalledFontNames.Value.Contains(candidate))
            {
                return candidate;
            }
        }

        return "monospace";
    }

    private static readonly Lazy<HashSet<string>> InstalledFontNames = new(() =>
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (global::Avalonia.Media.FontFamily family in global::Avalonia.Media.FontManager.Current.SystemFonts)
            {
                names.Add(family.Name);
            }
        }
        catch
        {
            // Font enumeration is best-effort; fall back to defaults.
        }

        return names;
    });
}
