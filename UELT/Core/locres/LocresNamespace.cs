using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace UELT.Core.locres
{
    public enum LocresVersion : byte
    {
        Legacy = 0,
        Compact = 1,
        Optimized = 2,
        Optimized_CityHash64_UTF16 = 3,
        Optimized_CityHash64_ExternID_UTF16 = 4,
    }

    public sealed class HashTable
    {
        public uint NameHash { get; set; }
        public uint KeyHash { get; set; }
        public uint ValueHash { get; set; }
        public uint ExternID { get; set; } = 0;

        public HashTable() { }

        public HashTable(uint nameHash, uint keyHash = 0, uint valueHash = 0)
        {
            NameHash = nameHash;
            KeyHash = keyHash;
            ValueHash = valueHash;
        }
    }

    public sealed class StringTable(string key, string value = "", uint keyHash = 0, uint valueHash = 0, uint externID = 0)
    {
        public string Key { get; set; } = key;
        public string Value { get; set; } = value;
        public uint KeyHash { get; set; } = keyHash;
        public uint ValueHash { get; set; } = valueHash;
        public uint ExternID { get; set; } = externID;
        public int OriginalStringIndex { get; set; } = -1;
        internal NameSpaceTable Root { get; set; } = null!;
        public bool KeyWasUnicode { get; set; } = false;
        public bool ValueWasUnicode { get; set; } = false;

        public StringTable() : this("", "") { }
    }

    public sealed class NameSpaceTable : List<StringTable>
    {
        private readonly Dictionary<string, StringTable> _lookup =
            new Dictionary<string, StringTable>(StringComparer.Ordinal);

        public string Name { get; set; }
        public uint NameHash { get; set; }

        public NameSpaceTable(string name, uint nameHash = 0)
        {
            Name = name;
            NameHash = nameHash;
        }

        public StringTable this[string key] => _lookup[key];

        public bool ContainsKey(string key) => _lookup.ContainsKey(key);

        public new void Add(StringTable entry)
        {
            entry.Root = this;
            base.Add(entry);
            _lookup[entry.Key] = entry;
        }

        public void RemoveKey(string key)
        {
            if (_lookup.TryGetValue(key, out var entry))
            {
                base.Remove(entry);
                _lookup.Remove(key);
            }
        }

        public void RenameKey(string oldKey, string newKey)
        {
            if (!_lookup.TryGetValue(oldKey, out var entry)) return;
            if (string.Equals(oldKey, newKey, StringComparison.Ordinal)) return;
            if (_lookup.ContainsKey(newKey))
                throw new InvalidOperationException($"ID '{newKey}' вже існує у області імен '{Name}'.");

            _lookup.Remove(oldKey);
            entry.Key = newKey;
            _lookup[newKey] = entry;
        }

        public void UpdateEntry(string oldKey, string newKey, string newValue,
                                uint keyHash, uint valueHash, uint externID)
        {
            if (!_lookup.TryGetValue(oldKey, out var entry)) return;

            if (!string.Equals(oldKey, newKey, StringComparison.Ordinal))
            {
                _lookup.Remove(oldKey);
                entry.Key = newKey;
                _lookup[newKey] = entry;
            }

            entry.Value = newValue;
            entry.KeyHash = keyHash != 0 ? keyHash : entry.KeyHash;
            string newValueDecoded = AssetHelper.ReplaceBreaklines(newValue, Back: true);
            entry.ValueHash = valueHash != 0 ? valueHash : newValueDecoded.StrCrc32();
            entry.ExternID = externID;
        }
        public bool NameWasUnicode { get; set; } = false;
    }

    public enum CV2KeySet
    {
        Release,
        Demo,
    }

    internal static class CV2Cipher
    {
        private static readonly byte[] DemoKey =
        {
            0x6D, 0xC0, 0xE5, 0x02, 0x17, 0x55, 0x29, 0xF2, 0x0E, 0x1F, 0x68, 0x0D, 0xAD, 0x3E, 0xF8, 0x2C,
            0x5F, 0x9E, 0xC2, 0x20, 0xEB, 0x54, 0xBE, 0x2E, 0x23, 0xA1, 0xA4, 0x7A, 0xE3, 0x09, 0x4C, 0x51,
            0xFD, 0x9B, 0x6E, 0xF9, 0x8B, 0x00, 0x37, 0xD4, 0x74, 0xA2, 0x64, 0xA0, 0xC3, 0x5C, 0x36, 0xE6,
            0x15, 0x0B, 0x1C, 0xFE, 0x3C, 0xAB, 0xF1, 0xE4, 0xC7, 0xAE, 0x3D, 0xB9, 0x01, 0x76, 0xAA, 0x21
        };

        private static readonly byte[] ReleaseKey =
        {
            0xE9, 0x63, 0xCB, 0x28, 0xB6, 0xCD, 0x06, 0xCA, 0x5D, 0xBB, 0x57, 0xDB, 0xDC, 0x18, 0xDE, 0x2A,
            0x38, 0x30, 0x8C, 0x69, 0xBA, 0x9C, 0xB1, 0x7D, 0x70, 0x0C, 0x08, 0x93, 0x14, 0xE2, 0x25, 0x92,
            0x7B, 0xAF, 0x56, 0x88, 0x47, 0x61, 0x86, 0xC8, 0xF7, 0xEE, 0x1B, 0x4E, 0xCC, 0x45, 0x98, 0x4D,
            0xBC, 0xDD, 0x59, 0x84, 0x26, 0x5B, 0x0F, 0x22, 0x85, 0x77, 0x5A, 0x9A, 0x53, 0x1A, 0x83, 0x81
        };

        private static byte[] GetKey(CV2KeySet keySet) =>
            keySet == CV2KeySet.Demo ? DemoKey : ReleaseKey;

        internal static void Decrypt(byte[] data, CV2KeySet keySet = CV2KeySet.Release)
            => ProcessData(data, encrypt: false, keySet);
        internal static void Encrypt(byte[] data, CV2KeySet keySet = CV2KeySet.Release)
            => ProcessData(data, encrypt: true, keySet);

        private static void ProcessData(byte[] data, bool encrypt, CV2KeySet keySet)
        {
            var key = GetKey(keySet);
            var span = data.AsSpan();

            long lutOffset = BitConverter.ToInt64(span.Slice(17, 8));
            int offset = (int)lutOffset;

            int stringsCount = BitConverter.ToInt32(span.Slice(offset, 4));
            offset += 4;

            for (int i = 0; i < stringsCount; i++)
            {
                int length = BitConverter.ToInt32(span.Slice(offset, 4));
                offset += 4;

                if (length > 0)
                {
                    var strSpan = span.Slice(offset, length);
                    offset += length + 4;
                    int charCount = length - 1;

                    for (int c = 0; c < charCount; c++)
                        strSpan[c] ^= key[(charCount + c) & 0x3F];
                }
                else if (length < 0)
                {
                    int absLen = -length;
                    Span<char> strSpan = MemoryMarshal.Cast<byte, char>(span.Slice(offset, absLen * 2));
                    offset += absLen * 2 + 4;
                    int charCount = absLen - 1;

                    for (int c = 0; c < charCount; c++)
                    {
                        char xorChar = (char)key[(charCount + c) & 0x3F];
                        if (!encrypt)
                        {
                            if (strSpan[c] == 0xF000) strSpan[c] = (char)0x0000;
                            if (strSpan[c] == 0xF001) strSpan[c] = (char)0xFFFF;
                            strSpan[c] ^= xorChar;
                        }
                        else
                        {
                            strSpan[c] ^= xorChar;
                            if (strSpan[c] == 0x0000) strSpan[c] = (char)0xF000;
                            if (strSpan[c] == 0xFFFF) strSpan[c] = (char)0xF001;
                        }
                    }
                }
                else
                {
                    offset += 4;
                }
            }
        }
    }

    public sealed class LocresFile : List<NameSpaceTable>, IAsset
    {
        private static readonly byte[] MagicGUID =
        {
            0x0E, 0x14, 0x74, 0x75, 0x67, 0x4A, 0x03, 0xFC,
            0x4A, 0x15, 0x90, 0x9D, 0xC3, 0x37, 0x7F, 0x1B
        };

        private readonly Dictionary<string, NameSpaceTable> _nsLookup = new(StringComparer.Ordinal);

        public LocresVersion Version { get; private set; }
        public bool IsGood { get; set; } = true;

        public bool IsCV2Encrypted { get; private set; } = false;

        public CV2KeySet CV2KeySet { get; set; } = CV2KeySet.Release;

        public LocresFile(string filePath)
        {
            using var fs = File.OpenRead(filePath);
            Load(fs);
        }

        public LocresFile(string filePath, bool? forceEncrypted, CV2KeySet keySet = CV2KeySet.Release)
        {
            CV2KeySet = keySet;
            using var fs = File.OpenRead(filePath);
            Load(fs, forceEncrypted);
        }

        public LocresFile(LocresVersion version = LocresVersion.Optimized)
        {
            Version = version;
            IsGood = true;
        }

        public new void Add(NameSpaceTable ns)
        {
            base.Add(ns);
            _nsLookup[ns.Name] = ns;
        }

        public NameSpaceTable this[string name] => _nsLookup[name];

        public bool ContainsKey(string name) => _nsLookup.ContainsKey(name);

        public void RemoveNameSpace(string name)
        {
            if (_nsLookup.TryGetValue(name, out var ns))
            {
                base.Remove(ns);
                _nsLookup.Remove(name);
            }
        }

        public void AddString(string nameSpace, string key, string value,
                      uint nameSpaceHash = 0, uint keyHash = 0,
                      uint? valueHash = null, uint externID = 0)
        {
            uint nsHash = nameSpaceHash != 0 ? nameSpaceHash
                        : string.IsNullOrEmpty(nameSpace) ? 0u : CalcHash(nameSpace);
            uint kHash = keyHash != 0 ? keyHash : CalcHash(key);
            string valueForHash = AssetHelper.ReplaceBreaklines(value, Back: true);
            uint vHash = valueHash ?? valueForHash.StrCrc32();

            if (!ContainsKey(nameSpace))
                Add(new NameSpaceTable(nameSpace, nsHash));

            var ns = this[nameSpace];
            if (nsHash != 0) ns.NameHash = nsHash;

            if (!ns.ContainsKey(key))
            {
                if (externID == 0 && Version == LocresVersion.Optimized_CityHash64_ExternID_UTF16)
                {
                    var allIds = this.SelectMany(n => n).Select(st => st.ExternID);
                    externID = (allIds.Any() ? allIds.Max() : 0u) + 1u;
                }

                ns.Add(new StringTable(key, value, kHash, vHash, externID));
            }
            else
            {
                var e = ns[key];
                e.Value = value;
                e.KeyHash = kHash;
                e.ValueHash = vHash;
                if (externID != 0) e.ExternID = externID;
            }
        }

        public void RemoveString(string nameSpace, string key)
        {
            if (ContainsKey(nameSpace))
                this[nameSpace].RemoveKey(key);
        }

        public HashTable GetHash(string nameSpace, string key)
        {
            if (!ContainsKey(nameSpace)) return null;
            var ns = this[nameSpace];
            if (!ns.ContainsKey(key)) return null;
            var e = ns[key];
            return new HashTable(ns.NameHash, e.KeyHash, e.ValueHash) { ExternID = e.ExternID };
        }

        public uint CalcHash(string str)
        {
            if (string.IsNullOrEmpty(str)) return 0;

            return Version == LocresVersion.Optimized_CityHash64_UTF16 || Version == LocresVersion.Optimized_CityHash64_ExternID_UTF16
                ? CityHash64Folded(str)
                : str.StrCrc32();
        }

        public void RemoveEmptyNamespaces()
        {
            var empty = this.Where(ns => ns.Count == 0).ToList();
            foreach (var ns in empty)
                RemoveNameSpace(ns.Name);
        }

        private static uint CityHash64Folded(string value)
        {
            byte[] utf16Bytes = Encoding.Unicode.GetBytes(value);
            ulong h = CityHash.CityHash64(utf16Bytes);
            unchecked
            {
                return (uint)h + (uint)(h >> 32) * 23u;
            }
        }

        public List<List<string>> ExtractTexts()
        {
            var result = new List<List<string>>();
            foreach (var ns in this)
            {
                var keyCount = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var e in ns)
                {
                    string baseId = string.IsNullOrEmpty(ns.Name) ? e.Key : $"{ns.Name}::{e.Key}";
                    keyCount.TryGetValue(baseId, out int count);
                    string id = count == 0 ? baseId : $"{baseId}[{count}]";
                    keyCount[baseId] = count + 1;
                    result.Add(new List<string> { id, e.Value });
                }
            }
            return result;
        }

        public void ImportTexts(List<List<string>> strings)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in strings)
                if (row.Count >= 2) map[row[0]] = row[1];

            foreach (var ns in this)
            {
                var keyCount = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var e in ns)
                {
                    string baseId = string.IsNullOrEmpty(ns.Name) ? e.Key : $"{ns.Name}::{e.Key}";
                    keyCount.TryGetValue(baseId, out int count);
                    string id = count == 0 ? baseId : $"{baseId}[{count}]";
                    keyCount[baseId] = count + 1;

                    if (map.TryGetValue(id, out string v))
                        e.Value = v;
                }
            }
        }

        public void SaveFile(string filePath)
        {
            using var ms = new MemoryStream();
            Save(ms);

            byte[] bytes = ms.ToArray();

            if (IsCV2Encrypted)
                CV2Cipher.Encrypt(bytes, CV2KeySet);

            File.WriteAllBytes(filePath, bytes);
        }

        private void Load(Stream stream, bool? forceEncrypted = null)
        {
            byte[] raw;
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                raw = ms.ToArray();
            }

            bool hasMagic = raw.Length >= MagicGUID.Length &&
                            raw.AsSpan(0, MagicGUID.Length).SequenceEqual(MagicGUID);

            if (hasMagic)
            {
                byte versionByte = raw[MagicGUID.Length];
                bool isVersionedAndHasLut = versionByte >= (byte)LocresVersion.Compact;

                if (isVersionedAndHasLut && forceEncrypted == true)
                {
                    byte[] decrypted = (byte[])raw.Clone();
                    CV2Cipher.Decrypt(decrypted, CV2KeySet);
                    IsCV2Encrypted = true;
                    raw = decrypted;
                }
            }

            using var loadStream = new MemoryStream(raw);
            using var br = new BinaryReader(loadStream, Encoding.UTF8, leaveOpen: false);

            byte[] magic = br.ReadBytes(16);

            if (magic.SequenceEqual(MagicGUID))
            {
                Version = (LocresVersion)br.ReadByte();
                if (Version > LocresVersion.Optimized_CityHash64_ExternID_UTF16)
                    throw new Exception($"Непідтримувана версія locres: {(byte)Version}");
            }
            else
            {
                Version = LocresVersion.Legacy;
                loadStream.Seek(0, SeekOrigin.Begin);
            }

            if (Version == LocresVersion.Legacy)
            {
                LoadLegacy(br);
                return;
            }

            long lutOffset = br.ReadInt64();
            long headerEnd = loadStream.Position;

            loadStream.Seek(lutOffset, SeekOrigin.Begin);
            int lutCount = br.ReadInt32();
            var lut = new string[lutCount];
            var lutWasUnicode = new bool[lutCount];

            for (int i = 0; i < lutCount; i++)
            {
                lut[i] = br.ReadUEString(out lutWasUnicode[i]);
                if (Version >= LocresVersion.Optimized)
                    br.ReadInt32();
            }

            loadStream.Seek(headerEnd, SeekOrigin.Begin);

            if (Version >= LocresVersion.Optimized)
                br.ReadInt32();

            int nsCount = br.ReadInt32();

            for (int n = 0; n < nsCount; n++)
            {
                uint nsHash = Version >= LocresVersion.Optimized ? br.ReadUInt32() : 0u;
                string nsName = br.ReadUEString(out bool nsWasUnicode);

                int keyCount = br.ReadInt32();

                if (!ContainsKey(nsName))
                    Add(new NameSpaceTable(nsName, nsHash) { NameWasUnicode = nsWasUnicode });

                var ns = this[nsName];
                if (nsHash != 0) ns.NameHash = nsHash;

                for (int k = 0; k < keyCount; k++)
                {
                    uint keyHash = Version >= LocresVersion.Optimized ? br.ReadUInt32() : 0u;
                    string keyStr = br.ReadUEString(out bool keyWasUnicode);
                    uint valHash = br.ReadUInt32();

                    int lutIdx = br.ReadInt32();
                    string value = lutIdx >= 0 && lutIdx < lut.Length
                                     ? lut[lutIdx] : string.Empty;

                    uint externID = Version == LocresVersion.Optimized_CityHash64_ExternID_UTF16
                                     ? br.ReadUInt32() : 0u;

                    if (!ns.ContainsKey(keyStr))
                    {
                        ns.Add(new StringTable(keyStr, value, keyHash, valHash, externID)
                        {
                            OriginalStringIndex = lutIdx,
                            KeyWasUnicode = keyWasUnicode,
                            ValueWasUnicode = lutIdx >= 0 && lutIdx < lutWasUnicode.Length ? lutWasUnicode[lutIdx] : false
                        });
                    }
                }
            }
        }

        private void LoadLegacy(BinaryReader br)
        {
            int nsCount = br.ReadInt32();
            for (int i = 0; i < nsCount; i++)
            {
                string nsName = br.ReadUEString(out bool nsWasUnicode);
                int keyCount = br.ReadInt32();

                if (!ContainsKey(nsName))
                    Add(new NameSpaceTable(nsName) { NameWasUnicode = nsWasUnicode });

                var ns = this[nsName];

                for (int j = 0; j < keyCount; j++)
                {
                    string key = br.ReadUEString(out bool keyWasUnicode);
                    uint valHash = br.ReadUInt32();
                    string value = br.ReadUEString(out bool valueWasUnicode);

                    ns.Add(new StringTable(key, value, 0, valHash)
                    {
                        KeyWasUnicode = keyWasUnicode,
                        ValueWasUnicode = valueWasUnicode
                    });
                }
            }
        }

        private void Save(Stream stream)
        {
            using var bw = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            if (Version == LocresVersion.Legacy)
            {
                SaveLegacy(bw);
                return;
            }

            bw.Write(MagicGUID);
            bw.Write((byte)Version);

            long lutOffsetPos = stream.Position;
            bw.Write(0L);

            if (Version >= LocresVersion.Optimized)
                bw.Write(this.Sum(ns => ns.Count));

            var lutList = new List<(string Value, bool WasUnicode)>();
            var lutIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var lutRefCounts = new List<int>();

            bw.Write(Count);

            foreach (var ns in this)
            {
                if (Version >= LocresVersion.Optimized)
                    bw.Write(ns.NameHash != 0 ? ns.NameHash : CalcHash(ns.Name));

                bw.WriteUEString(ns.Name, legacyEmptyString: Version <= LocresVersion.Optimized, forceUnicode: ns.NameWasUnicode);
                bw.Write(ns.Count);

                foreach (var e in ns)
                {
                    if (Version >= LocresVersion.Optimized)
                        bw.Write(e.KeyHash != 0 ? e.KeyHash : CalcHash(e.Key));

                    bw.WriteUEString(e.Key, legacyEmptyString: Version <= LocresVersion.Optimized, forceUnicode: e.KeyWasUnicode);
                    string eValueDecoded = AssetHelper.ReplaceBreaklines(e.Value, Back: true);
                    bw.Write(e.ValueHash != 0 ? e.ValueHash : eValueDecoded.StrCrc32());

                    if (!lutIndex.TryGetValue(e.Value, out int idx))
                    {
                        idx = lutList.Count;
                        lutList.Add((e.Value, e.ValueWasUnicode));
                        lutIndex[e.Value] = idx;
                        lutRefCounts.Add(0);
                    }
                    lutRefCounts[idx]++;
                    bw.Write(idx);

                    if (Version == LocresVersion.Optimized_CityHash64_ExternID_UTF16)
                        bw.Write(e.ExternID);
                }
            }

            long lutPos = stream.Position;

            bw.Write(lutList.Count);
            for (int i = 0; i < lutList.Count; i++)
            {
                bw.WriteUEString(lutList[i].Value, legacyEmptyString: Version <= LocresVersion.Optimized, forceUnicode: lutList[i].WasUnicode);
                if (Version >= LocresVersion.Optimized)
                    bw.Write(lutRefCounts[i]);
            }

            long endPos = stream.Position;

            stream.Seek(lutOffsetPos, SeekOrigin.Begin);
            bw.Write(lutPos);
            stream.Seek(endPos, SeekOrigin.Begin);
        }

        private void SaveLegacy(BinaryWriter bw)
        {
            bw.Write(Count);
            foreach (var ns in this)
            {
                bw.WriteUEString(ns.Name, legacyEmptyString: true, forceUnicode: ns.NameWasUnicode);
                bw.Write(ns.Count);
                foreach (var e in ns)
                {
                    bw.WriteUEString(e.Key, legacyEmptyString: true, forceUnicode: e.KeyWasUnicode);
                    string eValueDecoded = AssetHelper.ReplaceBreaklines(e.Value, Back: true);
                    bw.Write(e.ValueHash != 0 ? e.ValueHash : eValueDecoded.StrCrc32());
                    bw.WriteUEString(e.Value, legacyEmptyString: true, forceUnicode: e.ValueWasUnicode);
                }
            }
        }
    }
}