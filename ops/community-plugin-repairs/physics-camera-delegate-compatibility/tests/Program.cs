using System.Text.Json;
using LiquidFluid;
using SoftFlesh;
using Studio;

var results = new List<object>(); int failures = 0;
void Check(string n, bool v) { results.Add(new { name = n, passed = v }); if (!v) failures++; }
void Reset() { AppDomain.CurrentDomain.SetData("SoftFlesh.camChainedFor", null); SoftFleshPlugin.Hover = false; Singleton<Studio.Studio>.Instance = null; }
var liquid = new LiquidFluidPlugin(); var soft = new SoftFleshPlugin();
Reset(); liquid.LateUpdate(); soft.LateUpdate(); Check("no_studio_returns", true);
Singleton<Studio.Studio>.Instance = new Studio.Studio(); liquid.LateUpdate(); soft.LateUpdate(); Check("no_camera_returns", true);

foreach (bool priorDeny in new[] { false, true }) foreach (bool priorZoom in new[] { false, true })
{
    Reset(); var camera = new CameraControl();
    CameraControl.NoCtrlFunc deny = () => priorDeny, zoom = () => priorZoom;
    camera.noCtrlCondition = deny; camera.zoomCondition = zoom;
    Singleton<Studio.Studio>.Instance = new Studio.Studio { cameraCtrl = camera };
    liquid = new LiquidFluidPlugin(); soft = new SoftFleshPlugin();
    for (int frame = 0; frame < 120; frame++) { liquid.LateUpdate(); soft.LateUpdate(); }
    Check($"prior_results_preserved_{priorDeny}_{priorZoom}", camera.noCtrlCondition!() == priorDeny && camera.zoomCondition!() == priorZoom);
    Check($"original_delegate_identity_preserved_{priorDeny}_{priorZoom}", ReferenceEquals(camera.noCtrlCondition, deny) && ReferenceEquals(camera.zoomCondition, zoom));
}

foreach (bool liquidFirst in new[] { false, true })
{
    Reset(); var camera = new CameraControl(); camera.noCtrlCondition = () => false; camera.zoomCondition = () => true;
    Singleton<Studio.Studio>.Instance = new Studio.Studio { cameraCtrl = camera };
    liquid = new LiquidFluidPlugin(); soft = new SoftFleshPlugin();
    var guard = new MutableGuard(camera); guard.Attach();
    bool bounded = true;
    for (int frame = 0; frame < 100; frame++)
    {
        if (liquidFirst) { liquid.LateUpdate(); soft.LateUpdate(); } else { soft.LateUpdate(); liquid.LateUpdate(); }
        guard.Attach();
        try { camera.noCtrlCondition!(); } catch (BudgetExceeded) { bounded = false; break; }
    }
    Check("mutable_external_wrapper_bounded_" + liquidFirst, bounded && guard.Calls <= 100);
    Check("mutable_external_wrapper_owner_preserved_" + liquidFirst, ReferenceEquals(camera.noCtrlCondition, guard.CheckDelegate));
}

Reset(); var c0 = new CameraControl { noCtrlCondition = () => false, zoomCondition = () => true };
var st = new Studio.Studio { cameraCtrl = c0 }; Singleton<Studio.Studio>.Instance = st;
liquid = new LiquidFluidPlugin { _showUI = true, _mouseOverWindow = true }; soft = new SoftFleshPlugin(); SoftFleshPlugin.Hover = true;
c0.enabled = false; int writes = c0.Writes; liquid.LateUpdate(); soft.LateUpdate();
liquid._showUI = false; liquid._mouseOverWindow = false; SoftFleshPlugin.Hover = false; liquid.LateUpdate(); soft.LateUpdate();
Check("previously_disabled_camera_not_reenabled", !c0.enabled);
Check("no_camera_enabled_write_on_hover", c0.Writes == writes);
var c1 = new CameraControl { noCtrlCondition = () => true, zoomCondition = () => false };
var deny1 = c1.noCtrlCondition; var zoom1 = c1.zoomCondition; st.cameraCtrl = c1;
for (int i = 0; i < 100; i++) { liquid.LateUpdate(); soft.LateUpdate(); }
Check("new_camera_keeps_prior_values", c1.noCtrlCondition!() && !c1.zoomCondition!());
Check("new_camera_keeps_prior_owners", ReferenceEquals(c1.noCtrlCondition, deny1) && ReferenceEquals(c1.zoomCondition, zoom1));
Check("old_camera_no_extra_touches_after_switch", c0.Writes == writes);

Reset(); var camera2 = new CameraControl { noCtrlCondition = () => false, zoomCondition = () => true };
Singleton<Studio.Studio>.Instance = new Studio.Studio { cameraCtrl = camera2 };
var origDeny = camera2.noCtrlCondition; var origZoom = camera2.zoomCondition;
for (int cycle = 0; cycle < 16; cycle++)
{
    liquid = new LiquidFluidPlugin { _showUI = (cycle % 2 == 0), _mouseOverWindow = true };
    soft = new SoftFleshPlugin();
    for (int frame = 0; frame < 25; frame++) { liquid.LateUpdate(); soft.LateUpdate(); }
    // Removal is represented by stopping updates: no owned callback should need restoration.
    liquid = null!; soft = null!;
}
Check("repeated_lifecycle_leaves_no_owned_delegates", ReferenceEquals(camera2.noCtrlCondition, origDeny) && ReferenceEquals(camera2.zoomCondition, origZoom));
Check("repeated_lifecycle_no_camera_writes", camera2.Writes == 0);
Check("external_result_survives_repeated_lifecycle", !camera2.noCtrlCondition!() && camera2.zoomCondition!());
Console.WriteLine(JsonSerializer.Serialize(new { checks = results.Count, failed = failures, results, actualCil = "both LateUpdate methods and their original closures", runtimeUnity = false }));
return failures == 0 ? 0 : 1;

sealed class BudgetExceeded : Exception { }
sealed class MutableGuard
{
    readonly CameraControl camera;
    CameraControl.NoCtrlFunc? previous;
    public readonly CameraControl.NoCtrlFunc CheckDelegate;
    public int Calls;
    public MutableGuard(CameraControl camera) { this.camera = camera; CheckDelegate = Check; }
    public void Attach()
    {
        if (ReferenceEquals(camera.noCtrlCondition, CheckDelegate)) return;
        previous = camera.noCtrlCondition;
        camera.noCtrlCondition = CheckDelegate;
    }
    bool Check() { if (++Calls > 512) throw new BudgetExceeded(); return previous?.Invoke() ?? false; }
}
