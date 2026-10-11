using System.Text;
using UELT.Cli.Engine;
using UELT.Cli.Infra;
using UELT.Core;
using UELT.Core.locres;
using UELT.Hikaro;

namespace UELT.Cli.Commands;

/// <summary>Locres-only operations from the GUI menus "Файл" and "Операції для locres".</summary>
internal static class LocresCommands
{
    private static readonly Opt[] LocresOpen = { new("--cv2"), new("--cv2-demo") };
    private static readonly Opt[] LocresSave = { new("--cv2"), new("--cv2-demo"), new("--status"), Common.Output, Common.Overwrite, Common.InPlace };
    private static readonly string[] LocresExt = { ".locres" };

    public static readonly Command CreateLocres = new("create-locres", "створити locres з TXT (ID=текст)",
        new[] { Common.Output, Common.Overwrite, new Opt("--version", true), new Opt("--each") }, RunCreate);

    public static readonly Command MergeLocres = new("merge-locres", "об’єднати декілька locres (додати нові рядки)",
        LocresSave, RunMerge);

    public static readonly Command AddRows = new("add-rows", "імпортувати нові рядки з TXT у locres",
        LocresSave, RunAddRows);

    public static readonly Command RemoveRows = new("remove-rows", "видалити рядки з locres за ID",
        Common.Concat(LocresSave, new[] { new Opt("--ids-file", true, true) }), RunRemoveRows);

    public static readonly Command ImportHashes = new("import-hashes", "імпортувати хеші тексту з locres або TXT",
        LocresSave, RunImportHashes);

    public static readonly Command CompareLocres = new("compare-locres", "порівняти два locres і експортувати відмінності",
        Common.Concat(LocresOpen, new[] { Common.Output, Common.Overwrite, new Opt("--list"), Common.Json }), RunCompare);

    private static AssetOptions LocresOptions(ParsedArgs a) => AssetOptions.From(a);

    /// <summary>Accepts 0..4 and readable names. Default: Optimized (v2), the LocresFile default.</summary>
    public static LocresVersion ParseVersion(string s)
    {
        if (s == null) return LocresVersion.Optimized;
        return s.Trim().ToLowerInvariant() switch
        {
            "0" or "legacy" => LocresVersion.Legacy,
            "1" or "compact" => LocresVersion.Compact,
            "2" or "optimized" or "crc32" => LocresVersion.Optimized,
            "3" or "cityhash" or "cityhash64" => LocresVersion.Optimized_CityHash64_UTF16,
            "4" or "externid" or "cityhash64-externid" or "stellarblade" => LocresVersion.Optimized_CityHash64_ExternID_UTF16,
            _ => throw new UsageException($"--version: очікується 0..4 (legacy, compact, optimized, cityhash, externid), отримано «{s}».")
        };
    }

    private static (string Ns, string Key) SplitId(string id)
    {
        var parts = id.Split(new[] { "::" }, 2, StringSplitOptions.None);
        return parts.Length == 2 ? (parts[0], parts[1]) : ("", parts[0]);
    }

