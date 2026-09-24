using System.IO;

namespace UELT.Hikaro;

public static class SettingsManager
{
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hikaro", "UELT", "settings.txt");

    public static bool CreateStatusFiles { get; private set; } = true;
    public static bool DiscordPresenceEnabled { get; private set; } = true;
    public static bool CV2DecryptEnabled { get; private set; } = false;
    public static bool CV2IsDemo { get; private set; } = false;
    public static string OriginalTextAlignment { get; private set; } = "Center";
    public static int OriginalTextFontSize { get; private set; } = 12;
    public static int EditTextFontSize { get; private set; } = 14;
    public static int MaxTranslationMemoryVariants { get; private set; } = 5;

    public static void Load()
    {
        if (!File.Exists(SettingsPath)) return;

        foreach (var line in File.ReadAllLines(SettingsPath))
        {
            var parts = line.Split('=');
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            var value = parts[1].Trim();

            if (key == "CreateStatusFiles")
                CreateStatusFiles = value == "true";

            if (key == "DiscordPresenceEnabled")
                DiscordPresenceEnabled = value == "true";

            if (key == "CV2DecryptEnabled")
                CV2DecryptEnabled = value == "true";

            if (key == "CV2IsDemo")
                CV2IsDemo = value == "true";

            if (key == "OriginalTextAlignment" && (value == "Left" || value == "Center" || value == "Right"))
                OriginalTextAlignment = value;

            if (key == "OriginalTextFontSize" && int.TryParse(value, out var size) && size >= 8 && size <= 14)
                OriginalTextFontSize = size;

            if (key == "EditTextFontSize" && int.TryParse(value, out var editSize) && editSize >= 8 && editSize <= 14)
                EditTextFontSize = editSize;

            if (key == "MaxTranslationMemoryVariants" && int.TryParse(value, out var maxVariants) && maxVariants >= 3 && maxVariants <= 8)
                MaxTranslationMemoryVariants = maxVariants;
        }
    }

    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllLines(SettingsPath,
        [
            $"CreateStatusFiles={CreateStatusFiles.ToString().ToLower()}",
            $"DiscordPresenceEnabled={DiscordPresenceEnabled.ToString().ToLower()}",
            $"CV2DecryptEnabled={CV2DecryptEnabled.ToString().ToLower()}",
            $"CV2IsDemo={CV2IsDemo.ToString().ToLower()}",
            $"OriginalTextAlignment={OriginalTextAlignment}",
            $"OriginalTextFontSize={OriginalTextFontSize}",
            $"EditTextFontSize={EditTextFontSize}",
            $"MaxTranslationMemoryVariants={MaxTranslationMemoryVariants}",
        ]);
    }

    public static void SetCreateStatusFiles(bool value) { CreateStatusFiles = value; Save(); }
    public static void SetDiscordPresenceEnabled(bool value) { DiscordPresenceEnabled = value; Save(); }
    public static void SetCV2DecryptEnabled(bool value) { CV2DecryptEnabled = value; Save(); }
    public static void SetCV2IsDemo(bool value) { CV2IsDemo = value; Save(); }
    public static void SetOriginalTextAlignment(string value) { OriginalTextAlignment = value; Save(); }
    public static void SetOriginalTextFontSize(int value) { OriginalTextFontSize = value; Save(); }
    public static void SetEditTextFontSize(int value) { EditTextFontSize = value; Save(); }
    public static void SetMaxTranslationMemoryVariants(int value) { MaxTranslationMemoryVariants = Math.Clamp(value, 3, 8); Save(); }
}