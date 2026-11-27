using Codex.StudioFontController;
using System.Text.Json;

int checks = 0;
void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
var families = new[] { "Microsoft YaHei", "Microsoft JhengHei", "KaiTi", "DengXian" };
var labels = new[] { "微软雅黑 / Microsoft YaHei", "微软正黑体 / Microsoft JhengHei", "楷体 / KaiTi", "等线 / DengXian" };
var picker = new FontPickerState();
picker.Layout(families, labels, false, false, "legacy" );
Check(!picker.Ready && !picker.Expanded, "loading keeps initial layout stable");
picker.PendingExpanded = true;
Check(!picker.Expanded, "click defers structural change until next layout");
picker.Layout(families, labels, true, false, "Microsoft YaHei");
Check(picker.Ready && picker.Expanded && picker.CurrentCompatible && picker.Rows.Length == 4, "ready snapshot shows compatible list");
picker.Layout(families, labels, true, false, "微软雅黑", "Microsoft YaHei");
Check(picker.CurrentCompatible && picker.CurrentLabel == "微软雅黑" && picker.SelectedFamily == "Microsoft YaHei", "approved alias highlights canonical row without rewriting stored value");
int[] originalRows = picker.Rows;
picker.DraftQuery = "正黑";
Check(ReferenceEquals(originalRows, picker.Rows) && picker.Rows.Length == 4, "input event does not change repaint control count");
picker.Layout(families, labels, true, false, "Microsoft YaHei");
Check(picker.Rows.SequenceEqual(new[] { 1 }), "Chinese search keeps raw row identity");
picker.DraftQuery = "  yAhEi ";
picker.Layout(families, labels, true, false, "Microsoft YaHei");
Check(picker.Rows.SequenceEqual(new[] { 0 }), "case-insensitive original family search");
picker.DraftQuery = "不存在";
picker.Layout(families, labels, true, false, "legacy");
Check(picker.Rows.Length == 0 && !picker.CurrentCompatible && picker.CurrentLabel == "legacy", "empty search never rewrites old selection");
picker.DraftQuery = "";
picker.Layout(families, labels, true, false, "KaiTi");
Check(picker.Rows.Length == 4 && picker.CurrentCompatible, "clear restores entire list");
var cached = picker.Rows;
for (int i = 0; i < 1000; i++)
{
    picker.Layout(families, labels, true, false, "KaiTi");
    Check(ReferenceEquals(cached, picker.Rows), "unchanged layout does not rebuild results");
}
picker.PendingExpanded = false;
Check(picker.Expanded, "selection keeps current event structure");
picker.Layout(families, labels, true, false, "DengXian");
Check(!picker.Expanded && picker.CurrentLabel == "DengXian", "selection closes on next layout");
picker.Layout(Array.Empty<string>(), Array.Empty<string>(), false, true, "DengXian");
Check(!picker.Ready && picker.Failed && picker.Rows.Length == 0 && picker.CurrentLabel == "DengXian", "catalog failure preserves stored identity");
bool rejected = false;
try { FontPickerState.Filter(families, Array.Empty<string>(), ""); } catch (ArgumentException) { rejected = true; }
Check(rejected, "display identities cannot silently shift");
Console.WriteLine(JsonSerializer.Serialize(new { passed = true, checks, scope = "Actual font picker snapshot/filter state with synthetic identities. No GUI rendering or installed font access." }));
