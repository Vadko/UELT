using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UELT.Hikaro;

[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(RowStatusPayload))]
internal partial class RowStatusJsonContext : JsonSerializerContext { }

internal class RowStatusPayload
{
    [JsonPropertyName("key_mode")]
    public string KeyMode { get; set; }
    [JsonPropertyName("rows")]
    public Dictionary<string, string> Rows { get; set; }
}

public enum RowStatus
{
    None,
    NeedsReview,
    Approved
}

public class RowStatusManager
{
    private const string ModeId = "id";
    private const string ModeIndex = "index";

    public static string GetStatusFilePath(string mainFilePath)
        => Path.ChangeExtension(mainFilePath, ".STATUS");

    public static void Save(string savedFilePath, IEnumerable<DataGridItem> rows, bool useIndexKeys = false)
    {
        var rowList = rows.ToList();
        var dict = new Dictionary<string, string>();

        for (int i = 0; i < rowList.Count; i++)
        {
            var row = rowList[i];
            if (row.Status == RowStatus.None) continue;

            string key = useIndexKeys ? i.ToString() : (row.ID ?? "");
            dict[key] = row.Status.ToString();
        }

        string statusPath = GetStatusFilePath(savedFilePath);

        if (dict.Count == 0)
        {
            if (File.Exists(statusPath))
                File.Delete(statusPath);

            return;
        }

        var payload = new RowStatusPayload
        {
            KeyMode = useIndexKeys ? ModeIndex : ModeId,
            Rows = dict
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        options.TypeInfoResolverChain.Add(RowStatusJsonContext.Default);
        string json = JsonSerializer.Serialize(payload, typeof(RowStatusPayload), options);
        File.WriteAllText(statusPath, json);
    }

    public static void LoadFromFile(string statusFilePath, IEnumerable<DataGridItem> rows, bool useIndexKeys = false)
    {
        if (!File.Exists(statusFilePath))
            return;

        try
        {
            string json = File.ReadAllText(statusFilePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("key_mode", out var modeProp) || !root.TryGetProperty("rows", out var rowsProp))
                return;

            bool indexMode = modeProp.GetString() == ModeIndex;
            var dict = JsonSerializer.Deserialize(rowsProp.GetRawText(), RowStatusJsonContext.Default.DictionaryStringString);

            if (dict == null)
                return;

            var rowList = rows.ToList();

            if (indexMode)
            {
                foreach (var (key, statusStr) in dict)
                {
                    if (!int.TryParse(key, out int idx))
                        continue;

                    if (idx < 0 || idx >= rowList.Count)
                        continue;

                    if (Enum.TryParse<RowStatus>(statusStr, out var status))
                    {
                        var currentStatus = rowList[idx].Status;

                        if (status == RowStatus.Approved) 
                        {
                            rowList[idx].Status = RowStatus.Approved;
                        }
                        else if (status == RowStatus.NeedsReview && currentStatus == RowStatus.None) 
                        {
                            rowList[idx].Status = RowStatus.NeedsReview;
                        }
                    }
                }
            }
            else
            {
                var lookup = rowList.Where(r => r.ID != null).ToDictionary(r => r.ID!);

                foreach (var (id, statusStr) in dict)
                {
                    if (!lookup.TryGetValue(id, out var row))
                        continue;

                    if (Enum.TryParse<RowStatus>(statusStr, out var status))
                    {
                        var currentStatus = row.Status;

                        if (status == RowStatus.Approved)
                        {
                            row.Status = RowStatus.Approved;
                        }
                        else if (status == RowStatus.NeedsReview && currentStatus == RowStatus.None)
                        {
                            row.Status = RowStatus.NeedsReview;
                        }
                    }
                }
            }
        }
        catch { }
    }

}