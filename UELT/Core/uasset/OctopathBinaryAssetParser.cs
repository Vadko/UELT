using System.Buffers.Binary;
using System.IO;
using System.Text;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;

namespace UELT.Core.uasset;

public class OctopathBinaryAssetParser
{
    private ParsedState _state;
    private readonly Dictionary<int, TextEntry> _indexMap = new();

    #region Внутрішні типи
    private class ParsedState
    {
        public NormalExport Export = null!;
        public OctopathNode Root = null!;
    }

    private class TextEntry
    {
        public OctopathStringNode Node = null!;
    }

    private abstract class OctopathNode { }

    private class OctopathNullNode : OctopathNode { }

    private class OctopathBoolNode(bool value) : OctopathNode
    {
        public bool Value = value;
    }

    private class OctopathIntNode(long value) : OctopathNode
    {
        public long Value = value;
    }

    private class OctopathFloatNode(double value) : OctopathNode
    {
        public double Value = value;
        public bool IsDouble;
    }

    private class OctopathStringNode(string value) : OctopathNode
    {
        public string Value = value;
        public bool Modified;
    }

    private class OctopathArrayNode(List<OctopathNode> items) : OctopathNode
    {
        public List<OctopathNode> Items = items;
    }

    private class OctopathMapNode(List<(string Key, OctopathNode Value)> fields) : OctopathNode
    {
        public List<(string Key, OctopathNode Value)> Fields = fields;
    }
    #endregion

    #region Публічний API
    public static bool IsOctopathBinaryAsset(NormalExport export)
    {
        if (export?.Data == null) return false;
        return export.Data.Any(prop => prop.Name?.ToString() == "BinaryData");
    }

    public void Extract(NormalExport export, ref int startIndex, List<List<string>> result)
    {
        _indexMap.Clear();

        byte[] binaryData = null;
        foreach (var prop in export.Data)
        {
            if (prop.Name?.ToString() == "BinaryData" && prop is ArrayPropertyData arrProp && arrProp.Value != null)
            {
                var bytes = new List<byte>();
                foreach (var item in arrProp.Value)
                    if (item is BytePropertyData byteProp)
                        bytes.Add(byteProp.Value);
                binaryData = bytes.ToArray();
                break;
            }
        }

        if (binaryData == null || binaryData.Length == 0) return;

        using var ms = new MemoryStream(binaryData);
        using var reader = new BinaryReader(ms);

        OctopathNode root;
        try
        {
            root = ReadNode(reader);
        }
        catch (Exception ex)
        {
            throw new Exception($"Не вдалося розпарсити msgpack:\n{ex.Message}", ex);
        }

        _state = new ParsedState
        {
            Export = export,
            Root = root,
        };

        CollectStrings(root, "", ref startIndex, result);
    }

    public void ApplyText(int globalIndex, string newText)
    {
        if (_state == null) return;
        if (_indexMap.TryGetValue(globalIndex, out var entry))
        {
            entry.Node.Value = newText;
            entry.Node.Modified = true;
        }
    }

    public void Rebuild()
    {
        if (_state == null) return;

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        WriteNode(writer, _state.Root);
        byte[] newBinaryData = ms.ToArray();

        foreach (var prop in _state.Export.Data)
        {
            if (prop.Name?.ToString() == "BinaryData" && prop is ArrayPropertyData arrProp)
            {
                var newBytes = new BytePropertyData[newBinaryData.Length];
                for (int i = 0; i < newBinaryData.Length; i++)
                {
                    newBytes[i] = new BytePropertyData();
                    newBytes[i].Value = newBinaryData[i];
                    newBytes[i].ByteType = BytePropertyType.Byte;
                }
                arrProp.Value = newBytes;
                break;
            }
        }

        _state.Export.SerialSize = _state.Export.Extras.Length;
    }
    #endregion

