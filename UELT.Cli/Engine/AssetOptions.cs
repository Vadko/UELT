using System.Text.RegularExpressions;
using UAssetAPI.UnrealTypes;
using UELT.Cli.Infra;
using UELT.Core.locres;

namespace UELT.Cli.Engine;

/// <summary>How game files are opened. Replaces the GUI settings (CV2) and dialogs
/// (EngineVersionWindow, LanguageSelectWindow) with explicit command-line options.</summary>
public sealed class AssetOptions
{
    public bool Cv2 { get; init; }
    public CV2KeySet Cv2KeySet { get; init; } = CV2KeySet.Release;
    public EngineVersion? Engine { get; init; }
    public string MappingsPath { get; init; }
    public MappingType MappingType { get; init; } = MappingType.None;
    public string Language { get; init; }
    public bool Status { get; init; }

    public static readonly Opt[] Specs =
    {
        new("--cv2"), new("--cv2-demo"),
        new("--engine", true), new("--mappings", true),
        new("--lang", true), new("--status"),
    };

    /// <summary>Only the options that matter when opening a file read-only (no --status).</summary>
    public static readonly Opt[] OpenSpecs = Specs.Where(s => s.Name != "--status").ToArray();

    public static AssetOptions From(ParsedArgs a)
    {
        string mappings = a.Get("--mappings");
        EngineVersion? engine = EngineVersionParser.Parse(a.Get("--engine"));

        if (!string.IsNullOrEmpty(mappings))
        {
            if (!File.Exists(mappings))
                throw new UsageException($"Файл mappings не знайдено: {mappings}");
            if (!engine.HasValue)
                throw new UsageException("--mappings потребує явної версії рушія: --engine (не auto). Без версії UELT ігнорує mappings.");
        }

        bool demo = a.Has("--cv2-demo");
        return new AssetOptions
        {
            Cv2 = a.Has("--cv2") || demo,
            Cv2KeySet = demo ? CV2KeySet.Demo : CV2KeySet.Release,
            Engine = engine,
            MappingsPath = string.IsNullOrEmpty(mappings) ? null : mappings,
            MappingType = MappingTypeFromPath(mappings),
            Language = a.Get("--lang"),
            Status = a.Has("--status"),
        };
    }

    /// <summary>Same rule as EngineVersionWindow (cosmetic: every mappings file is loaded with new Usmap(path)).</summary>
    public static MappingType MappingTypeFromPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return MappingType.None;
        if (path.EndsWith(".jmap.gz", StringComparison.OrdinalIgnoreCase)) return MappingType.JmapGz;
        if (path.EndsWith(".jmap", StringComparison.OrdinalIgnoreCase)) return MappingType.Jmap;
        if (path.EndsWith(".usmap", StringComparison.OrdinalIgnoreCase)) return MappingType.Usmap;
        return MappingType.Unknown;
    }
}

public static partial class EngineVersionParser
{
    /// <summary>
    /// Accepts EngineVersion names (VER_UE4_27, VER_UE5_3), the GUI combo-box tags (0..27, 500EA, 500..508),
    /// short forms (4.27, 5.3, UE4.27, UE5.0EA) and "auto" (null = auto-detect).
    /// </summary>
    public static EngineVersion? Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return null;

        s = s.Trim();

        if (s.StartsWith("VER_", StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<EngineVersion>(s, ignoreCase: true, out var named)
            && IsOffered(named))
            return named;

        if (Tags.TryGetValue(s, out var tagged))
            return tagged;

        var m = ShortForm().Match(s);
        if (m.Success)
        {
            string name = $"VER_UE{m.Groups[1].Value}_{m.Groups[2].Value}{(m.Groups[3].Success ? "EA" : "")}";
            if (Enum.TryParse<EngineVersion>(name, ignoreCase: true, out var v) && IsOffered(v))
                return v;
        }

        throw new UsageException($"--engine: невідома версія рушія «{s}». Приклади: auto, 4.27, 5.3, 5.0EA, VER_UE4_27, VER_UE5_3, 27, 503.");
    }

    private static bool IsOffered(EngineVersion v) => Tags.ContainsValue(v);

    /// <summary>Exactly the versions offered by EngineVersionWindow (tags as in the XAML).</summary>
    public static readonly Dictionary<string, EngineVersion> Tags = BuildTags();

    private static Dictionary<string, EngineVersion> BuildTags()
    {
        var d = new Dictionary<string, EngineVersion>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i <= 27; i++)
            d[i.ToString()] = Enum.Parse<EngineVersion>($"VER_UE4_{i}");
        d["500EA"] = EngineVersion.VER_UE5_0EA;
        for (int i = 0; i <= 8; i++)
            d[$"50{i}"] = Enum.Parse<EngineVersion>($"VER_UE5_{i}");
        return d;
    }

    [GeneratedRegex(@"^(?:UE)?\s*([45])\.(\d+)\s*(EA)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ShortForm();

    [GeneratedRegex(@"VER_UE(\d+)_(\d+)(EA)?", RegexOptions.IgnoreCase)]
    private static partial Regex LabelRegex();

    /// <summary>Copy of FileTabState.FormatEngineVersion (VER_UE5_3 -> UE5.3).</summary>
    public static string Format(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var m = LabelRegex().Match(raw);
        if (!m.Success) return raw;
        string ea = m.Groups[3].Success ? "EA" : "";
        return $"UE{m.Groups[1].Value}.{m.Groups[2].Value}{ea}";
    }
}
