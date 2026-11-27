using System.Text;
using System.Text.Json;
using Codex.StudioFontController;

var checks = new List<object>();
void Check(bool passed, string name)
{
    checks.Add(new { name, passed });
    if (!passed) throw new InvalidOperationException(name);
}
void Reject(byte[] input, string name)
{
    bool rejected = false;
    try { ChineseFontData.Read(new MemoryStream(input)); }
    catch (InvalidDataException) { rejected = true; }
    catch (IOException) { rejected = true; }
    Check(rejected, name);
}
void U16(byte[] data, int offset, int value) { data[offset] = (byte)(value >> 8); data[offset + 1] = (byte)value; }
void U32(byte[] data, int offset, uint value) { U16(data, offset, (int)(value >> 16)); U16(data, offset + 2, (int)value); }
byte[] NameTable(params string[] names)
{
    byte[][] encoded = names.Select(Encoding.BigEndianUnicode.GetBytes).ToArray();
    int storage = 6 + names.Length * 12;
    byte[] result = new byte[storage + encoded.Sum(value => value.Length)];
    U16(result, 2, names.Length); U16(result, 4, storage);
    int position = 0;
    for (int index = 0; index < names.Length; index++)
    {
        int record = 6 + index * 12;
        U16(result, record, 3); U16(result, record + 2, 1); U16(result, record + 4, index == 0 ? 0x0409 : 0x0804);
        U16(result, record + 6, 1); U16(result, record + 8, encoded[index].Length); U16(result, record + 10, position);
        encoded[index].CopyTo(result, storage + position); position += encoded[index].Length;
    }
    return result;
}
byte[] Cmap12(uint first, uint last, uint glyph = 1)
{
    byte[] result = new byte[40];
    U16(result, 2, 1); U16(result, 4, 3); U16(result, 6, 10); U32(result, 8, 12);
    U16(result, 12, 12); U32(result, 16, 28); U32(result, 24, 1);
    U32(result, 28, first); U32(result, 32, last); U32(result, 36, glyph);
    return result;
}
byte[] Cmap4(bool useArray, bool missing)
{
    int count = 2, length = 16 + 8 * count + (useArray ? 2 : 0);
    byte[] result = new byte[12 + length];
    U16(result, 2, 1); U16(result, 4, 3); U16(result, 6, 1); U32(result, 8, 12);
    U16(result, 12, 4); U16(result, 14, length); U16(result, 18, count * 2);
    U16(result, 26, 0x4e2d); U16(result, 28, 0xffff);
    U16(result, 32, 0x4e2d); U16(result, 34, 0xffff);
    U16(result, 36, useArray ? 0 : missing ? -0x4e2d : 1 - 0x4e2d); U16(result, 38, 1);
    if (useArray) { U16(result, 40, 4); U16(result, 44, missing ? 0 : 9); }
    return result;
}
byte[] Font(byte[] names, byte[] cmap, bool variable = false, int initialOffset = 0)
{
    int count = variable ? 3 : 2, nameOffset = initialOffset + 12 + count * 16, cmapOffset = nameOffset + names.Length;
    byte[] result = new byte[cmapOffset + cmap.Length];
    U32(result, initialOffset, 0x00010000); U16(result, initialOffset + 4, count);
    U32(result, initialOffset + 12, 0x6e616d65); U32(result, initialOffset + 20, (uint)nameOffset); U32(result, initialOffset + 24, (uint)names.Length);
    U32(result, initialOffset + 28, 0x636d6170); U32(result, initialOffset + 36, (uint)cmapOffset); U32(result, initialOffset + 40, (uint)cmap.Length);
    if (variable) U32(result, initialOffset + 44, 0x66766172);
    names.CopyTo(result, nameOffset); cmap.CopyTo(result, cmapOffset);
    return result;
}
ChineseFontData Read(byte[] data) => ChineseFontData.Read(new MemoryStream(data));
var regular = Read(Font(NameTable("Synthetic Chinese", "合成中文"), Cmap12(0x4e00, 0x9fff)));
Check(regular.HasChineseSample, "format 12 covers the complete UI sample");
Check(regular.HasName("Synthetic Chinese") && regular.HasName("合成中文"), "font naming table preserves English and Chinese identities");
Check(!regular.HasName("Synthetic Chinese UI"), "different TTC face name is not an alias");
Check(!Read(Font(NameTable("Latin"), Cmap12(0x20, 0x7e))).HasChineseSample, "Latin-only cmap rejected independently of name");
Check(!Read(Font(NameTable("中文字体"), Cmap12(0x20, 0x7e))).HasChineseSample, "Chinese-looking name cannot bypass coverage");
Check(Read(Font(NameTable("Variable"), Cmap12(0x4e00, 0x9fff), true)).Variable, "variable source classified separately");
Check(ChineseFontData.HasGlyph(Cmap4(false, false), '中'), "format 4 delta maps a Chinese character");
Check(!ChineseFontData.HasGlyph(Cmap4(false, true), '中'), "format 4 zero glyph rejected");
Check(ChineseFontData.HasGlyph(Cmap4(true, false), '中'), "format 4 glyph array maps a Chinese character");
Check(!ChineseFontData.HasGlyph(Cmap4(true, true), '中'), "format 4 missing glyph array entry rejected");
Check(!ChineseFontData.HasGlyph(Cmap4(false, false), '文'), "format 4 absent character rejected");
Check(ChineseFontData.HasGlyph(Cmap12(0x20000, 0x20001), 0x20000), "format 12 supplementary glyph supported by parser");
Check(!ChineseFontData.HasGlyph(Cmap12(0x4e00, 0x4e00, 0), 0x4e00), "format 12 missing glyph zero rejected");
byte[] ttc = Font(NameTable("First Face"), Cmap12(0x4e00, 0x9fff), initialOffset: 16);
U32(ttc, 0, 0x74746366); U32(ttc, 4, 0x00010000); U32(ttc, 8, 1); U32(ttc, 12, 16);
Check(Read(ttc).HasName("First Face"), "TTC first-face directory uses absolute table offsets");
Reject(new byte[4], "truncated header rejected");
byte[] invalidTable = Font(NameTable("Bad"), Cmap12(0x4e00, 0x9fff)); U32(invalidTable, 20, uint.MaxValue);
Reject(invalidTable, "font table offset outside stream rejected");
Check(!ChineseFontProbe.SameBytes(new byte[] { 1, 2 }, new byte[] { 1, 3 }), "GDI fallback face table mismatch rejected");
Check(!ChineseFontProbe.SameBytes(new byte[] { 1 }, null), "missing GDI identity rejected");
Check(ChineseFontProbe.SameBytes(new byte[] { 1, 2 }, new byte[] { 1, 2 }), "identical first-face table accepted");

