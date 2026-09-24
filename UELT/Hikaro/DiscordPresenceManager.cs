using DiscordRPC;
using DiscordRPC.Logging;
using System.IO;

namespace UELT.Hikaro;

public sealed class DiscordPresenceManager : IDisposable
{
    private const string AppClientId = "1491610391393075230";

    private DiscordRpcClient _client;
    private bool _disposed;
    private readonly object _lock = new();
    private readonly Timestamps _sessionStart = Timestamps.Now;

    public DiscordPresenceManager()
    {
        try
        {
            _client = new DiscordRpcClient(AppClientId)
            {
                Logger = new ConsoleLogger { Level = LogLevel.Warning }
            };

            _client.OnReady += (_, e) =>
                System.Diagnostics.Debug.WriteLine($"[Discord RPC] Під’єднано як {e.User.Username}");

            _client.OnError += (_, e) =>
                System.Diagnostics.Debug.WriteLine($"[Discord RPC] Помилка: {e.Message}");

            _client.Initialize();
            SetIdle();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Discord RPC] Помилка ініціалізації: {ex.Message}");
            _client = null;
        }
    }

    public void SetIdle()
    {
        Set(new RichPresence
        {
            Details = "Розглядає інтерфейс",
            State = "👀",
            Timestamps = _sessionStart,
            Assets = new Assets
            {
                LargeImageKey = "app_icon",
                LargeImageText = "UELT"
            }
        });
    }

    public void SetFileState(string filePath, int rowCount, int totalRowCount, int openTabCount, bool hasUnsavedChanges)
    {
        if (_disposed || _client == null)
            return;

        string fileName = Path.GetFileName(filePath);
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        string details = hasUnsavedChanges ? $"✏️{fileName}" : $"{fileName}";
        int displayRows = openTabCount > 1 ? totalRowCount : rowCount;
        string tabsPart = openTabCount > 1 ? $"{openTabCount} {PluralizationHelper.GetTabsWordDiscord(openTabCount)}" : "";
        string rowsPart = $"{displayRows} {PluralizationHelper.GetRowsWord(displayRows)}";
        string state = string.IsNullOrEmpty(tabsPart) ? rowsPart : $"{tabsPart} · {rowsPart}";

        Set(new RichPresence
        {
            Details = details,
            State = state,
            Timestamps = _sessionStart,
            Assets = new Assets
            {
                LargeImageKey = "app_icon",
                LargeImageText = "UELT"
            }
        });
    }

    public void Clear()
    {
        if (_disposed || _client == null) return;
        _client.ClearPresence();
    }

    private void Set(RichPresence presence)
    {
        if (_disposed || _client == null) return;
        if (!SettingsManager.DiscordPresenceEnabled) return;
        _client.SetPresence(presence);
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        try
        {
            if (_client != null)
            {
                _client.ClearPresence();
                _client.Dispose();
                _client = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Discord RPC] Помилка при Dispose: {ex.Message}");
        }
    }
}