    #region Десеріалізація
    private static OctopathNode ReadNode(BinaryReader r)
    {
        byte b = r.ReadByte();

        if (b == 0xc2) return new OctopathBoolNode(false);
        if (b == 0xc3) return new OctopathBoolNode(true);
        if (b == 0xc0) return new OctopathNullNode();
        if (b <= 0x7f) return new OctopathIntNode(b);
        if (b >= 0xe0) return new OctopathIntNode((sbyte)b);

        if (b == 0xd2)
        {
            int v = BinaryPrimitives.ReadInt32BigEndian(r.ReadBytes(4));
            return new OctopathIntNode(v);
        }

        if (b == 0xca)
        {
            float f = BinaryPrimitives.ReadSingleBigEndian(r.ReadBytes(4));
            return new OctopathFloatNode(f) { IsDouble = false };
        }
        if (b == 0xcb)
        {
            double d = BinaryPrimitives.ReadDoubleBigEndian(r.ReadBytes(8));
            return new OctopathFloatNode(d) { IsDouble = true };
        }
        if (b == 0xcc) return new OctopathIntNode(r.ReadByte());
        if (b == 0xcd) return new OctopathIntNode(BinaryPrimitives.ReverseEndianness(r.ReadUInt16()));
        if (b == 0xce) return new OctopathIntNode(BinaryPrimitives.ReverseEndianness(r.ReadUInt32()));
        if (b == 0xcf) return new OctopathIntNode((long)BinaryPrimitives.ReverseEndianness(r.ReadUInt64()));
        if (b == 0xd0) return new OctopathIntNode(r.ReadSByte());
        if (b == 0xd1) return new OctopathIntNode(BinaryPrimitives.ReverseEndianness(r.ReadInt16()));
        if (b == 0xd3) return new OctopathIntNode(BinaryPrimitives.ReverseEndianness(r.ReadInt64()));

        if ((b >= 0xa0 && b <= 0xbf) || b == 0xd9 || b == 0xda || b == 0xdb)
        {
            r.BaseStream.Position--;
            return new OctopathStringNode(ReadString(r));
        }

        if ((b >= 0x90 && b <= 0x9f) || b == 0xdc || b == 0xdd)
        {
            r.BaseStream.Position--;
            return ReadArray(r);
        }

        if ((b >= 0x80 && b <= 0x8f) || b == 0xde || b == 0xdf)
        {
            r.BaseStream.Position--;
            return ReadMap(r);
        }

        throw new InvalidDataException($"Невідомий байт типу 0x{b:X2}");
    }

    private static string ReadString(BinaryReader r)
    {
        byte b = r.ReadByte();
        int length = b switch
        {
            >= 0xa0 and <= 0xbf => b & 0x1f,
            0xd9 => r.ReadByte(),
            0xda => BinaryPrimitives.ReverseEndianness(r.ReadUInt16()),
            0xdb => (int)BinaryPrimitives.ReverseEndianness(r.ReadUInt32()),
            _ => throw new InvalidDataException($"ReadString: невідомий байт 0x{b:X2}"),
        };
        return Encoding.UTF8.GetString(r.ReadBytes(length));
    }

    private static OctopathArrayNode ReadArray(BinaryReader r)
    {
        byte b = r.ReadByte();
        int length = b switch
        {
            >= 0x90 and <= 0x9f => b & 0x0f,
            0xdc => BinaryPrimitives.ReverseEndianness(r.ReadUInt16()),
            0xdd => (int)BinaryPrimitives.ReverseEndianness(r.ReadUInt32()),
            _ => throw new InvalidDataException($"ReadArray: невідомий байт 0x{b:X2}"),
        };

        var items = new List<OctopathNode>(length);
        for (int i = 0; i < length; i++)
            items.Add(ReadNode(r));

        return new OctopathArrayNode(items);
    }

    private static OctopathMapNode ReadMap(BinaryReader r)
    {
        byte b = r.ReadByte();
        int count = b switch
        {
            >= 0x80 and <= 0x8f => b & 0x0f,
            0xde => BinaryPrimitives.ReverseEndianness(r.ReadUInt16()),
            0xdf => (int)BinaryPrimitives.ReverseEndianness(r.ReadUInt32()),
            _ => throw new InvalidDataException($"ReadMap: невідомий байт 0x{b:X2}"),
        };

        var fields = new List<(string, OctopathNode)>(count);
        for (int i = 0; i < count; i++)
        {
            string key = ReadString(r);
            OctopathNode val = ReadNode(r);
            fields.Add((key, val));
        }

        return new OctopathMapNode(fields);
    }

    #endregion

