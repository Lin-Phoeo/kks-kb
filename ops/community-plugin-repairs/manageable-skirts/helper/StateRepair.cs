using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Studio;
using UnityEngine;

namespace Codex.ManageableSkirtsState
{
    public static class StateRepair
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly ConditionalWeakTable<object, Runtime> States = new ConditionalWeakTable<object, Runtime>();
        private static readonly FieldInfo Normal = typeof(GuideSelect).GetField("colorNormal", All);
        private static readonly FieldInfo Highlight = typeof(GuideSelect).GetField("colorHighlighted", All);
        private static readonly Color[] Colors = { new Color(1,0,0,.75f), new Color(1,.5f,0,.75f), new Color(1,1,0,.75f), new Color(0,1,0,.75f), new Color(0,.8f,1,.75f), new Color(0,0,1,.75f), new Color(.7f,0,1,.75f), new Color(1,.2f,.7f,.75f) };

        private sealed class Runtime
        {
            internal readonly object Plugin;
            internal readonly Type DataType;
            internal readonly FieldInfo DataCharacter, DataSpine;
            internal readonly IDictionary Markers;
            internal readonly ConfigEntry<bool> Coloring, Depth, Tails;
            internal readonly ConfigEntry<KeyboardShortcut> Hotkey;
            internal readonly MethodInfo Toggle, Selected;
            internal readonly ManualLogSource Log;
            internal readonly Dictionary<OCIChar, Session> Sessions = new Dictionary<OCIChar, Session>();
            internal float Elapsed;
            internal Runtime(object plugin)
            {
                Plugin = plugin; Type type = plugin.GetType();
                DataType = type.GetNestedType("CharData", BindingFlags.NonPublic);
                DataCharacter = DataType.GetField("ociChar", All); DataSpine = DataType.GetField("spine", All);
                Markers = (IDictionary)type.GetField("tracked", All).GetValue(plugin);
                Coloring = (ConfigEntry<bool>)type.GetField("enableColoring", All).GetValue(plugin);
                Depth = (ConfigEntry<bool>)type.GetField("enableDepthHiding", All).GetValue(plugin);
                Tails = (ConfigEntry<bool>)type.GetField("enableHideTailNodes", All).GetValue(plugin);
                Hotkey = (ConfigEntry<KeyboardShortcut>)type.GetField("toggleHotkey", All).GetValue(plugin);
                Toggle = type.GetMethod("ToggleSelectedCharacter", All); Selected = type.GetMethod("FindSelectedCharacter", All);
                Log = (ManualLogSource)typeof(BaseUnityPlugin).GetProperty("Logger", All).GetValue(plugin, null);
                if (Normal == null || Highlight == null) throw new MissingFieldException("GuideSelect color fields differ");
            }
        }
        private sealed class Session
        {
            internal readonly OCIChar Character;
            internal readonly ChaControl Control;
            internal readonly Animator Body;
            internal readonly Transform Spine;
            internal readonly Dictionary<GuideObject, GuideState> Guides = new Dictionary<GuideObject, GuideState>();
            internal bool[] Hidden = new bool[8];
            internal Session(OCIChar character, Transform spine) { Character=character; Control=character.charInfo; Body=character.charInfo.animBody; Spine=spine; }
            internal bool Alive { get { return Control && Body && Spine && Character.charInfo == Control && Control.animBody == Body; } }
        }
        private sealed class Owned<T>
        {
            internal T Original, Last;
            internal bool Active, Yielded, Touched;
            internal void Apply(T current, T wanted, Action<T> write)
            {
                if (Active && !EqualityComparer<T>.Default.Equals(current, Last)) { Active=false; Yielded=true; }
                if (Yielded || EqualityComparer<T>.Default.Equals(current, wanted)) return;
                if (!Active) Original=current;
                write(wanted); Last=wanted; Active=true; Touched=true;
            }
            internal void Restore(T current, Action<T> write)
            {
                if (Active && EqualityComparer<T>.Default.Equals(current, Last)) write(Original);
                Active=false;
            }
        }
        private sealed class GuideState
        {
            internal readonly GuideObject Guide;
            internal readonly GuideSelect Select;
            internal readonly Material Material;
            internal readonly Owned<bool> Visibility = new Owned<bool>();
            internal readonly Owned<Color> NormalColor = new Owned<Color>(), HighlightColor = new Owned<Color>(), ShownColor = new Owned<Color>(), Emission = new Owned<Color>();
            internal bool ColorApplied;
            internal bool HasChanges { get { return Visibility.Touched || NormalColor.Touched || HighlightColor.Touched || ShownColor.Touched || Emission.Touched; } }
            internal Color AppliedNormal, AppliedHighlight;
            internal GuideState(GuideObject guide, GuideSelect select)
            { Guide=guide; Select=select; Material=select ? select.material : null; }
            internal void SetHidden(bool hidden)
            {
                if (!Guide) return;
                if (hidden) Visibility.Apply(Guide.visible, false, v => Guide.visible=v);
                else Visibility.Restore(Guide.visible, v => Guide.visible=v);
            }
            internal void SetColor(bool enabled, int chain)
            {
                if (!Select) return;
                if (!enabled) { RestoreColor(); return; }
                if (ColorApplied) return;
                Color raw=Colors[chain];
                AppliedNormal=new Color(raw.r*.75f,raw.g*.75f,raw.b*.75f,.25f);
                AppliedHighlight=new Color(raw.r,raw.g,raw.b,1f);
                NormalColor.Apply((Color)Normal.GetValue(Select),AppliedNormal,v=>Normal.SetValue(Select,v));
                HighlightColor.Apply((Color)Highlight.GetValue(Select),AppliedHighlight,v=>Highlight.SetValue(Select,v));
                if (Material && Select.material == Material)
                {
                    ShownColor.Apply(Material.color,AppliedNormal,v=>Material.color=v);
                    if (Material.HasProperty("_EmissionColor")) Emission.Apply(Material.GetColor("_EmissionColor"),AppliedNormal,v=>Material.SetColor("_EmissionColor",v));
                }
                ColorApplied=true;
            }
            private void RestoreColor()
            {
                if (!Select) return;
                NormalColor.Restore((Color)Normal.GetValue(Select),v=>Normal.SetValue(Select,v));
                HighlightColor.Restore((Color)Highlight.GetValue(Select),v=>Highlight.SetValue(Select,v));
                if (Material && Select.material == Material)
                {
                    // Hover uses the highlighted color owned by this same guide style.
                    if (ShownColor.Active && Material.color.Equals(AppliedHighlight)) ShownColor.Last=AppliedHighlight;
                    ShownColor.Restore(Material.color,v=>Material.color=v);
                    if (Material.HasProperty("_EmissionColor"))
                    {
                        if (Emission.Active && Material.GetColor("_EmissionColor").Equals(AppliedHighlight)) Emission.Last=AppliedHighlight;
                        Emission.Restore(Material.GetColor("_EmissionColor"),v=>Material.SetColor("_EmissionColor",v));
                    }
                }
                ColorApplied=false;
            }
            internal void Restore() { if (Guide) SetHidden(false); RestoreColor(); }
        }
        private sealed class Node { internal GuideObject Guide; internal int Chain, Index; }
        private static Runtime Get(object plugin) { return States.GetValue(plugin, p=>new Runtime(p)); }
        private static bool Parse(string name, out int chain, out int node)
        {
            chain=node=-1; if (name == null) return false; string[] parts=name.Split('_');
            return parts.Length>=5 && int.TryParse(parts[3],out chain) && int.TryParse(parts[4],out node) && chain>=0 && chain<=7;
        }
        public static void Activate(object plugin, OCIChar character)
        {
            Runtime runtime=Get(plugin); if (runtime.Sessions.ContainsKey(character)) return;
            if (character == null || !character.charInfo || !character.charInfo.animBody) { runtime.Log.LogWarning("SKIRT_STATE_ACTIVATION_SKIPPED missing-character"); return; }
            Transform spine=null;
            foreach (Transform item in character.charInfo.animBody.GetComponentsInChildren<Transform>(true))
                if (item && item.name=="cf_j_spine01") { spine=item; break; }
            if (!spine) { runtime.Log.LogWarning("SKIRT_STATE_ACTIVATION_SKIPPED missing-spine"); return; }
            Session session=new Session(character,spine);
            try { Apply(runtime,session,true); }
            catch { RestoreSession(session); throw; }
            object marker=Activator.CreateInstance(runtime.DataType,true);
            runtime.DataCharacter.SetValue(marker,character); runtime.DataSpine.SetValue(marker,spine);
            runtime.Markers[character]=marker; runtime.Sessions.Add(character,session);
            runtime.Log.LogInfo("SKIRT_STATE_ACTIVE guides="+session.Guides.Count+" nativeListUnchanged=True");
        }
        public static void Restore(object plugin, OCIChar character)
        {
            Runtime runtime=Get(plugin); Session session;
            if (!runtime.Sessions.TryGetValue(character,out session)) return;
            RestoreSession(session); runtime.Sessions.Remove(character); runtime.Markers.Remove(character);
            runtime.Log.LogInfo("SKIRT_STATE_RESTORED nativeListUnchanged=True");
        }
        private static void RestoreSession(Session session)
        {
            var errors=new List<Exception>();
            foreach (GuideObject guide in new List<GuideObject>(session.Guides.Keys))
                try { session.Guides[guide].Restore(); session.Guides.Remove(guide); } catch(Exception error) { errors.Add(error); }
            if(errors.Count!=0) throw new AggregateException("Some owned skirt guide properties could not be restored",errors);
        }
        public static void RestoreAll(object plugin)
        {
            Runtime runtime=Get(plugin); var errors=new List<Exception>();
            foreach(OCIChar character in new List<OCIChar>(runtime.Sessions.Keys))
                try { Restore(plugin,character); } catch(Exception error) { errors.Add(error); }
            States.Remove(plugin);
            if(errors.Count!=0) throw new AggregateException("Skirt state cleanup incomplete",errors);
        }
        public static void Update(object plugin)
        {
            Runtime runtime=Get(plugin);
            if(runtime.Hotkey.Value.IsDown())
                try { runtime.Toggle.Invoke(plugin,null); }
                catch(Exception error) { runtime.Log.LogError("SKIRT_STATE_TOGGLE_FAILED "+(error is TargetInvocationException ? error.InnerException : error)); }
            if(runtime.Sessions.Count==0) return;
            runtime.Elapsed+=Time.deltaTime;
            if(runtime.Elapsed<.15f) return; runtime.Elapsed=0;
            try { Tick(plugin); } catch(Exception error) { runtime.Log.LogError("SKIRT_STATE_UPDATE_FAILED "+error); }
        }
        public static void FromData(object plugin, object data)
        { Runtime runtime=Get(plugin); OCIChar character=(OCIChar)runtime.DataCharacter.GetValue(data); Session session; if(runtime.Sessions.TryGetValue(character,out session)) { if(session.Alive) Apply(runtime,session,true); else Restore(plugin,character); } }
        public static void Tick(object plugin)
        {
            Runtime runtime=Get(plugin); OCIChar selected=(OCIChar)runtime.Selected.Invoke(plugin,null);
            foreach(OCIChar character in new List<OCIChar>(runtime.Sessions.Keys))
            {
                Session session=runtime.Sessions[character];
                if(!session.Alive) { Restore(plugin,character); runtime.Log.LogInfo("SKIRT_STATE_RETIRED replaced-or-deleted-character"); continue; }
                Apply(runtime,session,ReferenceEquals(selected,character) && character.treeNodeObject && character.treeNodeObject.visible);
            }
        }
        private static void Apply(Runtime runtime, Session session, bool updateDepth)
        {
            var nodes=new List<Node>(); var live=new HashSet<GuideObject>();
            foreach(OCIChar.BoneInfo bone in session.Character.listBones.ToArray())
            {
                if(bone==null || bone.boneGroup!=(OIBoneInfo.BoneGroup)1024) continue;
                GuideObject guide=bone.guideObject; int chain,node;
                if(!guide || !guide.transformTarget || !Parse(guide.transformTarget.name,out chain,out node)) continue;
                nodes.Add(new Node{Guide=guide,Chain=chain,Index=node}); live.Add(guide);
            }
            foreach(GuideObject guide in new List<GuideObject>(session.Guides.Keys))
                if(!live.Contains(guide)) { session.Guides[guide].Restore(); session.Guides.Remove(guide); }
            if(!runtime.Depth.Value) session.Hidden=new bool[8];
            else if(updateDepth && Camera.main)
            {
                float[] distances=new float[8]; for(int i=0;i<8;i++) distances[i]=float.MinValue;
                Vector3 forward=Camera.main.transform.forward,origin=session.Spine.position;
                foreach(Node node in nodes) if(node.Index==0) distances[node.Chain]=Vector3.Dot(node.Guide.transformTarget.position-origin,forward);
                var hidden=new bool[8];
                for(int i=0;i<3;i++) { float max=float.MinValue;int index=-1;for(int j=0;j<8;j++)if(!hidden[j]&&distances[j]>max){max=distances[j];index=j;}if(index>=0)hidden[index]=true; }
                session.Hidden=hidden;
            }
            bool tails=!Application.companyName.Contains("KoikatsuSunshine")&&runtime.Tails!=null&&runtime.Tails.Value;
            foreach(Node node in nodes)
            {
                bool hide=session.Hidden[node.Chain] || tails&&node.Index>=4;
                GuideSelect select=node.Guide.gameObject.GetComponentInChildren<GuideSelect>(); GuideState state;
                if(session.Guides.TryGetValue(node.Guide,out state) && state.Select!=select) { state.Restore(); session.Guides.Remove(node.Guide); state=null; }
                if(state==null && !hide && !runtime.Coloring.Value) continue;
                if(state==null) { state=new GuideState(node.Guide,select); session.Guides.Add(node.Guide,state); }
                state.SetHidden(hide); state.SetColor(runtime.Coloring.Value,node.Chain);
                if(!state.HasChanges || !hide && !runtime.Coloring.Value) session.Guides.Remove(node.Guide);
            }
        }
    }
}
