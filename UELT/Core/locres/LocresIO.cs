using System.IO;
using System.Text;

namespace UELT.Core.locres
{
    internal static class LocresReader
    {
        internal static string ReadUEString(this BinaryReader r)
        {
            int len = r.ReadInt32();
            if (len == 0) return string.Empty;

            string value;
            if (len > 0)
                value = Encoding.ASCII.GetString(r.ReadBytes(len)).TrimEnd('\0');
            else
                value = Encoding.Unicode.GetString(r.ReadBytes(-len * 2)).TrimEnd('\0');

            return AssetHelper.ReplaceBreaklines(value);
        }

        internal static string ReadUEString(this BinaryReader r, out bool wasUnicode)
        {
            int len = r.ReadInt32();
            wasUnicode = len < 0;
            if (len == 0) return string.Empty;

            string value;
            if (len > 0)
                value = Encoding.ASCII.GetString(r.ReadBytes(len)).TrimEnd('\0');
            else
                value = Encoding.Unicode.GetString(r.ReadBytes(-len * 2)).TrimEnd('\0');

            return AssetHelper.ReplaceBreaklines(value);
        }
    }

    internal static class LocresWriter
    {
        internal static void WriteUEString(this BinaryWriter w, string value, bool legacyEmptyString = false, bool forceUnicode = false)
        {
            if (string.IsNullOrEmpty(value))
            {
                if (legacyEmptyString)
                {
                    w.Write(0);
                }
                else
                {
                    w.Write(1);
                    w.Write((byte)0);
                }
                return;
            }

            value = AssetHelper.ReplaceBreaklines(value, true);

            string withNull = value + '\0';

            if (!forceUnicode && IsAscii(withNull))
            {
                byte[] data = Encoding.ASCII.GetBytes(withNull);
                w.Write(data.Length);
                w.Write(data);
            }
            else
            {
                byte[] data = Encoding.Unicode.GetBytes(withNull);
                w.Write(-withNull.Length);
                w.Write(data);
            }
        }

        private static bool IsAscii(string s)
        {
            foreach (char c in s)
                if (c > 127) return false;
            return true;
        }
    }
}