using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Codex.StudioFontController
{
    internal sealed class ChineseFontData
    {
        internal const string ProbeText = "中文工作室字体设置保存载入确定取消颜色材质骨骼旋转缩放阴影灯光表情动画播放停止相机目标选择调整显示隐藏复制粘贴删除恢复默认粗细大小位置透明度背景编辑器预览搜索";
        internal readonly byte[] NameTable;
        internal readonly string[] Names;
        internal readonly string PreferredFamily;
        internal readonly bool Variable, HasChineseSample;

        private ChineseFontData(byte[] names, byte[] cmap, bool variable)
        {
            NameTable = names;
            string preferred;
            Names = ReadNames(names, out preferred);
            PreferredFamily = preferred;
            Variable = variable;
            HasChineseSample = true;
            foreach (char character in ProbeText)
                if (!HasGlyph(cmap, character)) { HasChineseSample = false; break; }
        }

        internal bool HasName(string name)
        {
            foreach (string candidate in Names)
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static ChineseFontData Read(string path)
        {
            using (var stream = File.OpenRead(path)) return Read(stream);
        }

        internal static ChineseFontData Read(Stream stream)
        {
            byte[] header = ReadAt(stream, 0, 12);
            long faceOffset = 0;
            if (U32(header, 0) == 0x74746366)
            {
                uint faces = U32(header, 8);
                if (faces == 0 || faces > 4096) throw new InvalidDataException("Invalid font collection");
                faceOffset = U32(ReadAt(stream, 12, 4), 0);
                header = ReadAt(stream, faceOffset, 12);
            }
            uint signature = U32(header, 0);
            if (signature != 0x00010000 && signature != 0x4f54544f && signature != 0x74727565)
                throw new InvalidDataException("Unsupported font header");
            int count = U16(header, 4);
            if (count == 0 || count > 256) throw new InvalidDataException("Invalid font table count");
            byte[] directory = ReadAt(stream, faceOffset + 12, count * 16);
            byte[] names = null, cmap = null;
            bool variable = false;
            for (int index = 0; index < count; index++)
            {
                int entry = index * 16;
                uint tag = U32(directory, entry);
                if (tag == 0x66766172) variable = true;
                if (tag != 0x6e616d65 && tag != 0x636d6170) continue;
                uint size = U32(directory, entry + 12);
                if (size > 16 * 1024 * 1024) throw new InvalidDataException("Font table is too large");
                byte[] data = ReadAt(stream, U32(directory, entry + 8), (int)size);
                if (tag == 0x6e616d65) names = data; else cmap = data;
            }
            if (names == null || cmap == null) throw new InvalidDataException("Font identity or character map missing");
            return new ChineseFontData(names, cmap, variable);
        }

        private static byte[] ReadAt(Stream stream, long offset, int count)
        {
            if (offset < 0 || count < 0 || offset > stream.Length - count) throw new InvalidDataException("Truncated font data");
            stream.Position = offset;
            byte[] data = new byte[count];
            int read = 0;
            while (read < count)
            {
                int next = stream.Read(data, read, count - read);
                if (next == 0) throw new EndOfStreamException();
                read += next;
            }
            return data;
        }

        internal static string[] ReadNames(byte[] data, out string preferred)
        {
            int count = U16(data, 2), storage = U16(data, 4);
            if (count > 4096 || 6L + count * 12 > data.Length) throw new InvalidDataException("Truncated name records");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            preferred = null;
            for (int index = 0; index < count; index++)
            {
                int record = 6 + index * 12;
                int platform = U16(data, record), encoding = U16(data, record + 2), language = U16(data, record + 4);
                int id = U16(data, record + 6), size = U16(data, record + 8), start = storage + U16(data, record + 10);
                if ((platform != 0 && !(platform == 3 && (encoding == 1 || encoding == 10))) || (id != 1 && id != 4 && id != 16)) continue;
                if ((size & 1) != 0 || start < storage || start > data.Length - size) throw new InvalidDataException("Invalid font name span");
                string name = Encoding.BigEndianUnicode.GetString(data, start, size).TrimEnd('\0');
                if (name.Length == 0) continue;
                names.Add(name);
                if (preferred == null || (id == 1 && language == 0x0409)) preferred = name;
            }
            if (names.Count == 0) throw new InvalidDataException("No Unicode font family name");
            var result = new string[names.Count];
            names.CopyTo(result);
            return result;
        }

        internal static bool HasGlyph(byte[] cmap, int codepoint)
        {
            int count = U16(cmap, 2);
            if (4L + count * 8 > cmap.Length) throw new InvalidDataException("Truncated cmap directory");
            for (int index = 0; index < count; index++)
            {
                int record = 4 + index * 8, platform = U16(cmap, record), encoding = U16(cmap, record + 2);
                if (platform != 0 && !(platform == 3 && (encoding == 1 || encoding == 10))) continue;
                uint offset = U32(cmap, record + 4);
                if (offset > int.MaxValue || offset > cmap.Length - 2) throw new InvalidDataException("Invalid cmap offset");
                int start = (int)offset, format = U16(cmap, start);
                if (format == 4 && codepoint <= 0xffff && Format4(cmap, start, codepoint)) return true;
                if ((format == 12 || format == 13) && Format12(cmap, start, codepoint, format == 13)) return true;
            }
            return false;
        }

        private static bool Format4(byte[] data, int start, int codepoint)
        {
            int length = U16(data, start + 2), doubledCount = U16(data, start + 6), count = doubledCount / 2;
            if ((doubledCount & 1) != 0 || count == 0 || length < 16L + count * 8 || start > data.Length - length)
                throw new InvalidDataException("Invalid cmap format 4");
            int ends = start + 14, starts = ends + count * 2 + 2, deltas = starts + count * 2, offsets = deltas + count * 2;
            for (int index = 0; index < count; index++)
            {
                int low = U16(data, starts + index * 2), high = U16(data, ends + index * 2);
                if (codepoint < low || codepoint > high) continue;
                int delta = U16(data, deltas + index * 2), range = U16(data, offsets + index * 2);
                if (range == 0) return ((codepoint + delta) & 0xffff) != 0;
                long address = (long)offsets + index * 2 + range + (codepoint - low) * 2;
                if (address < start || address > (long)start + length - 2) throw new InvalidDataException("Invalid cmap glyph address");
                int glyph = U16(data, (int)address);
                return glyph != 0 && ((glyph + delta) & 0xffff) != 0;
            }
            return false;
        }

        private static bool Format12(byte[] data, int start, int codepoint, bool constant)
        {
            uint length = U32(data, start + 4), count = U32(data, start + 12);
            if (length < 16L + count * 12L || length > data.Length - start) throw new InvalidDataException("Invalid grouped cmap");
            int low = 0, high = (int)count - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2, entry = start + 16 + middle * 12;
                uint first = U32(data, entry), last = U32(data, entry + 4), glyph = U32(data, entry + 8);
                if (codepoint < first) high = middle - 1;
                else if (codepoint > last) low = middle + 1;
                else return (constant ? glyph : glyph + (uint)codepoint - first) != 0;
            }
            return false;
        }

        private static int U16(byte[] data, int offset)
        {
            if (offset < 0 || offset > data.Length - 2) throw new InvalidDataException("Truncated font field");
            return (data[offset] << 8) | data[offset + 1];
        }
        private static uint U32(byte[] data, int offset)
        {
            return ((uint)U16(data, offset) << 16) | (uint)U16(data, offset + 2);
        }
    }
}
