namespace UELT.Hikaro;

public static class PluralizationHelper
{
    private static string Pluralize(int count, string one, string few, string many)
    {
        int abs = Math.Abs(count), mod10 = abs % 10, mod100 = abs % 100;
        if (mod100 >= 11 && mod100 <= 19) return many;
        return mod10 switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many
        };
    }

    public static string GetRowsWord(int count) =>
        Pluralize(count, "рядок", "рядки", "рядків");

    public static string GetWordsWord(int count) =>
        Pluralize(count, "слово", "слова", "слів");

    public static string GetTranslateWord(int count) =>
    Pluralize(count, "переклад", "переклади", "перекладів");

    internal static string GetRowsWordLocative(int count)
    {
        string form = Pluralize(count, "рядку", "рядках", "рядках");
        return $"у {count} {form}";
    }

    internal static string GetCellsWord(int count) =>
        Pluralize(count, "комірку", "комірки", "комірок");

    internal static string GetCellsWordReplace(int count)
    {
        if (count % 10 == 1 && count % 100 != 11)
            return $"{count} комірці";
        return $"{count} комірках";
    }

    internal static string GetDuplicatesWord(int count) =>
        Pluralize(count, "дублікат", "дублікати", "дублікатів");

    internal static string GetNewWord(int count) =>
        Pluralize(count, "нового", "нових", "нових");

    internal static string GetFilesWord(int count) =>
        Pluralize(count, "файл", "файли", "файлів");

    internal static string GetFilesWord_CreateLocresWindow(int count)
    {
        if (count % 10 == 1 && count % 100 != 11)
            return "файлі";
        return "файлах";
    }

    internal static string GetTabsWord(int count) =>
        Pluralize(count, "вкладку", "вкладки", "вкладок");

    internal static string GetTabsWordLocative(int count)
    {
        if (count % 10 == 1 && count % 100 != 11)
            return "вкладці";
        return "вкладках";
    }

    internal static string GetTabsWordDiscord(int count) =>
        Pluralize(count, "вкладка", "вкладки", "вкладок");

    internal static string GetHashesWord(int count) =>
        Pluralize(count, "хеш", "хеші", "хешів");
}