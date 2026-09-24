namespace UELT.Hikaro;

internal static class ImportFileNameMatcher
{
    public static bool IsMatch(string targetBase, string sourceBase)
    {
        if (string.Equals(targetBase, sourceBase, StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(Normalize(targetBase), Normalize(sourceBase), StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        if (name.StartsWith("N_", StringComparison.OrdinalIgnoreCase))
            name = name[2..];

        int searchFrom = name.Length;
        int lastUnderscore = name.LastIndexOf('_', searchFrom - 1);
        if (lastUnderscore > 0 && IsDigitsOnly(name[(lastUnderscore + 1)..]))
            searchFrom = lastUnderscore;

        int nUnderscore = name.LastIndexOf('_', searchFrom - 1);
        if (nUnderscore > 0)
        {
            string segment = name[(nUnderscore + 1)..searchFrom];
            if (segment.Equals("N", StringComparison.OrdinalIgnoreCase))
                name = name[..nUnderscore];
        }

        return name;
    }

    private static bool IsDigitsOnly(string s)
        => !string.IsNullOrEmpty(s) && s.All(char.IsDigit);
}