int reads = 0, probes = 0;
var fixtures = new Dictionary<string, ChineseFontData> {
    ["chinese.ttf"] = regular,
    ["latin.ttf"] = Read(Font(NameTable("Latin"), Cmap12(0x20, 0x7e))),
    ["variable.ttf"] = Read(Font(NameTable("Variable"), Cmap12(0x4e00, 0x9fff), true))
};
var catalog = new ChineseFontCatalog(new Dictionary<string, string> {
    ["Synthetic Chinese"] = "chinese.ttf", ["合成中文"] = "chinese.ttf", ["Synthetic Chinese UI"] = "chinese.ttf",
    ["Latin"] = "latin.ttf", ["Variable"] = "variable.ttf"
}, path => { reads++; return fixtures[path]; }, (family, data) => { probes++; return new ChineseFontProbeResult(true, "synthetic", family); });
Check(catalog.Families.SequenceEqual(new[] { "Synthetic Chinese" }), "one verified raw family per source file");
Check(reads == 3 && probes == 2 && catalog.FileReads == 3 && catalog.NativeProbes == 2, "each actual alias probed once and each source file parsed once");
for (int index = 0; index < 1000; index++) { _ = catalog.Families; _ = catalog.Supports("合成中文", "chinese.ttf"); }
Check(reads == 3 && probes == 2, "repeated UI reads use cached results only");
Check(catalog.Supports("合成中文", "chinese.ttf"), "legacy localized aliases remain recognized");
Check(!catalog.Supports("Synthetic Chinese UI", "chinese.ttf"), "TTC second-face raw identity remains rejected");
var rejectedCatalog = new ChineseFontCatalog(new Dictionary<string, string> { ["Synthetic Chinese"] = "chinese.ttf" }, path => regular,
    (family, data) => new ChineseFontProbeResult(false, "gdi-source-face-differs", "Unexpected Fallback"));
Check(rejectedCatalog.Families.Length == 0, "Chinese fallback cannot impersonate requested face");
var badNames = NameTable("Legacy Names"); U16(badNames, 6, 1);
var partiallyReadable = new ChineseFontCatalog(new Dictionary<string, string> { ["Bad"] = "bad.ttf", ["Synthetic Chinese"] = "good.ttf" },
    path => path == "bad.ttf" ? Read(Font(badNames, Cmap12(0x4e00, 0x9fff))) : regular,
    (family, data) => new ChineseFontProbeResult(true, "synthetic", family));
