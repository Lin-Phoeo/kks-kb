using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;

namespace PerceptiveHeightCompatibility
{
    internal sealed class CaptureHook
    {
        private readonly Action beforeAction, afterAction, beforeHandler, afterHandler;
        private EventInfo beforeEvent, afterEvent;
        private bool beforeAttached, afterAttached, active;

        public CaptureHook(Action before, Action after)
        {
            beforeAction = before;
            afterAction = after;
            beforeHandler = Before;
            afterHandler = After;
        }

        private static Type Resolve()
        {
            PluginInfo info;
            if (Chainloader.PluginInfos.TryGetValue("com.bepis.bepinex.screenshotmanager", out info) && (object)info.Instance != null)
            {
                Type registered = info.Instance.GetType().Assembly.GetType("Screencap.ScreenshotManager");
                if (registered != null) return registered;
            }
            return Type.GetType("Screencap.ScreenshotManager, KKS_Screencap")
                ?? Type.GetType("Screencap.ScreenshotManager, KK_Screencap")
                ?? Type.GetType("Screencap.ScreenshotManager, Screencap");
        }

        private static EventInfo CaptureEvent(Type type, string name)
        {
            EventInfo value = type.GetEvent(name, BindingFlags.Static | BindingFlags.Public);
            return value != null && value.EventHandlerType == typeof(Action)
                && value.GetAddMethod() != null && value.GetAddMethod().IsStatic
                && value.GetRemoveMethod() != null && value.GetRemoveMethod().IsStatic ? value : null;
        }

        public void Enable()
        {
            if (active) return;
            Disable();
            if (beforeAttached || afterAttached) return;
            try
            {
                Type type = Resolve();
                if (type == null) return;
                beforeEvent = CaptureEvent(type, "OnPreCapture");
                afterEvent = CaptureEvent(type, "OnPostCapture");
                if (beforeEvent == null || afterEvent == null) return;
                beforeAttached = true;
                beforeEvent.AddEventHandler(null, beforeHandler);
                afterAttached = true;
                afterEvent.AddEventHandler(null, afterHandler);
                active = true;
            }
            catch { Disable(); }
        }

        public void Disable()
        {
            active = false;
            if (beforeAttached)
                try { beforeEvent.RemoveEventHandler(null, beforeHandler); beforeAttached = false; } catch { }
            if (afterAttached)
                try { afterEvent.RemoveEventHandler(null, afterHandler); afterAttached = false; } catch { }
            try { afterAction(); } catch { }
        }

        private void Before() { if (active) beforeAction(); }
        private void After() { if (active) afterAction(); }
    }
}
