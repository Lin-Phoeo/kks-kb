using System.Text.Json;
using Codex.StudioFontController;
using Codex.StudioPanelTypography;

int checks = 0;
void Check(bool valid, string message) { checks++; if (!valid) throw new Exception(message); }
TypographyValue Value(float size = 14, int style = 0, bool auto = false, float minimum = 12, float maximum = 18)
    => new() { Size = size, Style = style, Automatic = auto, Minimum = minimum, Maximum = maximum };
bool Same(TypographyValue a, TypographyValue b) => a.Size == b.Size && a.Style == b.Style && a.Automatic == b.Automatic && a.Minimum == b.Minimum && a.Maximum == b.Maximum;

foreach (int originalStyle in new[] { 0, 1, 2, 3, 2 | 4 | 16 | 64 })
{
    var original = Value(style: originalStyle);
    var state = new TypographyState(original);
    var actual = original;
    for (int cycle = 0; cycle < 20; cycle++)
    {
        actual = state.Apply(actual, true, 1);
        Check(actual.Size == 15 && actual.Style == (originalStyle | 1), "bold/delta drift");
        for (int redraw = 0; redraw < 10; redraw++)
        {
            state.ExternalSize(actual.Size, actual.Size);
            state.ExternalMinimum(actual.Minimum, actual.Minimum);
            state.ExternalMaximum(actual.Maximum, actual.Maximum);
            state.ExternalStyle(actual.Style, actual.Style);
            state.ExternalAutomatic(actual.Automatic, actual.Automatic);
            actual = state.Apply(actual, true, 1);
            Check(actual.Size == 15 && actual.Minimum == 13 && actual.Maximum == 19 && actual.Style == (originalStyle | 1), "self-assignment double adjustment");
        }
        actual = state.Apply(actual, false, -2);
        Check(actual.Size == 12 && actual.Style == originalStyle, "toggle must derive from original");
        actual = state.Restore(actual);
        Check(Same(actual, original), "restore lost style/range");
    }
}

var dynamicState = new TypographyState(Value());
var dynamicValue = dynamicState.Apply(Value(), true, 1);
dynamicState.ExternalSize(20, dynamicValue.Size); dynamicValue.Size = 20;
dynamicState.ExternalStyle(2, dynamicValue.Style); dynamicValue.Style = 2;
dynamicValue = dynamicState.Apply(dynamicValue, true, 1);
Check(dynamicValue.Size == 21 && dynamicValue.Style == 3, "external size and italic ignored");
dynamicValue = dynamicState.Restore(dynamicValue);
Check(dynamicValue.Size == 20 && dynamicValue.Style == 2, "external base not restored");

var fittedState = new TypographyState(Value(18, 2, true, 12, 18));
var fitted = fittedState.Apply(Value(18, 2, true, 12, 18), true, 2);
fitted.Size = 15; // TMP's internal fitting writes a result, not a user-selected base size.
fittedState.ExternalSize(fitted.Size, fitted.Size);
fitted = fittedState.Apply(fitted, true, 2);
Check(fitted.Size == 20 && fitted.Minimum == 14 && fitted.Maximum == 20, "TMP fit became original size");
fitted = fittedState.Restore(fitted);
Check(fitted.Size == 18 && fitted.Minimum == 12 && fitted.Maximum == 18 && fitted.Automatic, "TMP auto range restore");

var rollbackState = new TypographyState(Value());
var rollback = rollbackState.Apply(Value(), true, 1);
var savedState = rollbackState.Copy(); var savedValue = rollback;
rollback = rollbackState.Apply(rollback, false, 3);
rollbackState = savedState; rollback = savedValue;
rollback = rollbackState.Apply(rollback, true, 1);
Check(rollback.Size == 15 && rollback.Style == 1, "failed switch state rollback drift");
Check(Same(rollbackState.Restore(rollback), Value()), "failed switch original lost");