Check(partiallyReadable.Families.SequenceEqual(new[] { "Synthetic Chinese" }), "unsupported Unicode naming table does not abort the catalog");
var aliasMismatch = new ChineseFontCatalog(new Dictionary<string, string> { ["Synthetic Chinese"] = "one.ttf", ["合成中文"] = "one.ttf" }, path => regular,
    (family, data) => new ChineseFontProbeResult(family == "Synthetic Chinese", "synthetic alias mismatch", family));
Check(aliasMismatch.Supports("Synthetic Chinese", "one.ttf") && !aliasMismatch.Supports("合成中文", "one.ttf"), "one working alias does not approve an unverified alias");
var localizedOnly = new ChineseFontCatalog(new Dictionary<string, string> { ["合成中文"] = "one.ttf" }, path => regular,
    (family, data) => new ChineseFontProbeResult(true, "synthetic actual English family", family));
Check(localizedOnly.Families.SequenceEqual(new[] { "合成中文" }) && localizedOnly.Supports("Synthetic Chinese", "one.ttf") && localizedOnly.NativeProbes == 2,
    "English runtime family receives its own probe when registry only names Chinese family");
Check(localizedOnly.FilterRuntimeFamilies(new[] { "Synthetic Chinese" }).SequenceEqual(new[] { "合成中文" }), "runtime English alias keeps the same verified Chinese choice");
Check(localizedOnly.FilterRuntimeFamilies(new[] { "Unrelated" }).Length == 0, "runtime missing verified family removes the choice");
Check(localizedOnly.RuntimeFamilyForPath("one.ttf", new HashSet<string>(new[] { "Synthetic Chinese" })) == "Synthetic Chinese", "list and runtime loader use the same verified alias set");
Check(ChineseFontCatalog.DisplayName("Microsoft YaHei UI").StartsWith("微软雅黑（界面版）"), "YaHei UI is named distinctly");
Check(ChineseFontCatalog.DisplayName("Microsoft JhengHei").StartsWith("微软正黑体"), "JhengHei has accurate Chinese name");
Check(ChineseFontCatalog.DisplayName("Microsoft JhengHei UI").StartsWith("微软正黑体（界面版）"), "JhengHei UI retains its distinction");
Check(ChineseFontCatalog.DisplayName("Noto Sans SC").StartsWith("Noto 黑体简中"), "Noto is not mislabeled as Source Han");
Check(ChineseFontCatalog.DisplayName("DengXian Light").StartsWith("等线（细体）"), "style suffix localized without altering identity");
Check(ChineseFontCatalog.DisplayName("NSimSun").StartsWith("新宋体"), "NSimSun keeps its own name");
Check(ChineseFontCatalog.DisplayName("Unknown Family") == "Unknown Family", "unknown actual identity is retained");
Check(ChineseFontCatalog.DisplayName("Microsoft YaHei").EndsWith(" / Microsoft YaHei"), "friendly labels retain raw identity");
Check(ChineseFontCatalog.DisplayName("Arial Unicode MS") == "Arial 多语言字体 / Arial Unicode MS", "Arial Unicode uses an accurate descriptive label");
Check(ChineseFontCatalog.DisplayName("yyb Sans Regular") == "yyb Sans（常规） / yyb Sans Regular", "author family name retained with localized regular style");
foreach (var mapping in new Dictionary<string, string> {
    ["STZhongsong"] = "华文中宋", ["STSong"] = "华文宋体", ["STKaiti"] = "华文楷体",
    ["STLiti"] = "华文隶书", ["STCaiyun"] = "华文彩云", ["STFangsong"] = "华文仿宋",
    ["STHupo"] = "华文琥珀", ["STXinwei"] = "华文新魏", ["STXingkai"] = "华文行楷"
}) Check(ChineseFontCatalog.DisplayName(mapping.Key) == mapping.Value + " / " + mapping.Key, "actual Huawen runtime family " + mapping.Key + " uses its Chinese family name");
Check(ChineseFontCatalog.DisplayName("STSong Alternate") == "华文宋体（Alternate） / STSong Alternate", "unknown style suffix is retained without a guessed translation");
Console.WriteLine(JsonSerializer.Serialize(new { passed = true, checks = checks.Count, results = checks,
    limitation = "Synthetic in-memory SFNT/name/cmap fixtures and injected probe only. No registry, installed font, game installation or native GDI calls." }, new JsonSerializerOptions { WriteIndented = true }));
