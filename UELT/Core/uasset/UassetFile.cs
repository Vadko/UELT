using System.IO;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet.Bytecode.Expressions;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;
using UELT.Core.uasset;

namespace UELT.Core;

public class UassetFile : IAsset
{
    public UAsset Asset { get; private set; }
    public string AssetType => _assetType ?? "Normal";
    public bool IsGood { get; private set; } = true;
    public string FilePath { get; private set; }
    public string EngineVersionLabel { get; private set; } = "";
    public string MappingsFilePath { get; private set; } = "";
    private readonly MappingType _mappingType = MappingType.None;

    private string _assetType;
    private readonly Dictionary<int, Action<string>> _writeBackMap = [];
    private readonly List<string> _extractionWarnings = [];
    private readonly J5BinderAssetParser _j5Parser = new();
    private readonly OctopathBinaryAssetParser _octopathParser = new();
    private string _selectedLanguage = null;
    private bool _languageFilterIsPrefix = false;
    private readonly HashSet<string> _unknownTypes = [];
    private readonly HashSet<string> _rawStructTypes = [];
    private int _numRawStructs = 0;
    private int _rawExportCount = 0;
    private bool _hasNameMapDuplicates = false;
    private bool _failedBinaryEquality = false;

    public UassetFile(string filePath, EngineVersion? engineVersion = null, string mappingsPath = null, MappingType mappingType = MappingType.None)
    {
        FilePath = filePath;
        _mappingType = mappingType;

        try
        {
            Usmap mappings = null;

            if (!string.IsNullOrEmpty(mappingsPath) && File.Exists(mappingsPath))
            {
                mappings = new Usmap(mappingsPath);
            }

            if (engineVersion.HasValue)
            {
                Asset = mappings != null
                    ? new UAsset(filePath, engineVersion.Value, mappings)
                    : new UAsset(filePath, engineVersion.Value);
                EngineVersionLabel = engineVersion.Value.ToString();
            }
            else
            {
                Asset = new UAsset(filePath);
            }

            IsGood = true;
            DetectAssetType();
            CollectPropertiesStatistics();
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("unversioned asset") || ex is UnknownEngineVersionException)
            {
                throw new Exception("UNVERSIONED_ASSET_NEEDS_VERSION", ex);
            }
            IsGood = false;
            throw new Exception($"Не вдалося завантажити '{Path.GetFileName(filePath)}':\n{ex.Message}", ex);
        }
    }

    private void DetectAssetType()
    {
        if (Asset?.Exports == null) return;

        foreach (var export in Asset.Exports)
        {
            if (export is StringTableExport)
            {
                _assetType = "StringTable";
                return;
            }
        }

        foreach (var export in Asset.Exports)
        {
            if (export is not DataTableExport dataTable) continue;

            var firstRow = dataTable.Table?.Data?.FirstOrDefault();
            if (firstRow is StructPropertyData firstStruct && firstStruct.Value != null
                && firstStruct.Value.Count > 0
                && firstStruct.Value.All(p => p is StrPropertyData))
            {
                _assetType = "LocalizationDataTable";
            }
            else if (firstRow is StructPropertyData strArrStruct
                && strArrStruct.Value != null
                && strArrStruct.Value.Any(p =>
                    p is ArrayPropertyData ap
                    && ap.ArrayType?.ToString() == "StrProperty"
                    && ap.Value != null
                    && ap.Value.Any(el =>
                        el is StrPropertyData sp
                        && !string.IsNullOrEmpty(sp.Value?.ToString()))))
            {
                _assetType = "StrArrayDataTable";
            }
            else if (firstRow is StructPropertyData mixedStruct
                && mixedStruct.Value != null
                && mixedStruct.Value.Count > 0
                && mixedStruct.Value.Any(p => p is StrPropertyData)
                && mixedStruct.Value.Any(p => p is NamePropertyData np
                    && !string.IsNullOrEmpty(np.Value?.ToString())
                    && np.Value.ToString() != "None"))
            {
                _assetType = "StrNameDataTable";
            }
            else if (firstRow is StructPropertyData nameStruct
                && nameStruct.Value != null
                && nameStruct.Value.Count > 0
                && nameStruct.Value.Any(p =>
                    p is NamePropertyData np
                    && !string.IsNullOrEmpty(np.Value?.ToString())
                    && np.Value.ToString() != "None"))
            {
                _assetType = "NameDataTable";
            }
            else if (firstRow is StructPropertyData sevStruct
                && sevStruct.Value != null
                && sevStruct.Value.OfType<StructPropertyData>()
                    .Any(inner => inner.Value != null
                               && inner.Value.Count > 0
                               && inner.Value.All(p => p is StrPropertyData)))
            {
                _assetType = "SevLocalizationDataTable";
            }
            else
            {
                _assetType = "DataTable";
            }
            return;
        }

        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;

            foreach (var prop in normalExport.Data)
            {
                if (prop is not MapPropertyData mapProp || mapProp.Value == null) continue;

                foreach (var kvp in mapProp.Value)
                {
                    if (kvp.Value is not StructPropertyData tableStruct) continue;
                    if (tableStruct.Value == null) continue;

                    foreach (var tableField in tableStruct.Value)
                    {
                        if (tableField is not MapPropertyData entriesMap || entriesMap.Value == null) continue;

                        var firstEntry = entriesMap.Value.FirstOrDefault();
                        if (firstEntry.Key is not IntPropertyData) continue;
                        if (firstEntry.Value is not StructPropertyData entryStruct) continue;
                        if (entryStruct.Value == null) continue;
                        if (!entryStruct.Value.Any(p => p is StrPropertyData)) continue;

                        _assetType = "StringTableBundle";
                        return;
                    }
                }
            }
        }

        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;

            bool hasLanguageEnum = normalExport.Data
                .OfType<EnumPropertyData>()
                .Any(e => e.Name?.ToString() == "Language" && e.Value?.ToString().StartsWith("ELocaleEnum::") == true);
            if (!hasLanguageEnum) continue;

            foreach (var prop in normalExport.Data)
            {
                if (prop is not MapPropertyData mapProp || mapProp.Value == null || mapProp.Value.Count == 0) continue;

                var firstKvp = mapProp.Value.ElementAt(0);
                if (firstKvp.Key is not StrPropertyData) continue;
                if (firstKvp.Value is not StructPropertyData structVal) continue;
                if (structVal.StructType?.ToString() != "LocaleText") continue;

                _assetType = "LocaleMapAsset";
                return;
            }
        }

        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;

            foreach (var prop in normalExport.Data)
            {
                if (prop is not MapPropertyData mapProp) continue;
                if (mapProp.Name?.ToString() != "Subtitles") continue;
                if (mapProp.Value == null || mapProp.Value.Count == 0) continue;

                var firstKvp = mapProp.Value.ElementAt(0);
                if (firstKvp.Key is not StrPropertyData) continue;
                if (firstKvp.Value is not StructPropertyData structVal) continue;
                if (structVal.StructType?.ToString() != "LocalizedSubtitleClip") continue;

                _assetType = "FrogVideoSubtitleAsset";
                return;
            }
        }

        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;

            foreach (var prop in normalExport.Data)
            {
                if (prop is not MapPropertyData mapProp) continue;
                if (mapProp.Name?.ToString() != "LocalizedText") continue;
                if (mapProp.Value == null || mapProp.Value.Count == 0) continue;

                var firstKvp = mapProp.Value.ElementAt(0);
                if (firstKvp.Key is StrPropertyData && firstKvp.Value is StrPropertyData)
                {
                    _assetType = "DialogueAsset";
                    return;
                }
            }
        }

        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;

            foreach (var prop in normalExport.Data)
            {
                if (prop is not MapPropertyData mapProp) continue;
                if (mapProp.Name?.ToString() != "ActorMap") continue;
                if (mapProp.Value == null || mapProp.Value.Count == 0) continue;

                var firstKvp = mapProp.Value.ElementAt(0);
                if (firstKvp.Key is NamePropertyData)
                {
                    _assetType = "DialogueActorAsset";
                    return;
                }
            }
        }

        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;

            foreach (var prop in normalExport.Data)
            {
                if (prop is not MapPropertyData mapProp || mapProp.Value == null || mapProp.Value.Count == 0) continue;

                var firstKvp = mapProp.Value.ElementAt(0);
                if (firstKvp.Key is not NamePropertyData) continue;
                if (firstKvp.Value is not StructPropertyData structVal) continue;
                if (structVal.Value == null) continue;
                if (!structVal.Value.Any(p => p is StrPropertyData sp && !string.IsNullOrEmpty(sp.Value?.ToString()))) continue;

                _assetType = "TextDataMap";
                return;
            }
        }

        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;

            foreach (var prop in normalExport.Data)
            {
                if (prop is not ArrayPropertyData arrayProp) continue;
                if (arrayProp.ArrayType?.ToString() != "StructProperty") continue;
                if (arrayProp.Value == null || arrayProp.Value.Length == 0) continue;

                var firstEl = arrayProp.Value[0] as StructPropertyData;
                if (firstEl?.Value == null) continue;

                foreach (var field in firstEl.Value)
                {
                    if (field is not MapPropertyData mapField || mapField.Value == null || mapField.Value.Count == 0) continue;

                    var firstKvp = mapField.Value.ElementAt(0);
                    if (firstKvp.Key is NamePropertyData && firstKvp.Value is TextPropertyData textVal
                        && !string.IsNullOrEmpty(textVal.CultureInvariantString?.ToString()))
                    {
                        _assetType = "LocalizationGroupAsset";
                        return;
                    }
                }
            }
        }

        foreach (var export in Asset.Exports)
        {
            if (export is NormalExport ne && J5BinderAssetParser.IsJ5BinderAsset(ne))
            {
                _assetType = "J5BinderAsset";
                return;
            }
        }

        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport dlNormal) continue;
            string dlName = dlNormal.ObjectName?.ToString() ?? "";
            if (!dlName.StartsWith("DialogueLine_", StringComparison.Ordinal)) continue;
            if (dlNormal.Data == null) continue;
            bool hasTextId = dlNormal.Data.Any(p => p is NamePropertyData && p.Name?.ToString() == "TextId");
            bool hasText = dlNormal.Data.Any(p => p is StrPropertyData && p.Name?.ToString() == "Text");
            if (hasTextId && hasText)
            {
                _assetType = "DialogueChapterAsset";
                return;
            }
        }

        foreach (var export in Asset.Exports)
        {
            if (export is NormalExport ne && OctopathBinaryAssetParser.IsOctopathBinaryAsset(ne))
            {
                _assetType = "OctopathBinaryAsset";
                return;
            }
        }

        _assetType = "Normal";
    }

    #region Helpers
    public bool HasAnyTexts()
    {
        var texts = ExtractTexts();
        return texts != null && texts.Count > 0;
    }

    private static void CollectTextPropertyPrefixes(List<PropertyData> properties, List<string> found, HashSet<string> seen)
    {
        if (properties == null) return;
        foreach (var prop in properties)
        {
            if (prop is TextPropertyData textProp)
            {
                string prefix = StripPropertySuffix(textProp.Name?.ToString() ?? "");
                if (!string.IsNullOrEmpty(prefix) && seen.Add(prefix))
                    found.Add(prefix);
            }
            else if (prop is StructPropertyData structProp && structProp.Value != null)
                CollectTextPropertyPrefixes(structProp.Value, found, seen);
            else if (prop is ArrayPropertyData arrayProp && arrayProp.Value != null)
            {
                foreach (var el in arrayProp.Value)
                    if (el is StructPropertyData arrStruct && arrStruct.Value != null)
                        CollectTextPropertyPrefixes(arrStruct.Value, found, seen);
            }
        }
    }

    public void SetSelectedLanguage(string language)
    {
        _selectedLanguage = language;
        _languageFilterIsPrefix = (_assetType == "Normal" || _assetType == "DataTable");
    }

    private bool MatchesLanguageFilter(string propName)
    {
        if (_selectedLanguage == null) return true;
        if (_languageFilterIsPrefix)
        {
            string stripped = StripPropertySuffix(propName);
            if (stripped == propName) return true;
            return stripped == _selectedLanguage;
        }
        return propName == _selectedLanguage;
    }

    public List<string> GetAvailableLanguages()
    {
        if (Asset?.Exports == null) return new List<string>();

        return _assetType switch
        {
            "DataTable" or "LocalizationDataTable" => GetLanguagesFromDataTable(),
            "Normal" => GetLanguagesFromNormal(),
            "DialogueAsset" => GetLanguagesFromDialogueAsset(),
            "SevLocalizationDataTable" => GetLanguagesFromSevLocalizationDataTable(),
            "FrogVideoSubtitleAsset" => GetLanguagesFromFrogVideoSubtitleAsset(),
            _ => new List<string>()
        };
    }
    #endregion

    #region Write-back helpers
    private void RegisterTextWriteBack(int index, TextPropertyData textProp)
    {
        _writeBackMap[index] = value => textProp.CultureInvariantString = new FString(value);
    }

    private void RegisterStrWriteBack(int index, StrPropertyData strProp)
    {
        _writeBackMap[index] = value => strProp.Value = new FString(value);
    }

    private void RegisterNameWriteBack(int index, NamePropertyData nameProp)
    {
        _writeBackMap[index] = value =>
        {
            string nameText = value == "<none>" ? "None" : value;
            nameProp.Value = FName.FromString(Asset, nameText);
        };
    }

    private void RegisterStringTableWriteBack(int index, StringTableExport export, FString key)
    {
        _writeBackMap[index] = value => export.Table[key] = new FString(value);
    }

    private void RegisterKismetStringConstWriteBack(int index, EX_StringConst stringConst)
    {
        _writeBackMap[index] = value => stringConst.Value = value;
    }
    #endregion

    public List<List<string>> ExtractTexts()
    {
        _writeBackMap.Clear();
        _extractionWarnings.Clear();
        var result = new List<List<string>>();
        int globalIndex = 0;

        if (Asset?.Exports == null) return result;

        switch (_assetType)
        {
            case "DataTable":
                ExtractFromDataTable(ref globalIndex, result);
                break;
            case "LocalizationDataTable":
                ExtractFromLocalizationDataTable(ref globalIndex, result);
                break;
            case "StringTableBundle":
                ExtractFromStringTableBundle(ref globalIndex, result);
                break;
            case "DialogueAsset":
                ExtractFromDialogueAsset(ref globalIndex, result);
                break;
            case "FrogVideoSubtitleAsset": 
                ExtractFromFrogVideoSubtitleAsset(ref globalIndex, result);
                break;
            case "DialogueActorAsset":
                ExtractFromDialogueActorAsset(ref globalIndex, result);
                break;
            case "StringTable":
                ExtractFromStringTable(ref globalIndex, result);
                break;
            case "StrArrayDataTable":
                ExtractFromStrArrayDataTable(ref globalIndex, result);
                break;
            case "NameDataTable": 
                ExtractFromNameDataTable(ref globalIndex, result);
                break;
            case "StrNameDataTable":
                ExtractFromStrNameDataTable(ref globalIndex, result);
                break;
            case "TextDataMap":
                ExtractFromTextDataMap(ref globalIndex, result);
                break;
            case "LocalizationGroupAsset":
                ExtractFromLocalizationGroupAsset(ref globalIndex, result);
                break;
            case "J5BinderAsset":
                try
                {
                    _j5Parser.Extract(
                        Asset.Exports.OfType<NormalExport>()
                             .First(J5BinderAssetParser.IsJ5BinderAsset),
                        ref globalIndex, result);
                }
                catch (Exception ex)
                {
                    throw new Exception($"J5 Extract failed: {ex.Message}\n{ex.StackTrace}", ex);
                }
                break;
            case "DialogueChapterAsset":
                ExtractFromDialogueChapterAsset(ref globalIndex, result);
                break;
            case "SevLocalizationDataTable":
                ExtractFromSevLocalizationDataTable(ref globalIndex, result);
                break;
            case "LocaleMapAsset":
                ExtractFromLocaleMapAsset(ref globalIndex, result);
                break;
            case "OctopathBinaryAsset":
                try
                {
                    var export = Asset.Exports.OfType<NormalExport>()
                        .First(OctopathBinaryAssetParser.IsOctopathBinaryAsset);
                    _octopathParser.Extract(export, ref globalIndex, result);
                }
                catch (Exception ex)
                {
                    throw new Exception($"OctopathBinaryAsset extract failed: {ex.Message}", ex);
                }
                break;
            default:
                ExtractFromNormal(ref globalIndex, result);
                ExtractFromKismetText(ref globalIndex, result);
                ExtractFromKismetStringConst(ref globalIndex, result);
                break;
        }

        return result;
    }

    #region DataTable case
    private void ExtractFromDataTable(ref int index, List<List<string>> result)
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not DataTableExport dataTable) continue;
            if (dataTable.Table?.Data == null) continue;

            foreach (var row in dataTable.Table.Data)
            {
                if (row is not StructPropertyData structProp) continue;

                if (structProp.Value == null) continue;

                foreach (var prop in structProp.Value)
                {
                    if (prop is TextPropertyData textProp)
                    {
                        if (!MatchesLanguageFilter(prop.Name?.ToString() ?? "")) continue;

                        string text = textProp.CultureInvariantString?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(text))
                        {
                            string propValue = textProp.Value?.ToString() ?? prop.Name?.ToString() ?? "";
                            string ns = textProp.Namespace?.ToString() ?? "";
                            string path = string.IsNullOrEmpty(ns)
                                ? propValue
                                : $"{ns}::{propValue}";
                            result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(text) });
                            RegisterTextWriteBack(index, textProp);
                            index++;
                        }
                    }
                }
            }
        }
    }

    private List<string> GetLanguagesFromDataTable()
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not DataTableExport dataTable) continue;
            var firstRow = dataTable.Table?.Data?.FirstOrDefault();
            if (firstRow is not StructPropertyData firstStruct || firstStruct.Value == null) continue;

            var textPrefixes = firstStruct.Value
                .OfType<TextPropertyData>()
                .Select(p => StripPropertySuffix(p.Name?.ToString() ?? ""))
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .ToList();
            if (textPrefixes.Count > 0) return textPrefixes;

            return firstStruct.Value
                .OfType<StrPropertyData>()
                .Select(p => p.Name?.ToString() ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
        }
        return new List<string>();
    }
    #endregion

    #region Normal case
    private static string StripPropertySuffix(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        int underscoreCount = 0;
        for (int i = name.Length - 1; i >= 0; i--)
        {
            if (name[i] == '_')
            {
                underscoreCount++;
                if (underscoreCount == 2) return name[..i];
            }
        }
        return name;
    }

    private void ExtractFromNormal(ref int index, List<List<string>> result)
    {
        for (int exportIdx = 0; exportIdx < Asset.Exports.Count; exportIdx++)
        {
            var export = Asset.Exports[exportIdx];

            if (export is not NormalExport normalExport)
                continue;

            if (normalExport.Data == null || normalExport.Data.Count == 0)
                continue;

            try
            {
                string objectName = normalExport.ObjectName?.ToString() ?? "";

                string strObjectName = objectName;
                var nameTextProp = normalExport.Data
                    .OfType<TextPropertyData>()
                    .FirstOrDefault(p =>
                    {
                        string val = p.Value?.ToString() ?? "";
                        return val.Length == 32 && val.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'));
                    });
                if (nameTextProp != null)
                {
                    string uuid = nameTextProp.Value?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(uuid))
                        strObjectName = uuid;
                }

                ProcessProperties(normalExport.Data, "", ref index, result, objectName, strObjectName);
            }
            catch (Exception ex)
            {
                string exportName = normalExport.ObjectName?.ToString() ?? $"#{exportIdx}";
                _extractionWarnings.Add($"Export '{exportName}': не вдалося обробити властивості.\n{ex.Message}");
            }
        }
    }

    private readonly HashSet<string> _targetStrFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "SpokenText",
        "ScreenplayLineText",
        "DescriptionText"
    };

    private void ProcessProperties(List<PropertyData> properties, string path, ref int index, List<List<string>> result, string objectName = "", string strObjectName = null)
    {
        strObjectName ??= objectName;

        if (properties == null) return;

        foreach (var property in properties)
        {
            if (property == null) continue;

            string propertyName = property.Name?.ToString() ?? "";
            string currentPath = string.IsNullOrEmpty(path) ? propertyName : $"{path}.{propertyName}";

            if (property is StrPropertyData strProperty)
            {
                if (!_targetStrFields.Contains(propertyName)) continue;

                if (!MatchesLanguageFilter(propertyName)) continue;

                string text = strProperty.Value?.ToString() ?? "";
                if (!string.IsNullOrEmpty(text))
                {
                    string finalPath = string.IsNullOrEmpty(strObjectName)
                        ? currentPath
                        : $"{strObjectName}_{currentPath}";

                    result.Add(new List<string> { finalPath, AssetHelper.ReplaceBreaklines(text) });
                    RegisterStrWriteBack(index, strProperty);
                    index++;
                }
            }
            else if (property is TextPropertyData textProperty)
            {
                if (!MatchesLanguageFilter(propertyName)) continue;

                if (textProperty.HistoryType == TextHistoryType.Transform && textProperty.SourceFmt != null)
                {
                    string transformText = textProperty.SourceFmt.CultureInvariantString?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(transformText))
                    {
                        string propValue = textProperty.SourceFmt.Value?.ToString() ?? "";
                        if (string.IsNullOrEmpty(propValue)) propValue = currentPath;
                        string ns = textProperty.SourceFmt.Namespace?.ToString() ?? "";
                        string transformPath = string.IsNullOrEmpty(ns) ? propValue : $"{ns}::{propValue}";

                        result.Add(new List<string> { transformPath, AssetHelper.ReplaceBreaklines(transformText) });
                        RegisterTextWriteBack(index, textProperty.SourceFmt);
                        index++;
                    }
                    continue;
                }

                string textValue = textProperty.CultureInvariantString?.ToString() ?? "";
                if (!string.IsNullOrEmpty(textValue))
                {
                    string propValue = textProperty.Value?.ToString() ?? "";
                    if (string.IsNullOrEmpty(propValue)) propValue = currentPath;
                    string ns = textProperty.Namespace?.ToString() ?? "";
                    string finalPath = string.IsNullOrEmpty(ns) ? propValue : $"{ns}::{propValue}";

                    result.Add(new List<string> { finalPath, AssetHelper.ReplaceBreaklines(textValue) });
                    RegisterTextWriteBack(index, textProperty);
                    index++;
                }
            }
            else if (property is StructPropertyData structProperty && structProperty.Value != null)
            {
                ProcessProperties(structProperty.Value, currentPath, ref index, result, objectName, strObjectName);
            }
            else if (property is ArrayPropertyData arrayProperty && arrayProperty.Value != null)
            {
                for (int j = 0; j < arrayProperty.Value.Length; j++)
                {
                    var element = arrayProperty.Value[j];
                    if (element == null) continue;

                    string arrayPath = $"{currentPath}[{j}]";
                    string elementName = element.Name?.ToString() ?? "";

                    if (element is StructPropertyData arrayStruct)
                    {
                        ProcessProperties(arrayStruct.Value, arrayPath, ref index, result, objectName, strObjectName);
                    }
                    else if (element is TextPropertyData arrayText)
                    {
                        if (arrayText.HistoryType == TextHistoryType.Transform && arrayText.SourceFmt != null)
                        {
                            string transformText = arrayText.SourceFmt.CultureInvariantString?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(transformText))
                            {
                                string propValue = arrayText.SourceFmt.Value?.ToString() ?? "";
                                if (string.IsNullOrEmpty(propValue)) propValue = arrayPath;
                                string ns = arrayText.SourceFmt.Namespace?.ToString() ?? "";
                                string transformPath = string.IsNullOrEmpty(ns) ? propValue : $"{ns}::{propValue}";

                                result.Add(new List<string> { transformPath, AssetHelper.ReplaceBreaklines(transformText) });
                                RegisterTextWriteBack(index, arrayText.SourceFmt);
                                index++;
                            }
                        }
                        else
                        {
                            string textValue = arrayText.CultureInvariantString?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(textValue))
                            {
                                string propValue = arrayText.Value?.ToString() ?? "";
                                if (string.IsNullOrEmpty(propValue)) propValue = arrayPath;
                                string ns = arrayText.Namespace?.ToString() ?? "";
                                string finalPath = string.IsNullOrEmpty(ns) ? propValue : $"{ns}::{propValue}";

                                result.Add(new List<string> { finalPath, AssetHelper.ReplaceBreaklines(textValue) });
                                RegisterTextWriteBack(index, arrayText);
                                index++;
                            }
                        }
                    }
                    else if (element is StrPropertyData arrayStr)
                    {
                        bool isOptionsArray = propertyName == "Options";
                        if (!isOptionsArray && !_targetStrFields.Contains(elementName)) continue;

                        string text = arrayStr.Value?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(text))
                        {
                            string finalPath = string.IsNullOrEmpty(strObjectName)
                                ? arrayPath
                                : isOptionsArray
                                    ? $"{strObjectName}_Options_{j}"
                                    : $"{strObjectName}_{arrayPath}";

                            result.Add(new List<string> { finalPath, AssetHelper.ReplaceBreaklines(text) });
                            RegisterStrWriteBack(index, arrayStr);
                            index++;
                        }
                    }
                }
            }
        }
    }

    private List<string> GetLanguagesFromNormal()
    {
        var found = new List<string>();
        var seen = new HashSet<string>();

        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;
            CollectTextPropertyPrefixes(normalExport.Data, found, seen);
            if (found.Count > 0) return found;
        }
        return found;
    }
    #endregion

    #region KismetText extraction
    private void ExtractFromKismetText(ref int index, List<List<string>> result)
    {
        var seenKeys = new Dictionary<string, int>();
        var groups = new Dictionary<int, List<EX_StringConst>>();

        foreach (var export in Asset.Exports)
        {
            IEnumerable<KismetExpression> bytecode = null;

            if (export is FunctionExport funcExp)
                bytecode = funcExp.ScriptBytecode;

            else if (export is ClassExport classExp)
                bytecode = classExp.ScriptBytecode;

            if (bytecode == null) continue;

            try
            {
                CollectKismetTexts(bytecode, ref index, result, seenKeys, groups);
            }
            catch (Exception ex)
            {
                string exportName = export.ObjectName?.ToString() ?? "Unknown";
                _extractionWarnings.Add($"Export '{exportName}': не вдалося обробити bytecode (KismetText).\n{ex.Message}");
            }
        }
    }

    private static IEnumerable<KismetExpression> GetKismetChildren(KismetExpression expr)
    {
        if (expr == null) yield break;

        var type = expr.GetType();

        foreach (var field in type.GetFields(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance))
        {
            var val = field.GetValue(expr);
            if (val == null) continue;

            if (val is KismetExpression single)
            {
                yield return single;
            }
            else if (val is KismetExpression[] arr)
            {
                foreach (var e in arr) if (e != null) yield return e;
            }
            else if (val is IEnumerable<KismetExpression> seq)
            {
                foreach (var e in seq) if (e != null) yield return e;
            }
        }
    }

    private void CollectKismetTexts(IEnumerable<KismetExpression> expressions, ref int index, List<List<string>> result, Dictionary<string, int> seenKeys, Dictionary<int, List<EX_StringConst>> groups)
    {
        if (expressions == null) return;

        foreach (var expr in expressions)
        {
            if (expr is EX_TextConst textConst && textConst.Value != null)
            {
                var val = textConst.Value;

                if (val.TextLiteralType == EBlueprintTextLiteralType.LocalizedText)
                {
                    var srcConst = val.LocalizedSource as EX_StringConst;
                    var keyConst = val.LocalizedKey as EX_StringConst;
                    var nsConst = val.LocalizedNamespace as EX_StringConst;

                    string text = srcConst?.Value ?? "";
                    string key = keyConst?.Value ?? "";
                    string ns = nsConst?.Value ?? "";

                    if (!string.IsNullOrEmpty(text) && srcConst != null)
                    {
                        if (seenKeys.TryGetValue(key, out int existingIdx))
                        {
                            if (groups.TryGetValue(existingIdx, out var list))
                                list.Add(srcConst);
                        }
                        else
                        {
                            string path = string.IsNullOrEmpty(ns) ? key : $"{ns}::{key}";
                            result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(text) });

                            var group = new List<EX_StringConst> { srcConst };
                            groups[index] = group;
                            _writeBackMap[index] = value =>
                            {
                                foreach (var strConst in group)
                                    strConst.Value = value;
                            };

                            seenKeys[key] = index;
                            index++;
                        }
                    }
                }
            }

            var children = GetKismetChildren(expr);
            if (children != null)
                CollectKismetTexts(children, ref index, result, seenKeys, groups);
        }
    }
    private void ExtractFromKismetStringConst(ref int index, List<List<string>> result)
    {
        foreach (var export in Asset.Exports)
        {
            IEnumerable<KismetExpression> bytecode = null;

            if (export is FunctionExport funcExp)
                bytecode = funcExp.ScriptBytecode;
            else if (export is ClassExport classExp)
                bytecode = classExp.ScriptBytecode;

            if (bytecode == null) continue;

            string exportName = export.ObjectName?.ToString() ?? "Unknown";

            try
            {
                CollectKismetStringConsts(bytecode, ref index, result, exportName);
            }
            catch (Exception ex)
            {
                _extractionWarnings.Add($"Export '{exportName}': не вдалося обробити bytecode (KismetStringConst).\n{ex.Message}");
            }
        }
    }

    private void CollectKismetStringConsts(IEnumerable<KismetExpression> expressions, ref int index, List<List<string>> result, string exportName, Dictionary<string, int> callCounters = null)
    {
        if (expressions == null) return;

        callCounters ??= new Dictionary<string, int>();

        foreach (var expr in expressions)
        {
            if (expr is EX_LocalVirtualFunction lvf && lvf.Parameters != null)
            {
                string funcName = lvf.VirtualFunctionName?.ToString() ?? "Unknown";
                callCounters.TryGetValue(funcName, out int callIdx);
                callCounters[funcName] = callIdx + 1;

                for (int i = 0; i < lvf.Parameters.Length; i++)
                {
                    if (lvf.Parameters[i] is EX_StringConst sc && !string.IsNullOrEmpty(sc.Value))
                    {
                        string path = $"{exportName}.{funcName}#{callIdx}[{i}]";
                        result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(sc.Value) });
                        RegisterKismetStringConstWriteBack(index, sc);
                        index++;
                    }
                }
            }
            else if (expr is EX_FinalFunction ff && ff.Parameters != null)
            {
                for (int i = 0; i < ff.Parameters.Length; i++)
                {
                    if (ff.Parameters[i] is EX_StringConst sc && !string.IsNullOrEmpty(sc.Value))
                    {
                        string ffKey = $"FinalFunc_{ff.StackNode}";
                        callCounters.TryGetValue(ffKey, out int ffCallIdx);
                        callCounters[ffKey] = ffCallIdx + 1;

                        string path = $"{exportName}.FinalFunc_{ff.StackNode}#{ffCallIdx}[{i}]";
                        result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(sc.Value) });
                        RegisterKismetStringConstWriteBack(index, sc);
                        index++;
                    }
                }
            }

            var children = GetKismetChildren(expr);
            if (children != null)
                CollectKismetStringConsts(children, ref index, result, exportName, callCounters);
        }
    }
    #endregion

    #region LocalizationDataTable case
    private void ExtractFromLocalizationDataTable(ref int index, List<List<string>> result)
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not DataTableExport dataTable) continue;
            if (dataTable.Table?.Data == null) continue;

            foreach (var row in dataTable.Table.Data)
            {
                if (row is not StructPropertyData structProp) continue;
                string rowName = structProp.Name?.ToString() ?? "Unknown";
                if (structProp.Value == null) continue;

                foreach (var prop in structProp.Value)
                {
                    if (prop is not StrPropertyData strProp) continue;

                    if (!MatchesLanguageFilter(strProp.Name?.ToString() ?? "")) continue;

                    string text = strProp.Value?.ToString() ?? "";
                    if (string.IsNullOrEmpty(text)) continue;

                    string path = $"{rowName}.{strProp.Name}";
                    result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(text) });
                    RegisterStrWriteBack(index, strProp);
                    index++;
                }
            }
        }
    }
    #endregion

    #region StringTableBundle case
    private void ExtractFromStringTableBundle(ref int index, List<List<string>> result)
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;

            foreach (var prop in normalExport.Data)
            {
                if (prop is not MapPropertyData outerMap || outerMap.Value == null) continue;

                foreach (var tableKvp in outerMap.Value)
                {
                    if (tableKvp.Value is not StructPropertyData tableStruct) continue;
                    if (tableStruct.Value == null) continue;

                    string tableName = tableKvp.Key?.ToString() ?? "Unknown";

                    foreach (var tableField in tableStruct.Value)
                    {
                        if (tableField is not MapPropertyData entriesMap || entriesMap.Value == null) continue;

                        var firstEntry = entriesMap.Value.FirstOrDefault();
                        if (firstEntry.Key is not IntPropertyData) continue;

                        foreach (var entryKvp in entriesMap.Value)
                        {
                            if (entryKvp.Value is not StructPropertyData entryStruct) continue;
                            if (entryStruct.Value == null) continue;

                            int entryId = entryKvp.Key is IntPropertyData idKey ? idKey.Value : -1;

                            foreach (var entryField in entryStruct.Value)
                            {
                                if (entryField is not StrPropertyData strProp) continue;

                                string text = strProp.Value?.ToString() ?? "";
                                if (string.IsNullOrEmpty(text)) continue;

                                string fieldName = strProp.Name?.ToString() ?? "";
                                string path = $"{tableName}[{entryId}].{fieldName}";
                                result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(text) });
                                RegisterStrWriteBack(index, strProp);
                                index++;
                            }
                        }
                    }
                }
            }
        }
    }
    #endregion

    #region DialogueAsset case
    private void ExtractFromDialogueAsset(ref int index, List<List<string>> result)
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;

            string textId = null;
            MapPropertyData localizedTextMap = null;

            foreach (var prop in normalExport.Data)
            {
                if (prop is NamePropertyData nameProp && nameProp.Name?.ToString() == "TextId")
                    textId = nameProp.Value?.ToString() ?? "";
                else if (prop is MapPropertyData mapProp && mapProp.Name?.ToString() == "LocalizedText")
                    localizedTextMap = mapProp;
            }

            if (textId == null || localizedTextMap?.Value == null) continue;

            string exportName = normalExport.ObjectName?.ToString() ?? "Unknown";

            foreach (var kvp in localizedTextMap.Value)
            {
                if (kvp.Key is not StrPropertyData langKey) continue;
                if (kvp.Value is not StrPropertyData textValue) continue;

                string lang = langKey.Value?.ToString() ?? "";

                if (!MatchesLanguageFilter(lang)) continue;

                string text = textValue.Value?.ToString() ?? "";
                if (string.IsNullOrEmpty(text)) continue;

                string path = _selectedLanguage == null
                    ? $"{exportName}.{textId}[{lang}]"
                    : $"{exportName}.{textId}";

                result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(text) });
                RegisterStrWriteBack(index, textValue);
                index++;
            }
        }
    }

    private List<string> GetLanguagesFromDialogueAsset()
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;

            foreach (var prop in normalExport.Data)
            {
                if (prop is not MapPropertyData mapProp) continue;
                if (mapProp.Name?.ToString() != "LocalizedText") continue;
                if (mapProp.Value == null) continue;

                return mapProp.Value
                    .Select(kvp => (kvp.Key as StrPropertyData)?.Value?.ToString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
        }
        return new List<string>();
    }
    #endregion

    #region DialogueActorAsset case
    private void ExtractFromDialogueActorAsset(ref int index, List<List<string>> result)
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;

            string exportName = normalExport.ObjectName?.ToString() ?? "Unknown";

            foreach (var prop in normalExport.Data)
            {
                if (prop is not NamePropertyData nameProp) continue;

                string fieldName = nameProp.Name?.ToString() ?? "";
                if (fieldName != "Name" && fieldName != "Nick") continue;

                if (!MatchesLanguageFilter(fieldName)) continue;

                string text = nameProp.Value?.ToString() ?? "";
                if (string.IsNullOrEmpty(text)) continue;

                string path = $"{exportName}.{fieldName}";
                result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(text) });
                RegisterNameWriteBack(index, nameProp);
                index++;
            }
        }
    }
    #endregion

    #region StringTable case
    private void ExtractFromStringTable(ref int index, List<List<string>> result)
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not StringTableExport stringTableExport) continue;
            if (stringTableExport.Table == null) continue;

            string namespaceName = stringTableExport.Table.TableNamespace?.ToString() ?? "";

            foreach (var kvp in stringTableExport.Table)
            {
                string key = kvp.Key?.ToString() ?? "";
                string text = kvp.Value?.ToString() ?? "";

                if (string.IsNullOrEmpty(text)) continue;

                string path = string.IsNullOrEmpty(namespaceName)
                    ? key
                    : $"{namespaceName}::{key}";

                result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(text) });
                RegisterStringTableWriteBack(index, stringTableExport, kvp.Key);
                index++;
            }
        }
    }
    #endregion

    #region StrArrayDataTable case
    private void ExtractFromStrArrayDataTable(ref int index, List<List<string>> result)
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not DataTableExport dataTable) continue;
            if (dataTable.Table?.Data == null) continue;

            foreach (var row in dataTable.Table.Data)
            {
                if (row is not StructPropertyData structProp) continue;

                string rowName = structProp.Name?.ToString() ?? "Unknown";
                if (structProp.Value == null) continue;

                foreach (var prop in structProp.Value)
                {
                    if (prop is not ArrayPropertyData arrayProp) continue;
                    if (arrayProp.ArrayType?.ToString() != "StrProperty") continue;
                    if (arrayProp.Value == null || arrayProp.Value.Length == 0) continue;

                    string fieldName = arrayProp.Name?.ToString() ?? "Field";

                    for (int i = 0; i < arrayProp.Value.Length; i++)
                    {
                        if (arrayProp.Value[i] is not StrPropertyData strProp) continue;

                        string text = strProp.Value?.ToString() ?? "";
                        if (string.IsNullOrEmpty(text)) continue;

                        string path = arrayProp.Value.Length > 1
                            ? $"{rowName}.{fieldName}[{i}]"
                            : $"{rowName}.{fieldName}";

                        result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(text) });
                        RegisterStrWriteBack(index, strProp);
                        index++;
                    }
                }
            }
        }
    }
    #endregion

    #region NameDataTable case
    private void ExtractFromNameDataTable(ref int index, List<List<string>> result)
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not DataTableExport dataTable) continue;
            if (dataTable.Table?.Data == null) continue;

            foreach (var row in dataTable.Table.Data)
            {
                if (row is not StructPropertyData structProp) continue;

                string rowName = structProp.Name?.ToString() ?? "Unknown";
                if (structProp.Value == null) continue;

                string genderSegment = "";
                foreach (var p in structProp.Value)
                {
                    if (p is EnumPropertyData enumProp
                        && enumProp.Name?.ToString() == "Type"
                        && enumProp.EnumType?.ToString() == "EGENDER_TYPE")
                    {
                        string gv = enumProp.Value?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(gv))
                            genderSegment = gv;
                        break;
                    }
                }

                foreach (var prop in structProp.Value)
                {
                    if (prop is not NamePropertyData nameProp) continue;

                    string fieldName = nameProp.Name?.ToString() ?? "";

                    if (fieldName == "Name") continue;

                    if (!MatchesLanguageFilter(fieldName)) continue;

                    string text = nameProp.Value?.ToString() ?? "";

                    if (string.IsNullOrEmpty(text)) continue;
                    string displayText = text == "None" ? "<none>" : text;

                    string path = string.IsNullOrEmpty(genderSegment)
                        ? $"{rowName}.{fieldName}"
                        : $"{rowName}.{genderSegment}.{fieldName}";

                    result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(displayText) });
                    RegisterNameWriteBack(index, nameProp);
                    index++;
                }
            }
        }
    }
    #endregion

    #region TextDataMap case
    private void ExtractFromTextDataMap(ref int index, List<List<string>> result)
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;

            string exportName = normalExport.ObjectName?.ToString() ?? "Unknown";

            foreach (var prop in normalExport.Data)
            {
                if (prop is not MapPropertyData mapProp || mapProp.Value == null) continue;

                foreach (var kvp in mapProp.Value)
                {
                    if (kvp.Key is not NamePropertyData nameKey) continue;
                    if (kvp.Value is not StructPropertyData structVal) continue;
                    if (structVal.Value == null) continue;

                    string entryKey = nameKey.Value?.ToString() ?? "";

                    string genderSegment = "";
                    var genderEnum = structVal.Value
                        .OfType<EnumPropertyData>()
                        .FirstOrDefault(e => e.Name?.ToString() == "Gender"
                                          && e.EnumType?.ToString() == "ETextGender");
                    if (genderEnum != null)
                    {
                        string gv = genderEnum.Value?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(gv))
                            genderSegment = gv;
                    }

                    var strFields = structVal.Value
                        .OfType<StrPropertyData>()
                        .Where(sp => !string.IsNullOrEmpty(sp.Value?.ToString()))
                        .ToList();

                    if (strFields.Count == 0) continue;

                    bool multipleFields = strFields.Count > 1;

                    foreach (var strProp in strFields)
                    {
                        string text = strProp.Value.ToString();
                        string path;
                        if (multipleFields)
                        {
                            path = string.IsNullOrEmpty(genderSegment)
                                ? $"{exportName}.{entryKey}.{strProp.Name}"
                                : $"{exportName}.{entryKey}.{genderSegment}.{strProp.Name}";
                        }
                        else
                        {
                            path = string.IsNullOrEmpty(genderSegment)
                                ? $"{exportName}.{entryKey}"
                                : $"{exportName}.{entryKey}.{genderSegment}";
                        }

                        result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(text) });
                        RegisterStrWriteBack(index, strProp);
                        index++;
                    }
                }
            }
        }
    }
    #endregion

    #region LocalizationGroupAsset case
    private void ExtractFromLocalizationGroupAsset(ref int index, List<List<string>> result)
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;

            foreach (var prop in normalExport.Data)
            {
                if (prop is not ArrayPropertyData arrayProp) continue;
                if (arrayProp.ArrayType?.ToString() != "StructProperty") continue;
                if (arrayProp.Value == null) continue;

                foreach (var element in arrayProp.Value)
                {
                    if (element is not StructPropertyData groupStruct) continue;
                    if (groupStruct.Value == null) continue;

                    MapPropertyData textsMap = null;

                    foreach (var field in groupStruct.Value)
                    {
                        if (field is MapPropertyData mapField && textsMap == null)
                            textsMap = mapField;
                    }

                    if (textsMap?.Value == null) continue;

                    foreach (var kvp in textsMap.Value)
                    {
                        if (kvp.Key is not NamePropertyData entryKey) continue;
                        if (kvp.Value is not TextPropertyData textProp) continue;

                        string text = textProp.CultureInvariantString?.ToString() ?? "";
                        if (string.IsNullOrEmpty(text)) continue;

                        string entryName = entryKey.Value?.ToString() ?? "";
                        if (string.IsNullOrEmpty(entryName)) continue;

                        string ns = textProp.Namespace?.ToString() ?? "";

                        string path = string.IsNullOrEmpty(ns)
                            ? entryName
                            : $"{ns}::{entryName}";

                        result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(text) });
                        RegisterTextWriteBack(index, textProp);
                        index++;
                    }
                }
            }
        }
    }
    #endregion

    #region DialogueChapterAsset case
    private void ExtractFromDialogueChapterAsset(ref int index, List<List<string>> result)
    {
        var localizedMap = new Dictionary<string, StrPropertyData>();
        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport ldExport) continue;
            if (ldExport.Data == null) continue;
            foreach (var prop in ldExport.Data)
            {
                if (prop is not MapPropertyData mapProp) continue;
                if (mapProp.Name?.ToString() != "KeyValues") continue;
                if (mapProp.Value == null) continue;
                foreach (var kvp in mapProp.Value)
                {
                    if (kvp.Key is not NamePropertyData keyProp) continue;
                    if (kvp.Value is not StrPropertyData valProp) continue;
                    string textId = keyProp.Value?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(textId))
                        localizedMap[textId] = valProp;
                }
            }
        }

        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            string exportName = normalExport.ObjectName?.ToString() ?? "";
            if (!exportName.StartsWith("DialogueLine_", StringComparison.Ordinal)) continue;
            if (normalExport.Data == null) continue;

            string textId = null;
            StrPropertyData baseTextProp = null;

            foreach (var prop in normalExport.Data)
            {
                if (prop is NamePropertyData nameProp && nameProp.Name?.ToString() == "TextId")
                    textId = nameProp.Value?.ToString() ?? "";
                else if (prop is StrPropertyData strProp && strProp.Name?.ToString() == "Text")
                    baseTextProp = strProp;
            }

            if (string.IsNullOrEmpty(textId)) continue;

            string path = $"{exportName}.{textId}";

            if (localizedMap.TryGetValue(textId, out var localizedProp))
            {
                string text = localizedProp.Value?.ToString() ?? "";
                if (string.IsNullOrEmpty(text)) continue;
                result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(text) });
                RegisterStrWriteBack(index, localizedProp);
                index++;
            }
            else if (baseTextProp != null)
            {
                string text = baseTextProp.Value?.ToString() ?? "";
                if (string.IsNullOrEmpty(text)) continue;
                result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(text) });
                RegisterStrWriteBack(index, baseTextProp);
                index++;
            }
        }

        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            string exportName = normalExport.ObjectName?.ToString() ?? "";
            if (!exportName.StartsWith("DialogueConversation_", StringComparison.Ordinal)) continue;
            if (normalExport.Data == null) continue;

            NamePropertyData nameProp = null;
            foreach (var prop in normalExport.Data)
            {
                if (prop is NamePropertyData np && np.Name?.ToString() == "Name")
                {
                    nameProp = np;
                    break;
                }
            }

            if (nameProp == null) continue;
            string nameValue = nameProp.Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(nameValue) || nameValue == "None") continue;

            string path = $"{exportName}.Name";
            result.Add(new List<string> { path, nameValue });
            RegisterNameWriteBack(index, nameProp);
            index++;
        }

        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            string exportName = normalExport.ObjectName?.ToString() ?? "";
            if (!exportName.StartsWith("DialogueScene_", StringComparison.Ordinal)) continue;
            if (normalExport.Data == null) continue;

            NamePropertyData nameProp = null;
            foreach (var prop in normalExport.Data)
            {
                if (prop is NamePropertyData np && np.Name?.ToString() == "Name")
                {
                    nameProp = np;
                    break;
                }
            }

            if (nameProp == null) continue;
            string nameValue = nameProp.Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(nameValue) || nameValue == "None") continue;

            string path = $"{exportName}.Name";
            result.Add(new List<string> { path, nameValue });
            RegisterNameWriteBack(index, nameProp);
            index++;
        }
    }
    #endregion

    #region SevLocalizationDataTable case
    private void ExtractFromSevLocalizationDataTable(ref int index, List<List<string>> result)
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not DataTableExport dataTable) continue;
            if (dataTable.Table?.Data == null) continue;

            foreach (var row in dataTable.Table.Data)
            {
                if (row is not StructPropertyData structProp) continue;
                string rowName = structProp.Name?.ToString() ?? "Unknown";
                if (structProp.Value == null) continue;

                foreach (var prop in structProp.Value)
                {
                    if (prop is not StructPropertyData inner || inner.Value == null) continue;
                    if (!inner.Value.All(p => p is StrPropertyData)) continue;

                    string innerName = inner.Name?.ToString() ?? "";

                    foreach (var langProp in inner.Value.OfType<StrPropertyData>())
                    {
                        if (!MatchesLanguageFilter(langProp.Name?.ToString() ?? "")) continue;

                        string text = langProp.Value?.ToString() ?? "";
                        if (string.IsNullOrEmpty(text)) continue;

                        string langCode = langProp.Name?.ToString() ?? "";
                        string path = string.IsNullOrEmpty(innerName)
                            ? $"{rowName}.{langCode}"
                            : $"{rowName}.{innerName}.{langCode}";

                        result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(text) });
                        RegisterStrWriteBack(index, langProp);
                        index++;
                    }
                }
            }
        }
    }

    private List<string> GetLanguagesFromSevLocalizationDataTable()
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not DataTableExport dataTable) continue;
            var firstRow = dataTable.Table?.Data?.FirstOrDefault() as StructPropertyData;
            if (firstRow?.Value == null) continue;

            foreach (var prop in firstRow.Value)
            {
                if (prop is not StructPropertyData inner || inner.Value == null) continue;
                if (!inner.Value.All(p => p is StrPropertyData)) continue;

                return inner.Value
                    .OfType<StrPropertyData>()
                    .Select(p => p.Name?.ToString() ?? "")
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
            }
        }
        return new List<string>();
    }
    #endregion

    #region LocaleMapAsset case
    private void ExtractFromLocaleMapAsset(ref int index, List<List<string>> result)
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;

            foreach (var prop in normalExport.Data)
            {
                if (prop is not MapPropertyData mapProp || mapProp.Value == null) continue;

                foreach (var kvp in mapProp.Value)
                {
                    if (kvp.Key is not StrPropertyData keyProp) continue;
                    if (kvp.Value is not StructPropertyData valuStruct) continue;
                    if (valuStruct.Value == null) continue;

                    string entryKey = keyProp.Value?.ToString() ?? "";

                    var textProp = valuStruct.Value.OfType<StrPropertyData>()
                        .FirstOrDefault(p => p.Name?.ToString() == "Text");
                    if (textProp == null) continue;

                    string text = textProp.Value?.ToString() ?? "";
                    if (string.IsNullOrEmpty(text)) continue;

                    var displayNameProp = valuStruct.Value.OfType<StrPropertyData>()
                        .FirstOrDefault(p => p.Name?.ToString() == "DisplayName");
                    string displayName = displayNameProp?.Value?.ToString() ?? "";
                    if (text == displayName) continue;

                    result.Add(new List<string> { entryKey, AssetHelper.ReplaceBreaklines(text) });
                    RegisterStrWriteBack(index, textProp);
                    index++;
                }
            }
        }
    }
    #endregion

    #region StrNameDataTable case
    private void ExtractFromStrNameDataTable(ref int index, List<List<string>> result)
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not DataTableExport dataTable) continue;
            if (dataTable.Table?.Data == null) continue;

            foreach (var row in dataTable.Table.Data)
            {
                if (row is not StructPropertyData structProp) continue;

                string rowName = structProp.Name?.ToString() ?? "Unknown";
                if (structProp.Value == null) continue;

                foreach (var prop in structProp.Value)
                {
                    if (prop is not StrPropertyData strProp) continue;

                    string fieldName = strProp.Name?.ToString() ?? "";
                    if (!MatchesLanguageFilter(fieldName)) continue;

                    string text = strProp.Value?.ToString() ?? "";
                    if (string.IsNullOrEmpty(text)) continue;

                    string path = $"{rowName}.{fieldName}";
                    result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(text) });
                    RegisterStrWriteBack(index, strProp);
                    index++;
                }
            }
        }
    }
    #endregion

    #region FrogVideoSubtitleAsset case
    private void ExtractFromFrogVideoSubtitleAsset(ref int index, List<List<string>> result)
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;

            foreach (var prop in normalExport.Data)
            {
                if (prop is not MapPropertyData mapProp) continue;
                if (mapProp.Name?.ToString() != "Subtitles") continue;
                if (mapProp.Value == null) continue;

                foreach (var kvp in mapProp.Value)
                {
                    if (kvp.Key is not StrPropertyData langKey) continue;
                    if (kvp.Value is not StructPropertyData clipHolder) continue;
                    if (clipHolder.Value == null) continue;

                    string lang = langKey.Value?.ToString() ?? "";

                    if (!MatchesLanguageFilter(lang)) continue;

                    var clipsArray = clipHolder.Value
                        .OfType<ArrayPropertyData>()
                        .FirstOrDefault(a => a.Name?.ToString() == "LocalizedSubtitleClips");
                    if (clipsArray?.Value == null) continue;

                    foreach (var element in clipsArray.Value)
                    {
                        if (element is not StructPropertyData clipStruct || clipStruct.Value == null) continue;

                        var textProp = clipStruct.Value
                            .OfType<TextPropertyData>()
                            .FirstOrDefault(p => p.Name?.ToString() == "SubtitleText");
                        if (textProp == null) continue;

                        string text = textProp.CultureInvariantString?.ToString() ?? "";
                        if (string.IsNullOrEmpty(text)) continue;

                        string id = textProp.Value?.ToString() ?? "";
                        string path = _selectedLanguage == null ? $"{id}_{lang}" : id;

                        result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(text) });
                        RegisterTextWriteBack(index, textProp);
                        index++;
                    }
                }
            }
        }
    }

    private List<string> GetLanguagesFromFrogVideoSubtitleAsset()
    {
        foreach (var export in Asset.Exports)
        {
            if (export is not NormalExport normalExport) continue;
            if (normalExport.Data == null) continue;

            foreach (var prop in normalExport.Data)
            {
                if (prop is not MapPropertyData mapProp) continue;
                if (mapProp.Name?.ToString() != "Subtitles") continue;
                if (mapProp.Value == null) continue;

                return mapProp.Value
                    .Select(kvp => (kvp.Key as StrPropertyData)?.Value?.ToString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
        }
        return new List<string>();
    }
    #endregion

    public void ImportTexts(List<List<string>> strings)
    {
        foreach (var stringData in strings)
        {
            if (stringData.Count < 3) continue;

            if (!int.TryParse(stringData[0], out int index)) continue;

            string newText = AssetHelper.ReplaceBreaklines(stringData[2], Back: true);

            if (_writeBackMap.TryGetValue(index, out var writeBack))
            {
                writeBack(newText);
            }
            else if (_assetType == "J5BinderAsset")
            {
                _j5Parser.ApplyText(index, newText);
            }
            else if (_assetType == "OctopathBinaryAsset")
            {
                _octopathParser.ApplyText(index, newText);
            }
        }

        if (_assetType == "J5BinderAsset")
        {
            _j5Parser.Rebuild();
            var j5Export = Asset.Exports.OfType<NormalExport>().First(J5BinderAssetParser.IsJ5BinderAsset);
            j5Export.SerialSize = j5Export.Extras.Length;
        }
    }

    public void SaveFile(string filePath)
    {
        if (Asset == null) return;

        try
        {
            if (_assetType == "J5BinderAsset")
            {
                var j5Export = Asset.Exports.OfType<NormalExport>().First(J5BinderAssetParser.IsJ5BinderAsset);
                j5Export.SerialSize = j5Export.Extras.Length;
                Asset.Write(filePath);
                return;
            }

            if (_assetType == "OctopathBinaryAsset")
            {
                _octopathParser.Rebuild();
                var octExport = Asset.Exports.OfType<NormalExport>()
                    .First(OctopathBinaryAssetParser.IsOctopathBinaryAsset);
                octExport.SerialSize = octExport.Extras.Length;
                Asset.Write(filePath);
                return;
            }

            Asset.Write(filePath);
        }
        catch (Exception ex)
        {
            throw new Exception($"Не вдалося зберегти:\n{ex.Message}", ex);
        }
    }

    #region Warnings and Statistics
    public List<string> GetWarnings()
    {
        var warnings = new List<string>();

        if (Asset?.Exports == null)
            return warnings;

        string fileName = Path.GetFileName(FilePath);

        if (_extractionWarnings.Count > 0)
            warnings.AddRange(_extractionWarnings);

        if (Asset.HasUnversionedProperties && Asset.Mappings == null)
        {
            warnings.Add($"Файл {fileName}:\nВиявлено не версійовані властивості (UE5+).\nДля коректного парсингу потрібен usmap/jmap/jmap.gz. Без нього більшість даних будуть втрачені.");
        }

        if (_hasNameMapDuplicates)
        {
            warnings.Add($"Файл {fileName}:\nВиявлено дублікати в NameMap.\nЦе не впливає на редагування, але може свідчити про пошкодження файлу.");
        }

        if (_rawExportCount > 0)
        {
            warnings.Add($"Файл {fileName}:\nНе вдалося розібрати {_rawExportCount} {GetExportsWord(_rawExportCount)}.\nВони збережуться коректно, але їх зміст буде недоступний.");
        }

        if (_unknownTypes.Count > 0)
        {
            string msg = $"Файл {fileName}:\nВиявлено {_unknownTypes.Count} {GetPropertiesWord(_unknownTypes.Count)}: {string.Join(", ", _unknownTypes)}.";
            if (_failedBinaryEquality)
                msg += " Це може вплинути на здатність коректно зберігати файл через бінарну нерівність.";
            else
                msg += " Але файл збережеться коректно.";
            warnings.Add(msg);
        }

        if (_rawStructTypes.Count > 0)
        {
            string msg = $"Файл {fileName}:\nВиявлено {_numRawStructs} {GetRawStructsWord(_numRawStructs)} з типів: {string.Join(", ", _rawStructTypes)}.";
            if (_failedBinaryEquality)
                msg += " Це може вплинути на здатність коректно зберігати файл через бінарну нерівність.";
            else
                msg += " Але файл збережеться коректно.";
            warnings.Add(msg);
        }

        return warnings;
    }

    private static string GetExportsWord(int count)
    {
        int abs = Math.Abs(count), mod10 = abs % 10, mod100 = abs % 100;
        if (mod100 >= 11 && mod100 <= 19) return "експортів";
        return mod10 switch { 1 => "експорт", 2 or 3 or 4 => "експорти", _ => "експортів" };
    }

    private static string GetPropertiesWord(int count)
    {
        int abs = Math.Abs(count), mod10 = abs % 10, mod100 = abs % 100;
        if (mod100 >= 11 && mod100 <= 19) return "невідомих типів властивостей";
        return mod10 switch { 1 => "невідомий тип властивості", 2 or 3 or 4 => "невідомих типи властивостей", _ => "невідомих типів властивостей" };
    }

    private static string GetRawStructsWord(int count)
    {
        int abs = Math.Abs(count), mod10 = abs % 10, mod100 = abs % 100;
        if (mod100 >= 11 && mod100 <= 19) return "сирих властивостей структури";
        return mod10 switch { 1 => "сиру властивість структури", 2 or 3 or 4 => "сирі властивості структури", _ => "сирих властивостей структури" };
    }

    private void CollectPropertiesStatistics()
    {
        _unknownTypes.Clear();
        _rawStructTypes.Clear();
        _numRawStructs = 0;
        _rawExportCount = 0;
        _hasNameMapDuplicates = false;

        if (Asset?.Exports == null) return;

        _rawExportCount = Asset.Exports.Count(e => e is RawExport);

        var nameMapRefs = new HashSet<string>();
        foreach (FString name in Asset.GetNameMapIndexList())
        {
            if (nameMapRefs.Contains(name.Value))
            {
                _hasNameMapDuplicates = true;
                break;
            }
            nameMapRefs.Add(name.Value);
        }

        foreach (var export in Asset.Exports)
        {
            if (export is NormalExport normalExport && normalExport.Data != null)
            {
                CollectUnknownPropertiesRecursive(normalExport.Data);
            }
        }

        _failedBinaryEquality = !string.IsNullOrEmpty(FilePath) &&
                                !FilePath.EndsWith(".json") &&
                                !Asset.VerifyBinaryEquality();
    }

    private void CollectUnknownPropertiesRecursive(List<PropertyData> properties)
    {
        if (properties == null) return;

        foreach (var prop in properties)
        {
            if (prop == null) continue;

            if (prop is UnknownPropertyData unknown)
            {
                string type = unknown.SerializingPropertyType?.Value;
                if (!string.IsNullOrEmpty(type))
                    _unknownTypes.Add(type);
            }
            else if (prop is RawStructPropertyData rawStruct)
            {
                _numRawStructs++;
                string type = rawStruct.StructType?.ToString();
                if (!string.IsNullOrEmpty(type))
                    _rawStructTypes.Add(type);
            }
            else if (prop is StructPropertyData structProp && structProp.Value != null)
            {
                CollectUnknownPropertiesRecursive(structProp.Value);
            }
            else if (prop is ArrayPropertyData arrayProp && arrayProp.Value != null)
            {
                foreach (var element in arrayProp.Value)
                {
                    if (element is StructPropertyData arrStruct && arrStruct.Value != null)
                        CollectUnknownPropertiesRecursive(arrStruct.Value);
                    else if (element is UnknownPropertyData || element is RawStructPropertyData)
                        CollectUnknownPropertiesRecursive(new List<PropertyData> { element });
                }
            }
            else if (prop is MapPropertyData mapProp && mapProp.Value != null)
            {
                foreach (var kvp in mapProp.Value)
                {
                    if (kvp.Key != null)
                        CollectUnknownPropertiesRecursive(new List<PropertyData> { kvp.Key });
                    if (kvp.Value != null)
                        CollectUnknownPropertiesRecursive(new List<PropertyData> { kvp.Value });
                }
            }
        }
    }
    #endregion

}