    private static List<string> CollectTxt(IEnumerable<string> paths)
    {
        var result = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                result.AddRange(Directory.GetFiles(p, "*.txt", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            else if (File.Exists(p))
                result.Add(p);
            else
                throw new CliException($"Файл або теку не знайдено: {p}");
        }
        if (result.Count == 0) throw new CliException("Не знайдено жодного TXT файлу.");
        return result;
    }

    // ---------------------------------------------------------------- create-locres

    /// <summary>CreateLocresWindow.CreateButton_Click: first occurrence of an ID wins.</summary>
    private static LocresFile BuildLocres(IEnumerable<string> txtFiles, LocresVersion version, out int duplicatesCount)
    {
        var locres = new LocresFile(version);
        var seenKeys = new Dictionary<string, string>();
        var duplicates = new List<string>();

        foreach (var path in txtFiles)
        {
            string fileName = Path.GetFileName(path);
            foreach (var (id, text) in TxtHelper.ParseIDFormat(path))
            {
                var (ns, key) = SplitId(id);
                if (string.IsNullOrWhiteSpace(key)) continue;

                string fullKey = $"{ns}::{key}";
                if (seenKeys.TryGetValue(fullKey, out string firstFile))
                    duplicates.Add($"{id} ({fileName} — уже є з {firstFile})");
                else
                {
                    seenKeys[fullKey] = fileName;
                    locres.AddString(ns, key, text);
                }
            }
        }

        duplicatesCount = duplicates.Count;
        if (duplicates.Count > 0)
        {
            Log.Warn($"Знайдено {duplicates.Count} {PluralizationHelper.GetDuplicatesWord(duplicates.Count)} ID — залишено перший:");
            foreach (var d in duplicates.Take(20)) Log.Raw("  " + d);
            if (duplicates.Count > 20) Log.Raw($"  ... і ще {duplicates.Count - 20}");
        }

        if (locres.Sum(n => n.Count) == 0)
            throw new CliException("Файли не містять жодного правильного рядка.");

        return locres;
    }

    private static int RunCreate(ParsedArgs a)
    {
        a.RequirePositionals(1, -1, "TXT файл(и)");
        var version = ParseVersion(a.Get("--version"));
        bool overwrite = a.Has("--overwrite");
        string output = a.Get("--output");
        Infra.Files.RejectStdout(output);
        var txts = CollectTxt(a.Positionals);

        if (!a.Has("--each"))
        {
            string outPath = output == null
                ? Path.ChangeExtension(txts[0], ".locres")
                : Infra.Files.LooksLikeDirectory(output) ? Path.Combine(output, Path.GetFileNameWithoutExtension(txts[0]) + ".locres") : output;
            Infra.Files.EnsureWritable(outPath, overwrite);
            var locres = BuildLocres(txts, version, out _);
            Infra.Files.EnsureParentDir(outPath);
            locres.SaveFile(outPath);
            Log.Out($"OK {string.Join(", ", txts)} -> {outPath} ({Common.RowsW(locres.Sum(n => n.Count))}, {InfoCommand.LocresVersionName(version)})");
            return ExitCodes.Ok;
        }

        // --each: one locres per TXT, mirroring folders under -o.
        var (inputs, batch) = Infra.Files.CollectInputs(a.Positionals, new[] { ".txt" });
        var namer = new ExportNamer(output, batch, flat: false, overwrite);
        var runner = new BatchRunner(batch);
        runner.Run(inputs, f =>
        {
            string outPath = namer.Resolve(f, ".locres");
            Infra.Files.EnsureWritable(outPath, overwrite);
            var locres = BuildLocres(new[] { f.FullPath }, version, out _);
            Infra.Files.EnsureParentDir(outPath);
            locres.SaveFile(outPath);
            Common.OkLine(f, outPath, locres.Sum(n => n.Count));
        });
        Common.BatchSummary(runner, verb: "Створено");
        return runner.ExitCode();
    }

    // ---------------------------------------------------------------- shared runner for target edits

    private static int RunOnTargets(ParsedArgs a, string what, Func<AssetSession, InputFile, string> edit)
    {
        var options = LocresOptions(a);
        bool overwrite = a.Has("--overwrite");
        var (files, batch) = Infra.Files.CollectInputs(a.Positionals.Take(1).ToList(), LocresExt, a.Get("--output"));
        var namer = new GameOutputNamer(a.Get("--output"), a.Has("--in-place"), batch);
        var runner = new BatchRunner(batch);

        runner.Run(files, f =>
        {
            string outPath = namer.Resolve(f);
            namer.CheckWritable(f, outPath, overwrite, options.Status);
            var s = AssetSession.Open(f, options, batch);
            string summary = edit(s, f);
            s.Save(outPath);
            Log.Out($"OK {f.Display} -> {outPath} ({summary})");
        });

        Common.BatchSummary(runner);
        return runner.ExitCode();
    }

    /// <summary>
    /// Source files for one target of merge-locres / add-rows / import-hashes. Files given directly apply to
    /// every target. A source folder: for a single target file every matching file in it is used (GUI
    /// multi-select); for targets collected from a folder the source folder is paired with the target tree like
    /// pack does (same relative path / name, SourceSpec.Resolve), so rows never leak between unrelated files.
    /// </summary>
    private sealed class TargetSources
    {
        private readonly List<(string Path, SourceSpec Folder)> _args = new();
        private readonly string[] _exts;

        public TargetSources(IEnumerable<string> args, string[] exts)
        {
            _exts = exts;
            foreach (var p in args)
            {
                if (Directory.Exists(p))
                {
                    if (!Directory.EnumerateFiles(p, "*.*", SearchOption.AllDirectories).Any(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant())))
                        throw new CliException($"У теці «{p}» не знайдено файлів {string.Join(", ", exts)}.");
                    _args.Add((p, new SourceSpec(p, null, "id")));
                }
                else if (File.Exists(p))
                {
                    if (!exts.Contains(Path.GetExtension(p).ToLowerInvariant()))
                        throw new CliException($"Непідтримуваний тип файлу: {p} (підтримуються {string.Join(", ", exts)}).");
                    _args.Add((p, null));
                }
                else throw new CliException($"Файл або теку не знайдено: {p}");
            }
        }

        public List<string> For(InputFile target)
        {
            var result = new List<string>();
            bool folderUnmatched = false;
            foreach (var (path, folder) in _args)
            {
                if (folder == null)
                    result.Add(path);
                else if (!target.FromFolder)
                    result.AddRange(Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                        .Where(f => _exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                else
                {
                    string match = folder.Resolve(target, _exts);
                    if (match != null) result.Add(match);
                    else folderUnmatched = true;
                }
            }

            result = result.Where(x => !Infra.Files.SamePath(x, target.FullPath)).Distinct().ToList();
            if (result.Count == 0)
                throw new UnmatchedException(folderUnmatched
                    ? "не знайдено відповідний файл у теці-джерелі (файли зіставляються за відносним шляхом/ім’ям)."
                    : "немає інших файлів-джерел.");
            return result;
        }
    }

    // ---------------------------------------------------------------- merge-locres

    private static int RunMerge(ParsedArgs a)
    {
        a.RequirePositionals(2, -1, "цільовий locres і файл(и) для об’єднання");
        var options = LocresOptions(a);
        var sources = new TargetSources(a.Positionals.Skip(1), LocresExt);

        return RunOnTargets(a, "merge", (s, f) =>
        {
            var (added, skipped) = Merge(s.Locres, sources.For(f), options);
            return $"додано {added} {PluralizationHelper.GetNewWord(added)} {PluralizationHelper.GetRowsWord(added)}, пропущено {skipped} {PluralizationHelper.GetDuplicatesWord(skipped)}";
        });
    }

    /// <summary>MainWindow.MergeLocresFilesUnique_Click.</summary>
    private static (int Added, int Skipped) Merge(LocresFile currentLocres, IEnumerable<string> fileNames, AssetOptions options)
    {
        var itemsToAdd = new List<(string nsName, StringTable entry)>();
        int skippedCount = 0;
        var existingIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var ns in currentLocres)
            foreach (var entry in ns)
                existingIds.Add(string.IsNullOrEmpty(ns.Name) ? entry.Key : $"{ns.Name}::{entry.Key}");

        foreach (string fileName in fileNames)
        {
            var sourceFile = AssetSession.OpenLocres(fileName, options);
            foreach (var ns in sourceFile)
            {
                foreach (var entry in ns)
                {
                    string id = string.IsNullOrEmpty(ns.Name) ? entry.Key : $"{ns.Name}::{entry.Key}";
                    if (existingIds.Contains(id))
                    {
                        skippedCount++;
                        continue;
                    }
                    itemsToAdd.Add((ns.Name, new StringTable(entry.Key, entry.Value, entry.KeyHash, entry.ValueHash, entry.ExternID)));
                    existingIds.Add(id);
                }
            }
        }

        foreach (var (nsName, entry) in itemsToAdd)
        {
            if (!currentLocres.ContainsKey(nsName))
                currentLocres.Add(new NameSpaceTable(nsName, !string.IsNullOrEmpty(nsName) ? currentLocres.CalcHash(nsName) : 0u));

            var nsTable = currentLocres[nsName];
            if (nsTable.NameHash == 0 && !string.IsNullOrEmpty(nsName))
                nsTable.NameHash = currentLocres.CalcHash(nsName);

            if (nsTable.ContainsKey(entry.Key))
                continue;

            uint kHash = currentLocres.CalcHash(entry.Key);
            uint vHash = entry.ValueHash != 0 ? entry.ValueHash : entry.Value.StrCrc32();
            uint externID = entry.ExternID;

            if (externID == 0 && currentLocres.Version == LocresVersion.Optimized_CityHash64_ExternID_UTF16)
            {
                var allIds = currentLocres.SelectMany(n => n).Select(st => st.ExternID);
                externID = (allIds.Any() ? allIds.Max() : 0u) + 1u;
            }

            nsTable.Add(new StringTable(entry.Key, entry.Value, kHash, vHash, externID));
        }

        return (itemsToAdd.Count, skippedCount);
    }

    // ---------------------------------------------------------------- add-rows

    /// <summary>MainWindow.ImportNewRowsFromTXT_Click.</summary>
    private static int RunAddRows(ParsedArgs a)
    {
        a.RequirePositionals(2, -1, "цільовий locres і TXT файл(и)");
        var sources = new TargetSources(a.Positionals.Skip(1), new[] { ".txt" });

        return RunOnTargets(a, "add-rows", (s, f) =>
        {
            var txts = sources.For(f);
            var asset = s.Locres;
            var existingIds = new HashSet<string>(s.Rows.Select(r => r.ID), StringComparer.Ordinal);
            int addedCount = 0, skippedCount = 0;

            foreach (var path in txts)
            {
                foreach (var (id, text) in TxtHelper.ParseIDFormat(path))
                {
                    if (existingIds.Contains(id)) { skippedCount++; continue; }
                    var (ns, key) = SplitId(id);
                    if (string.IsNullOrWhiteSpace(key)) { skippedCount++; continue; }
                    asset.AddString(ns, key, text);
                    existingIds.Add(id);
                    addedCount++;
                }
            }

            return $"додано {addedCount} {PluralizationHelper.GetNewWord(addedCount)} {PluralizationHelper.GetRowsWord(addedCount)}, пропущено {skippedCount} {PluralizationHelper.GetDuplicatesWord(skippedCount)}";
        });
    }

    // ---------------------------------------------------------------- remove-rows

    private static int RunRemoveRows(ParsedArgs a)
    {
        a.RequirePositionals(1, -1, "цільовий locres");
        var ids = new List<string>(a.Positionals.Skip(1));
        foreach (var file in a.GetAll("--ids-file"))
        {
            if (!File.Exists(file)) throw new CliException($"Файл не знайдено: {file}");
            foreach (var raw in File.ReadAllLines(file, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith('#')) continue;
                int sep = raw.IndexOf('=');
                ids.Add(sep > 0 ? raw[..sep] : raw.TrimEnd('\r'));
            }
        }
        if (ids.Count == 0) throw new UsageException("Вкажіть ID рядків (аргументами або через --ids-file).");
        var idSet = new HashSet<string>(ids, StringComparer.Ordinal);

        return RunOnTargets(a, "remove-rows", (s, f) =>
        {
            // MainWindow.RemoveSelectedRows_Click: RemoveString per row, then RemoveEmptyNamespaces.
            var toRemove = new List<(string Ns, string Key)>();
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in s.Rows.OfType<LocresDataGridItem>())
            {
                if (!idSet.Contains(row.ID) || row.StringTableEntry == null) continue;
                var ns = s.Locres.First(n => n.Contains(row.StringTableEntry));
                toRemove.Add((ns.Name, row.StringTableEntry.Key));
                found.Add(row.ID);
            }

            foreach (var (ns, key) in toRemove)
                s.Locres.RemoveString(ns, key);
            s.Locres.RemoveEmptyNamespaces();

            foreach (var missing in idSet.Where(id => !found.Contains(id)))
                Log.Warn($"{f.Display}: ID не знайдено: {missing}");

            return $"видалено {Common.RowsW(toRemove.Count)}";
        });
    }

