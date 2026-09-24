using System.IO;
using System.Text;
using UAssetAPI.ExportTypes;

namespace UELT.Core.uasset;

public class J5BinderAssetParser
{
    private const ulong MagicBNDFLL = 0x4C4C46444E42UL;  
    private const ulong MagicBNDLL = 0x4C4C4C444E42UL;    
    private const ulong MagicSTXTENLL = 0x4C4C4E4554585453UL; 
    private const int BLOCK_STRUCT_SIZE = 56;  
    private const int STREAM_START = 8;   
    private const int BTE_TABLE_SIZE = 8;   
    private const int BTE_RESERVED = 16;     
    private const int BTE_TABLE_OFFSET = 48;   

    private ParsedState _state;
    private readonly Dictionary<int, (StxtBlock block, int entryIndex)> _globalToEntryMap = new();

    private class ParsedState
    {
        public NormalExport Export = null!;
        public long BaseOffset;
        public int TableInfoBase;
        public int TablesCount;
        public StxtBlock[] Blocks = null!;
        public byte[] OriginalExtras = null!;
    }

    private class StxtBlock
    {
        public int ArrayPos;
        public int Idx;
        public int Type;
        public long TableSize;
        public long Reserved;
        public ulong Hash;
        public long NameOffset;
        public long NextOffset;
        public long TableOffset;
        public string Name = "";

        public ulong StxtMagic;
        public int StxtType;
        public int HeaderSize;
        public int TableBufSize;
        public int TablesCount;
        public ulong Unko;

        public StxtEntry[] Entries;
        public byte[] OriginalBlockBytes;
        public long OriginalBlockStart;
    }

    private class StxtEntry
    {
        public int Index;
        public string Text = "";
        public int OriginalOffset;
        public byte[] OriginalBytes = null!;
        public bool Modified;
        public int StringLength;
    }

    public static bool IsJ5BinderAsset(NormalExport export)
    {
        var extras = export?.Extras;
        if (extras == null || extras.Length < 16) return false;
        ulong magic = BitConverter.ToUInt64(extras, STREAM_START);
        return magic == MagicBNDFLL || magic == MagicBNDLL;
    }

