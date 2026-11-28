namespace UnityEngine
{
    public class Object
    {
        static int next; readonly int id = ++next;
        public int GetInstanceID() => id;
        public static bool operator ==(Object? a, Object? b) => ReferenceEquals(a, b);
        public static bool operator !=(Object? a, Object? b) => !ReferenceEquals(a, b);
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => id;
    }
    public class Behaviour : Object
    {
        bool value = true;
        public int Writes;
        public bool enabled { get => value; set { this.value = value; Writes++; } }
    }
}
public class Singleton<T> { public static T? Instance { get; set; } }
namespace Studio
{
    public sealed class CameraControl : UnityEngine.Behaviour
    {
        public delegate bool NoCtrlFunc();
        public NoCtrlFunc? noCtrlCondition, zoomCondition;
    }
    public sealed class Studio : UnityEngine.Object { public CameraControl? cameraCtrl { get; set; } }
}
namespace LiquidFluid
{
    public sealed class LiquidFluidPlugin : UnityEngine.Object
    {
        public bool _showUI, _showQuick, _showBig, _mouseOverWindow, _camWasOver;
        public Studio.CameraControl.NoCtrlFunc? _ourNoCtrl, _ourZoom;
        public void LateUpdate() => throw new InvalidOperationException("Exact CIL not extracted");
    }
}
namespace SoftFlesh
{
    public sealed class SoftFleshPlugin : UnityEngine.Object
    {
        public Studio.CameraControl? _lastCc;
        public static bool Hover;
        public static bool Vote() => Hover;
        public void LateUpdate() => throw new InvalidOperationException("Exact CIL not extracted");
    }
}