// Simulate native measure/draw calls against the actual scope object, including nested measurement and exceptions.
var scope = new TypographyScope();
var style = Value(0, 2);
float fixedHeight = 24, padding = 6;
float Measure()
{
    bool outer = scope.Enter(style);
    try
    {
        if (outer) { style.Size = TypographyPolicy.Size(14, 3); style.Style = TypographyPolicy.Style(style.Style, true); }
        float height = style.Size * 1.2f + padding;
        return height;
    }
    finally { if (scope.Exit()) style = scope.Original; }
}
for (int i = 0; i < 1000; i++)
{
    float layout = Measure();
    Check(style.Size == 0 && style.Style == 2, "implicit font size not restored after measure");
    bool outer = scope.Enter(style);
    style.Size = TypographyPolicy.Size(14, 3); style.Style = TypographyPolicy.Style(style.Style, true);
    float nestedMeasure = Measure();
    Check(!scope.Enter(style) && scope.Depth == 2, "nested scope reapplied");
    scope.Exit();
    var clone = scope.Original; // The GUIStyle constructor hook uses this exact original snapshot.
    Check(clone.Size == 0 && clone.Style == 2, "clone inherited temporary typography");
    float draw = style.Size * 1.2f + padding;
    Check(layout == draw && nestedMeasure == draw, "layout and draw disagree");
    if (scope.Exit()) style = scope.Original;
    Check(style.Size == 0 && scope.Depth == 0 && fixedHeight == 24 && padding == 6, "scope changed control layout");
}
scope.Enter(style);
try { style.Size = 17; throw new InvalidOperationException("synthetic draw failure"); }
catch (InvalidOperationException) { }
finally { if (scope.Exit()) style = scope.Original; }
Check(style.Size == 0 && style.Style == 2, "exception finalizer restore");

var input = TypographyPolicy.Adjust(Value(14), true, 3, 14);
Check(input.Size == 14 && input.Style == 1, "input cannot grow past native available height");
Check(TypographyPolicy.Adjust(Value(10), false, -2).Size == 10, "small explicit controls must not be forced larger or smaller");
Check(TypographyPolicy.Adjust(Value(0), false, 3).Size == 0, "implicit GUI size must be resolved by caller");

string sidebarRoot = SidebarTextPolicy.Root;
var header = SidebarTextPolicy.Resolve(sidebarRoot + "01_Add/Image Menu/Text", 180, 30, false, false, false);
var body = SidebarTextPolicy.Resolve(sidebarRoot + "02_Manipulate/Label", 180, 24, false, false, false);
var field = SidebarTextPolicy.Resolve(sidebarRoot + "02_Manipulate/Input/Text", 80, 20, true, false, false);
for (int cycle = 0; cycle < 20; cycle++)
{
    foreach (int delta in new[] { 0, 1, 3, -2, 0 })
    {
        var h = TypographyPolicy.Adjust(Value(header.Size, 1, true, header.Minimum, header.Size), true, delta);
        var b = TypographyPolicy.Adjust(Value(body.Size, 2, true, body.Minimum, body.Size), true, delta);
        var f = TypographyPolicy.Adjust(Value(field.Size, 0, false, field.Minimum, field.Size), true, delta, 14);
        Check(h.Size > b.Size && b.Style == 3 && h.Style == 1, "sidebar hierarchy or italic lost");
        Check(f.Size <= 14 && h.Minimum <= h.Maximum, "sidebar input/range invalid");
        Check(delta != 0 || h.Size == header.Size && b.Size == body.Size, "sidebar cycle did not restore role base");
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { passed = true, checks, cycles = 20,
    scope = "Actual TypographyState, TypographyScope and sidebar policy against synthetic controls; includes explicit setter self-assignment, external changes, rollback and nested measure/draw lifecycle.",
    limitation = "No Unity native measurements, Harmony constructor/setter patch execution, pixels or physical input. Those require the Studio acceptance run." }));
