using System.Reflection;

namespace UELT.Hikaro;

public static class AppVersion
{
    public const string Name = "UELT";
    public static readonly string Version = ReadVersion();
    public static string Full => $"{Name} {Version}";

    private static string ReadVersion()
    {
        string informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrEmpty(informational))
        {
            return typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        int buildMetadata = informational.IndexOf('+');
        return buildMetadata < 0 ? informational : informational[..buildMetadata];
    }
}