    #region Збір рядків
    private void CollectStrings(OctopathNode node, string path, ref int index, List<List<string>> result)
    {
        switch (node)
        {
            case OctopathStringNode strNode when !string.IsNullOrEmpty(strNode.Value):
                if (!string.IsNullOrEmpty(strNode.Value))
                {
                    result.Add(new List<string> { path, AssetHelper.ReplaceBreaklines(strNode.Value) });
                    _indexMap[index] = new TextEntry { Node = strNode };
                    index++;
                }
                break;

            case OctopathArrayNode arrNode:
                for (int i = 0; i < arrNode.Items.Count; i++)
                {
                    CollectStrings(arrNode.Items[i], $"{path}[{i}]", ref index, result);
                }
                break;

            case OctopathMapNode mapNode:
                string id = null;
                string voiceId = null;

                foreach (var (key, val) in mapNode.Fields)
                {
                    if (key == "m_id" && val is OctopathIntNode intNode)
                        id = intNode.Value.ToString();
                    else if (key == "m_voiceId" && val is OctopathIntNode voiceNode)
                        voiceId = voiceNode.Value.ToString();
                }

                string basePath = path;
                if (id != null)
                {
                    basePath = voiceId != null ? $"{id}_{voiceId}" : id;
                }

                foreach (var (key, val) in mapNode.Fields)
                {
                    if (key == "m_id" || key == "m_voiceId") continue;

                    string childPath;
                    if (key == "m_gametext" && id != null)
                    {
                        if (val is OctopathArrayNode arrNode && arrNode.Items.Count > 0)
                        {
                            for (int i = 0; i < arrNode.Items.Count; i++)
                            {
                                if (arrNode.Items[i] is OctopathStringNode strNode && !string.IsNullOrEmpty(strNode.Value))
                                {
                                    CollectStrings(strNode, basePath, ref index, result);
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        childPath = string.IsNullOrEmpty(basePath) ? key : $"{basePath}.{key}";
                        CollectStrings(val, childPath, ref index, result);
                    }
                }
                break;
        }
    }
    #endregion

    #region Серіалізація
    private static void WriteNode(BinaryWriter w, OctopathNode node)
    {
        switch (node)
        {
            case OctopathNullNode:
                w.Write((byte)0xc0);
                break;

            case OctopathBoolNode boolNode:
                w.Write(boolNode.Value ? (byte)0xc3 : (byte)0xc2);
                break;

            case OctopathIntNode intNode:
                {
                    long v = intNode.Value;
                    if (v >= 0 && v <= 0x7f)
                        w.Write((byte)v);
                    else if (v < 0 && v >= -32)
                        w.Write((byte)(sbyte)v);
                    else if (v >= int.MinValue && v <= int.MaxValue)
                    {
                        w.Write((byte)0xd2);
                        WriteInt32BE(w, (int)v);
                    }
                    else
                    {
                        w.Write((byte)0xd3);
                        WriteInt64BE(w, v);
                    }
                    break;
                }

            case OctopathFloatNode floatNode:
                if (floatNode.IsDouble)
                {
                    w.Write((byte)0xcb);
                    WriteDoubleBE(w, floatNode.Value);
                }
                else
                {
                    w.Write((byte)0xca);
                    WriteFloatBE(w, (float)floatNode.Value);
                }
                break;

            case OctopathStringNode strNode:
                WriteString(w, strNode.Value);
                break;

            case OctopathArrayNode arrNode:
                {
                    int len = arrNode.Items.Count;
                    if (len <= 15)
                        w.Write((byte)(0x90 | len));
                    else if (len <= 0xffff)
                    {
                        w.Write((byte)0xdc);
                        WriteUInt16BE(w, (ushort)len);
                    }
                    else
                    {
                        w.Write((byte)0xdd);
                        WriteUInt32BE(w, (uint)len);
                    }
                    foreach (var item in arrNode.Items)
                        WriteNode(w, item);
                    break;
                }

            case OctopathMapNode mapNode:
                {
                    int count = mapNode.Fields.Count;
                    if (count <= 15)
                        w.Write((byte)(0x80 | count));
                    else if (count <= 0xffff)
                    {
                        w.Write((byte)0xde);
                        WriteUInt16BE(w, (ushort)count);
                    }
                    else
                    {
                        w.Write((byte)0xdf);
                        WriteUInt32BE(w, (uint)count);
                    }
                    foreach (var (key, val) in mapNode.Fields)
                    {
                        WriteString(w, key);
                        WriteNode(w, val);
                    }
                    break;
                }
        }
    }

    private static void WriteString(BinaryWriter w, string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        int len = bytes.Length;
        if (len <= 31)
            w.Write((byte)(0xa0 | len));
        else if (len <= 0xff)
        {
            w.Write((byte)0xd9);
            w.Write((byte)len);
        }
        else if (len <= 0xffff)
        {
            w.Write((byte)0xda);
            WriteUInt16BE(w, (ushort)len);
        }
        else
        {
            w.Write((byte)0xdb);
            WriteUInt32BE(w, (uint)len);
        }
        w.Write(bytes);
    }
    #endregion

    #region Big-endian хелпери
    private static void WriteInt32BE(BinaryWriter w, int v)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buf, v);
        w.Write(buf);
    }

    private static void WriteInt64BE(BinaryWriter w, long v)
    {
        var buf = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buf, v);
        w.Write(buf);
    }

    private static void WriteUInt16BE(BinaryWriter w, ushort v)
    {
        var buf = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buf, v);
        w.Write(buf);
    }

    private static void WriteUInt32BE(BinaryWriter w, uint v)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buf, v);
        w.Write(buf);
    }

    private static void WriteFloatBE(BinaryWriter w, float v)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(buf, v);
        w.Write(buf);
    }

    private static void WriteDoubleBE(BinaryWriter w, double v)
    {
        var buf = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(buf, v);
        w.Write(buf);
    }
    #endregion
}