    // ---------------------------------------------------------------- import-hashes

    private static int RunImportHashes(ParsedArgs a)
    {
        a.RequirePositionals(2, -1, "цільовий locres і джерело хешів (locres або txt)");
        var options = LocresOptions(a);
        var sources = new TargetSources(a.Positionals.Skip(1), new[] { ".locres", ".txt" });

        return RunOnTargets(a, "import-hashes", (s, f) =>
        {
            int totalImported = 0, totalUnchanged = 0;
            foreach (var src in sources.For(f))
            {
                var (imported, unchanged) = Path.GetExtension(src).Equals(".txt", StringComparison.OrdinalIgnoreCase)
                    ? ImportHashesFromTxt(s.Rows, src)
                    : ImportHashesFromLocres(s.Rows, src, options);
                totalImported += imported;
                totalUnchanged += unchanged;
            }

            if (totalImported == 0 && totalUnchanged == 0)
                Log.Warn($"{f.Display}: не знайдено жодного рядка для оновлення хешів. Перевірте, чи збігаються ID.");

            return $"імпортовано {totalImported} {PluralizationHelper.GetHashesWord(totalImported)}, без змін {Common.RowsW(totalUnchanged)}";
        });
    }

    /// <summary>LocresHelper.ImportHashesFromLocres (with explicit CV2 options).</summary>
    private static (int Imported, int Unchanged) ImportHashesFromLocres(IList<DataGridItem> dataRows, string locresFilePath, AssetOptions options)
    {
        var locresFile = AssetSession.OpenLocres(locresFilePath, options);
        if (!locresFile.IsGood) return (0, 0);

        var hashDict = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var ns in locresFile)
        {
            var keyCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in ns)
            {
                string baseId = string.IsNullOrEmpty(ns.Name) ? e.Key : $"{ns.Name}::{e.Key}";
                keyCount.TryGetValue(baseId, out int c);
                string id = c == 0 ? baseId : $"{baseId}[{c}]";
                keyCount[baseId] = c + 1;
                hashDict[id] = e.ValueHash;
            }
        }
        return ApplyHashes(dataRows, hashDict);
    }

    /// <summary>LocresHelper.ImportHashesFromTxt.</summary>
    private static (int Imported, int Unchanged) ImportHashesFromTxt(IList<DataGridItem> dataRows, string txtFilePath)
    {
        var entries = TxtHelper.ParseIDFormat(txtFilePath);
        if (entries.Count == 0) return (0, 0);

        var hashDict = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var (id, text) in entries)
        {
            string decoded = AssetHelper.ReplaceBreaklines(text, Back: true);
            hashDict[id] = decoded.StrCrc32();
        }
        return ApplyHashes(dataRows, hashDict);
    }

    private static (int Imported, int Unchanged) ApplyHashes(IList<DataGridItem> dataRows, Dictionary<string, uint> hashDict)
    {
        int imported = 0, unchanged = 0;
        foreach (var item in dataRows)
        {
            if (item is not LocresDataGridItem locresItem || locresItem.HashTable == null) continue;
            if (!hashDict.TryGetValue(locresItem.ID, out uint valueHash)) continue;

            if (locresItem.HashTable.ValueHash == valueHash)
                unchanged++;
            else
            {
                locresItem.HashTable.ValueHash = valueHash;
                if (locresItem.StringTableEntry != null)
                    locresItem.StringTableEntry.ValueHash = valueHash;
                imported++;
            }
        }
        return (imported, unchanged);
    }

    // ---------------------------------------------------------------- compare-locres

    /// <summary>LocresCompareWindow.LoadLocresDict.</summary>
    private static Dictionary<string, (string Text, uint Hash)> LoadLocresDict(string path, AssetOptions options)
    {
        var locresFile = AssetSession.OpenLocres(path, options);
        if (!locresFile.IsGood) return null;

        var dict = new Dictionary<string, (string Text, uint Hash)>(StringComparer.Ordinal);
        foreach (var ns in locresFile)
        {
            var keyCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in ns)
            {
                string baseId = string.IsNullOrEmpty(ns.Name) ? entry.Key : $"{ns.Name}::{entry.Key}";
                keyCount.TryGetValue(baseId, out int c);
                string fullId = c == 0 ? baseId : $"{baseId}[{c}]";
                keyCount[baseId] = c + 1;
                dict[fullId] = (entry.Value ?? "", entry.ValueHash);
            }
        }
        return dict;
    }

    private static int RunCompare(ParsedArgs a)
    {
        a.RequirePositionals(2, 2, "старий і новий locres (файли або теки)");
        var options = LocresOptions(a);
        bool overwrite = a.Has("--overwrite"), list = a.Has("--list"), json = a.Has("--json");
        string oldArg = a.Positionals[0], newArg = a.Positionals[1];
        string output = a.Get("--output");
        Infra.Files.RejectStdout(output);
        // --json / --list are read-only reports: export files only when -o is given explicitly.
        bool export = output != null || !(json || list);

        bool folders = Directory.Exists(oldArg) && Directory.Exists(newArg);
        if (!folders && (Directory.Exists(oldArg) || Directory.Exists(newArg)))
            throw new UsageException("compare-locres: обидва аргументи мають бути файлами або обидва — теками.");

        var (newFiles, batch) = Infra.Files.CollectInputs(new[] { newArg }, LocresExt);
        var oldSpec = folders ? new SourceSpec(oldArg, "locres", "id") : null;
        if (!folders && !File.Exists(oldArg)) throw new CliException($"Файл не знайдено: {oldArg}");
        var runner = new BatchRunner(batch);
        var jsonResults = new List<object>();

        runner.Run(newFiles, f =>
        {
            string oldPath = folders ? oldSpec.Resolve(f, originalOnly: true) : oldArg;
            if (oldPath == null) throw new UnmatchedException("не знайдено відповідний старий locres.");

            var oldDict = LoadLocresDict(oldPath, options);
            var newDict = LoadLocresDict(f.FullPath, options);
            if (oldDict == null || newDict == null)
                throw new CliException("Не вдалося прочитати один або обидва файли.");

            var newItems = new List<LocresCompareItem>();
            var changedItems = new List<LocresCompareItem>();
            var hashChangedItems = new List<LocresCompareItem>();

            foreach (var (key, newEntry) in newDict)
            {
                if (!oldDict.TryGetValue(key, out var oldEntry))
                {
                    newItems.Add(new LocresCompareItem { Key = key, NewText = newEntry.Text });
                    continue;
                }
                if (oldEntry.Text != newEntry.Text)
                    changedItems.Add(new LocresCompareItem { Key = key, OldText = oldEntry.Text, NewText = newEntry.Text });
                else if (oldEntry.Hash != newEntry.Hash)
                    hashChangedItems.Add(new LocresCompareItem { Key = key, OldText = oldEntry.Text, NewText = newEntry.Text });
            }

            // Export (LocresCompareWindow.Export_Click): <stem>_new.txt and <stem>_changed.csv.
            string stemPath;
            if (output == null) stemPath = Path.Combine(Path.GetDirectoryName(f.Display) ?? "", f.BaseName);
            else if (batch) stemPath = Path.Combine(output, f.RelDir, f.BaseName);
            else if (Infra.Files.LooksLikeDirectory(output)) stemPath = Path.Combine(output, f.BaseName);
            else stemPath = Path.Combine(Path.GetDirectoryName(output) ?? "", Path.GetFileNameWithoutExtension(output));

            string newPath = stemPath + "_new.txt";
            string changedPath = stemPath + "_changed.csv";
            var saved = new List<string>();

            if (export && newItems.Count > 0) Infra.Files.EnsureWritable(newPath, overwrite);
            if (export && changedItems.Count > 0) Infra.Files.EnsureWritable(changedPath, overwrite);

            if (export && newItems.Count > 0)
            {
                Infra.Files.EnsureParentDir(newPath);
                File.WriteAllLines(newPath, newItems.Select(x => $"{x.Key}={x.NewText}"), new UTF8Encoding(false));
                saved.Add(newPath);
            }
            if (export && changedItems.Count > 0)
            {
                Infra.Files.EnsureParentDir(changedPath);
                LocresCompareCsvExporter.Export(null, changedPath, Array.Empty<(string Key, string Text)>(),
                    changedItems.Select(x => (x.Key, x.OldText, x.NewText)));
                saved.Add(changedPath);
            }

            if (json)
            {
                jsonResults.Add(new
                {
                    oldFile = oldPath, newFile = f.Display,
                    @new = newItems.Select(x => new { key = x.Key, text = x.NewText }),
                    changed = changedItems.Select(x => new { key = x.Key, oldText = x.OldText, newText = x.NewText }),
                    hashChanged = hashChangedItems.Select(x => new { key = x.Key, text = x.NewText }),
                    saved
                });
                return;
            }

            Log.Out($"{oldPath} -> {f.Display}");
            Log.Out($"Нових рядків: {newItems.Count}");
            Log.Out($"Змінений текст {PluralizationHelper.GetRowsWordLocative(changedItems.Count)}");
            Log.Out($"Прихованих змін хешів тексту: {hashChangedItems.Count}{(newItems.Count + changedItems.Count + hashChangedItems.Count == 0 ? ". Файли ідентичні." : "")}");
            if (list)
            {
                foreach (var x in newItems) Log.Out($"+ {x.Key}={x.NewText}");
                foreach (var x in changedItems) Log.Out($"~ {x.Key}: {x.OldText} → {x.NewText}");
                foreach (var x in hashChangedItems) Log.Out($"# {x.Key}");
            }
            foreach (var p in saved) Log.Out($"Збережено: {p}");
        });

        if (json && (batch || jsonResults.Count > 0)) Log.Json(batch ? jsonResults : jsonResults[0]);
        return runner.ExitCode();
    }
}