    public void Extract(NormalExport export, ref int startIndex, List<List<string>> result)
    {
        _globalToEntryMap.Clear();

        var buf = export.Extras;
        if (buf == null || buf.Length < 56) return;

        using var ms = new MemoryStream(buf);
        using var reader = new BinaryReader(ms);

        reader.BaseStream.Seek(STREAM_START, SeekOrigin.Begin);

        ulong magic = reader.ReadUInt64();
        if (magic != MagicBNDFLL && magic != MagicBNDLL)
            return;

        reader.ReadInt32();
        int tablesCount = reader.ReadInt32(); 
        reader.ReadInt32(); 
        reader.ReadInt32();
        reader.ReadInt64();
        long baseOffset = reader.ReadInt64();
        reader.ReadInt64();

        int tableInfoBase = (int)reader.BaseStream.Position;

        var blocks = new StxtBlock[tablesCount];
        for (int t = 0; t < tablesCount; t++)
        {
            long blockOffset = tableInfoBase + (long)t * BLOCK_STRUCT_SIZE;
            reader.BaseStream.Seek(blockOffset, SeekOrigin.Begin);

            var block = new StxtBlock
            {
                ArrayPos = t,
                Idx = reader.ReadInt32(),
                Type = reader.ReadInt32(),
                TableSize = reader.ReadInt64(),
                Reserved = reader.ReadInt64(),
                Hash = reader.ReadUInt64(),
                NameOffset = reader.ReadInt64(),
                NextOffset = reader.ReadInt64(),
                TableOffset = reader.ReadInt64(),
            };
            blocks[t] = block;
        }

        for (int t = 0; t < tablesCount; t++)
        {
            var block = blocks[t];
            long namePos = STREAM_START + block.NameOffset;
            if (namePos > 0 && namePos < buf.Length)
            {
                reader.BaseStream.Seek(namePos, SeekOrigin.Begin);
                var nameBytes = new List<byte>();
                byte b;
                while ((b = reader.ReadByte()) != 0)
                    nameBytes.Add(b);
                string name = Encoding.UTF8.GetString(nameBytes.ToArray());
                if (name.EndsWith(".stx", StringComparison.OrdinalIgnoreCase))
                    name = name[..^4];
                block.Name = name;
            }
            else
            {
                block.Name = $"Table{t}";
            }
        }

        for (int t = 0; t < tablesCount; t++)
        {
            var block = blocks[t];
            long stxtPos = STREAM_START + baseOffset + block.TableOffset;
            long blockEnd = Math.Min(stxtPos + block.TableSize, buf.Length);

            if (stxtPos < 0 || stxtPos >= buf.Length)
            {
                block.OriginalBlockBytes = Array.Empty<byte>();
                block.OriginalBlockStart = stxtPos;
                continue;
            }

            int len = (int)(blockEnd - stxtPos);
            block.OriginalBlockBytes = new byte[len];
            block.OriginalBlockStart = stxtPos;
            if (len > 0)
                Array.Copy(buf, stxtPos, block.OriginalBlockBytes, 0, len);
        }

        for (int t = 0; t < tablesCount; t++)
        {
            var block = blocks[t];
            long stxtPos = STREAM_START + baseOffset + block.TableOffset;

            if (stxtPos + 32 >= buf.Length) continue;
            reader.BaseStream.Seek(stxtPos, SeekOrigin.Begin);

            ulong stxtMagic = reader.ReadUInt64();
            if (stxtMagic != MagicSTXTENLL) continue;

            block.StxtMagic = stxtMagic;
            block.StxtType = reader.ReadInt32();
            block.HeaderSize = reader.ReadInt32();
            block.TableBufSize = reader.ReadInt32();
            block.TablesCount = reader.ReadInt32();
            block.Unko = reader.ReadUInt64();

            block.Entries = new StxtEntry[block.TablesCount];
            int tableStart = (int)(stxtPos + block.HeaderSize);

            for (int s = 0; s < block.TablesCount; s++)
            {
                int entryOffset = tableStart + s * block.TableBufSize;
                if (entryOffset + 8 > buf.Length) break;

                reader.BaseStream.Seek(entryOffset, SeekOrigin.Begin);
                int strIdx = reader.ReadInt32();
                int strOffset = reader.ReadInt32();

                int strAbsPos = (int)(stxtPos + strOffset);
                if (strAbsPos + 2 > buf.Length) continue;

                reader.BaseStream.Seek(strAbsPos, SeekOrigin.Begin);
                var strBytes = new List<byte>();
                while (true)
                {
                    if (strBytes.Count + 2 > buf.Length - strAbsPos) break;
                    byte b1 = reader.ReadByte();
                    byte b2 = reader.ReadByte();
                    strBytes.Add(b1);
                    strBytes.Add(b2);
                    if (b1 == 0 && b2 == 0) break;
                }

                if (strBytes.Count >= 2 && strBytes[^2] == 0 && strBytes[^1] == 0)
                    strBytes.RemoveRange(strBytes.Count - 2, 2);

                string text = Encoding.Unicode.GetString(strBytes.ToArray());

                var entryBytes = new byte[strBytes.Count + 2];
                Array.Copy(strBytes.ToArray(), entryBytes, strBytes.Count);

                var entry = new StxtEntry
                {
                    Index = strIdx,
                    Text = text,
                    OriginalOffset = strOffset,
                    OriginalBytes = entryBytes,
                    Modified = false,
                    StringLength = entryBytes.Length,
                };

                block.Entries[s] = entry;
                _globalToEntryMap[startIndex] = (block, s);

                string path = $"{block.Name}[{strIdx}]";
                result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(text) });

                startIndex++;
            }
        }

        _state = new ParsedState
        {
            Export = export,
            BaseOffset = baseOffset,
            TableInfoBase = tableInfoBase,
            TablesCount = tablesCount,
            Blocks = blocks,
            OriginalExtras = (byte[])buf.Clone(),
        };
    }

    public void ApplyText(int globalIndex, string newText)
    {
        if (_state == null)
            return;

        if (_globalToEntryMap.TryGetValue(globalIndex, out var info))
        {
            var entry = info.block.Entries![info.entryIndex];
            if (entry != null)
            {
                entry.Text = newText;
                entry.Modified = true;
            }
        }
    }

    public void Rebuild()
    {
        if (_state == null) return;

        var st = _state;
        var oldBuf = st.OriginalExtras;

        var builtBlocks = new byte[st.TablesCount][];
        bool anyChanged = false;

        for (int t = 0; t < st.TablesCount; t++)
        {
            var block = st.Blocks[t];
            if (block.StxtMagic == MagicSTXTENLL)
            {
                builtBlocks[t] = BuildStxtenll(block);
                if (builtBlocks[t].Length != block.TableSize)
                    anyChanged = true;
            }
            else
            {
                builtBlocks[t] = block.OriginalBlockBytes ?? Array.Empty<byte>();
            }
        }

        if (!anyChanged)
        {
            RebuildInPlace(st, oldBuf, builtBlocks);
            return;
        }

        var ordered = st.Blocks
            .OrderBy(b => b.TableOffset)
            .ToArray();

        long dataRegionStart = STREAM_START + st.BaseOffset;

        using var dataMs = new MemoryStream();
        using var dataWriter = new BinaryWriter(dataMs);

        var newTableOffsets = new long[st.TablesCount];
        var newTableSizes = new long[st.TablesCount];

        long readCursor = 0;

        foreach (var block in ordered)
        {
            long origBlockStart = block.TableOffset;

            if (origBlockStart > readCursor)
            {
                long gapLen = origBlockStart - readCursor;
                long gapAbs = dataRegionStart + readCursor;
                if (gapAbs >= 0 && gapAbs + gapLen <= oldBuf.Length)
                    dataWriter.Write(oldBuf, (int)gapAbs, (int)gapLen);
                else
                    for (long g = 0; g < gapLen; g++) dataWriter.Write((byte)0);
                readCursor = origBlockStart;
            }

            long newOffset = dataMs.Position;
            newTableOffsets[block.ArrayPos] = newOffset;

            byte[] blockBytes = builtBlocks[block.ArrayPos];
            dataWriter.Write(blockBytes);
            newTableSizes[block.ArrayPos] = blockBytes.Length;

            long aligned = Align16(dataMs.Position);
            while (dataMs.Position < aligned)
                dataWriter.Write((byte)0);

            readCursor = origBlockStart + block.TableSize;

            long origAligned = Align16(origBlockStart + block.TableSize);
            if (origAligned > readCursor)
                readCursor = origAligned;
        }

        long trailingStart = dataRegionStart + readCursor;
        if (trailingStart < oldBuf.Length)
        {
            int trailingLen = (int)(oldBuf.Length - trailingStart);
            dataWriter.Write(oldBuf, (int)trailingStart, trailingLen);
        }

        byte[] newDataRegion = dataMs.ToArray();

        int headerRegionLen = (int)dataRegionStart; 
        int newTotalLen = headerRegionLen + newDataRegion.Length;
        byte[] newBuf = new byte[newTotalLen];

        Array.Copy(oldBuf, 0, newBuf, 0, Math.Min(headerRegionLen, oldBuf.Length));

        Array.Copy(newDataRegion, 0, newBuf, headerRegionLen, newDataRegion.Length);

        for (int t = 0; t < st.TablesCount; t++)
        {
            long entryAbs = st.TableInfoBase + (long)t * BLOCK_STRUCT_SIZE;

            long newOffset = newTableOffsets[t];
            long newSize = newTableSizes[t];

            BitConverter.TryWriteBytes(newBuf.AsSpan((int)(entryAbs + BTE_TABLE_SIZE), 8), newSize);
            BitConverter.TryWriteBytes(newBuf.AsSpan((int)(entryAbs + BTE_RESERVED), 8), newSize);
            BitConverter.TryWriteBytes(newBuf.AsSpan((int)(entryAbs + BTE_TABLE_OFFSET), 8), newOffset);
        }

        BitConverter.TryWriteBytes(newBuf.AsSpan(0, 8), (long)newTotalLen);

        st.Export.Extras = newBuf;
    }

    private void RebuildInPlace(ParsedState st, byte[] oldBuf, byte[][] builtBlocks)
    {
        byte[] newBuf = (byte[])oldBuf.Clone();

        for (int t = 0; t < st.TablesCount; t++)
        {
            var block = st.Blocks[t];
            byte[] newBlock = builtBlocks[t];
            if (newBlock == null || newBlock == block.OriginalBlockBytes) continue;

            long absPos = STREAM_START + st.BaseOffset + block.TableOffset;
            long origSz = block.TableSize;

            if (absPos < 0 || absPos >= newBuf.Length) continue;

            int copyLen = (int)Math.Min(newBlock.Length, origSz);
            Array.Copy(newBlock, 0, newBuf, absPos, copyLen);

            long tailStart = absPos + copyLen;
            long tailEnd = absPos + origSz;
            if (tailEnd > tailStart && tailEnd <= newBuf.Length)
                Array.Clear(newBuf, (int)tailStart, (int)(tailEnd - tailStart));
        }

        BitConverter.TryWriteBytes(newBuf.AsSpan(0, 8), (long)newBuf.Length);
        st.Export.Extras = newBuf;
    }

    private byte[] BuildStxtenll(StxtBlock block)
    {
        int count = block.Entries?.Length ?? 0;
        int headerSize = block.HeaderSize;
        int entrySize = block.TableBufSize;
        const int align = 4;

        bool hasChanges = block.Entries != null && block.Entries.Any(e => e is { Modified: true });
        if (!hasChanges && block.OriginalBlockBytes != null)
            return block.OriginalBlockBytes;

        if (block.Entries == null || count == 0)
            return block.OriginalBlockBytes ?? new byte[headerSize];

        var encoded = new byte[count][];
        for (int s = 0; s < count; s++)
        {
            var entry = block.Entries[s];
            if (entry == null) { encoded[s] = new byte[2]; continue; }

            if (entry.Modified)
            {
                string text = entry.Text ?? "";
                byte[] raw = Encoding.Unicode.GetBytes(text);
                var bytes = new byte[raw.Length + 2];
                Array.Copy(raw, bytes, raw.Length);
                encoded[s] = bytes;
            }
            else
            {
                encoded[s] = entry.OriginalBytes ?? new byte[2];
            }
        }

        int stringsStart = headerSize + count * entrySize;
        int cursor = stringsStart;
        var offsets = new int[count];
        for (int s = 0; s < count; s++)
        {
            cursor = Align(cursor, align);
            offsets[s] = cursor;
            cursor += encoded[s].Length;
        }

        int totalSize = Align(cursor, 4);
        byte[] buf = new byte[totalSize];

        BitConverter.TryWriteBytes(buf.AsSpan(0, 8), block.StxtMagic);
        BitConverter.TryWriteBytes(buf.AsSpan(8, 4), block.StxtType);
        BitConverter.TryWriteBytes(buf.AsSpan(12, 4), headerSize);
        BitConverter.TryWriteBytes(buf.AsSpan(16, 4), entrySize);
        BitConverter.TryWriteBytes(buf.AsSpan(20, 4), count);
        BitConverter.TryWriteBytes(buf.AsSpan(24, 8), block.Unko);

        for (int s = 0; s < count; s++)
        {
            int o = headerSize + s * entrySize;
            int idx = block.Entries[s]?.Index ?? s;
            BitConverter.TryWriteBytes(buf.AsSpan(o, 4), idx);
            BitConverter.TryWriteBytes(buf.AsSpan(o + 4, 4), offsets[s]);
        }

        for (int s = 0; s < count; s++)
        {
            byte[] bytes = encoded[s];
            int off = offsets[s];
            if (off + bytes.Length <= buf.Length)
                Array.Copy(bytes, 0, buf, off, bytes.Length);
        }

        return buf;
    }

    private static int Align(int offset, int alignment) => (offset + alignment - 1) & ~(alignment - 1);
    private static long Align16(long offset) => (offset + 15L) & ~15L;
}