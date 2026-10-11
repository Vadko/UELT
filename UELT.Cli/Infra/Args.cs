using System.Globalization;

namespace UELT.Cli.Infra;

/// <summary>Option definition: "--name" (+ aliases like "-o"), with or without a value.</summary>
public sealed record Opt(string Name, bool HasValue = false, bool Repeatable = false, params string[] Aliases);

/// <summary>Hand-rolled parser: positionals, "--flag", "--opt value", "--opt=value", "-o value", "--" ends options.</summary>
public sealed class ParsedArgs
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

    public List<string> Positionals { get; } = new();

    public bool Help => _flags.Contains("--help");

    public bool Has(string name) => _flags.Contains(name) || _values.ContainsKey(name);

    public string Get(string name) => _values.TryGetValue(name, out var v) ? v[^1] : null;

    public List<string> GetAll(string name) => _values.TryGetValue(name, out var v) ? v : new List<string>();

    public int? GetInt(string name, int min, int max)
    {
        string s = Get(name);
        if (s == null) return null;
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < min || n > max)
            throw new UsageException($"{name}: очікується ціле число від {min} до {max}, отримано «{s}».");
        return n;
    }

    /// <summary>Value of a "choice" option (case-insensitive), or <paramref name="def"/>.</summary>
    public string GetChoice(string name, string def, params string[] allowed)
    {
        string s = Get(name);
        if (s == null) return def;
        var match = allowed.FirstOrDefault(a => string.Equals(a, s, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new UsageException($"{name}: недопустиме значення «{s}». Допустимі: {string.Join(", ", allowed)}.");
        return match;
    }

    public static ParsedArgs Parse(IReadOnlyList<string> args, IEnumerable<Opt> specs)
    {
        var all = specs.Concat(new[] { new Opt("--help", false, false, "-h", "-?"), new Opt("--quiet", false, false, "-q") }).ToList();
        var byName = new Dictionary<string, Opt>(StringComparer.Ordinal);
        foreach (var o in all)
        {
            byName[o.Name] = o;
            foreach (var a in o.Aliases) byName[a] = o;
        }

        var result = new ParsedArgs();
        bool onlyPositionals = false;

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];

            if (onlyPositionals || a == "-" || !a.StartsWith('-') || IsNegativeNumber(a))
            {
                result.Positionals.Add(a);
                continue;
            }

            if (a == "--")
            {
                onlyPositionals = true;
                continue;
            }

            string name = a, inlineValue = null;
            int eq = a.IndexOf('=');
            if (a.StartsWith("--") && eq > 2)
            {
                name = a[..eq];
                inlineValue = a[(eq + 1)..];
            }

            if (!byName.TryGetValue(name, out var spec))
                throw new UsageException($"Невідомий параметр: {name}");

            if (!spec.HasValue)
            {
                if (inlineValue != null)
                    throw new UsageException($"Параметр {spec.Name} не приймає значення.");
                result._flags.Add(spec.Name);
                continue;
            }

            string value = inlineValue;
            if (value == null)
            {
                if (i + 1 >= args.Count)
                    throw new UsageException($"Параметр {spec.Name} потребує значення.");
                value = args[++i];
            }

            if (!result._values.TryGetValue(spec.Name, out var list))
                result._values[spec.Name] = list = new List<string>();
            else if (!spec.Repeatable)
                throw new UsageException($"Параметр {spec.Name} вказано кілька разів.");

            list.Add(value);
        }

        return result;
    }

    private static bool IsNegativeNumber(string s) => s.Length > 1 && s[0] == '-' && char.IsDigit(s[1]);

    public void RequirePositionals(int min, int max, string what)
    {
        if (Positionals.Count < min)
            throw new UsageException($"Не вказано {what}.");
        if (max >= 0 && Positionals.Count > max)
            throw new UsageException($"Забагато аргументів: {string.Join(" ", Positionals.Skip(max))}");
    }
}
