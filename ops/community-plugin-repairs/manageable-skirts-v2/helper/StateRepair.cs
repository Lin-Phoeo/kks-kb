using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using Studio;
using UnityEngine;

namespace Codex.ManageableSkirtsV2State
{
    public static class StateRepair
    {
        private const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly FieldInfo Normal=typeof(GuideSelect).GetField("colorNormal",All), Highlight=typeof(GuideSelect).GetField("colorHighlighted",All);
        private static readonly ConditionalWeakTable<GuideObject,Owned<bool>> Visibility=new ConditionalWeakTable<GuideObject,Owned<bool>>();
        private static readonly ConditionalWeakTable<Renderer,Owned<bool>> Renderers=new ConditionalWeakTable<Renderer,Owned<bool>>();
        private static readonly ConditionalWeakTable<OCIChar.BoneInfo,Owned<bool>> Active=new ConditionalWeakTable<OCIChar.BoneInfo,Owned<bool>>();
        private static readonly ConditionalWeakTable<GuideSelect,Colors> Styles=new ConditionalWeakTable<GuideSelect,Colors>();
        private static readonly ConditionalWeakTable<object,Stamp> Stamps=new ConditionalWeakTable<object,Stamp>();
        private sealed class Stamp { internal ChaControl Control; internal Animator Body; }
        private sealed class Owned<T>
        {
            internal T Original,Last;internal bool Has,Yielded;
            internal void Apply(T current,T wanted,Action<T> write)
            {
                if(Has&&!EqualityComparer<T>.Default.Equals(current,Last)){Has=false;Yielded=true;}
                if(Yielded||EqualityComparer<T>.Default.Equals(current,wanted))return;
                if(!Has)Original=current;
                write(wanted);Last=wanted;Has=true;
            }
            internal void Restore(T current,Action<T> write){if(Has&&EqualityComparer<T>.Default.Equals(current,Last))write(Original);Has=false;}
        }
        private sealed class Colors
        {
            internal Material Material;
            internal readonly Owned<Color> Normal=new Owned<Color>(),Highlight=new Owned<Color>(),Shown=new Owned<Color>(),Emission=new Owned<Color>();
        }
        private static object Field(object o,string name){return o.GetType().GetField(name,All).GetValue(o);}
        private static void Put(object o,string name,object value){o.GetType().GetField(name,All).SetValue(o,value);}
        private static object Call(object o,string name,params object[] args)
        {try{return o.GetType().GetMethod(name,All).Invoke(o,args);}catch(TargetInvocationException e){throw e.InnerException;}}
        private static bool Config(object p,string name){var c=Field(p,name) as ConfigEntry<bool>;return c!=null&&c.Value;}
        private static BepInEx.Logging.ManualLogSource Log(object plugin){return (BepInEx.Logging.ManualLogSource)typeof(BaseUnityPlugin).GetProperty("Logger",All).GetValue(plugin,null);}
        private static IList Entries(object data){return (IList)Field(data,"allEntries");}
        private static OCIChar Character(object data){return (OCIChar)Field(data,"ociChar");}
        private static bool Valid(OCIChar.BoneInfo bone){return bone!=null&&bone.guideObject&&bone.guideObject.transformTarget;}
        public static bool CanActivate(OCIChar c){return c!=null&&c.charInfo&&c.charInfo.animBody;}
        private static Stamp Mark(object data)
        {return Stamps.GetValue(data,d=>{var c=Character(d).charInfo;return new Stamp{Control=c,Body=c?c.animBody:null};});}
        public static bool IsLive(object data)
        {
            if(data==null)return false;var c=Character(data);if(c==null||!c.charInfo||!c.charInfo.animBody||!(Transform)Field(data,"spine"))return false;
            var mark=Mark(data);return c.charInfo==mark.Control&&c.charInfo.animBody==mark.Body;
        }
        public static void SetVisible(GuideObject guide,bool value)
        {
            if(!guide)return;Owned<bool> state;
            if(value){if(Visibility.TryGetValue(guide,out state))state.Restore(guide.visible,v=>guide.visible=v);}
            else Visibility.GetOrCreateValue(guide).Apply(guide.visible,false,v=>guide.visible=v);
        }
        public static void SuppressRenderer(Renderer renderer,bool value)
        {if(renderer)Renderers.GetOrCreateValue(renderer).Apply(renderer.enabled,value,v=>renderer.enabled=v);}
        public static void RestoreRenderer(Renderer renderer,bool ignored)
        {Owned<bool> state;if(renderer&&Renderers.TryGetValue(renderer,out state)){state.Restore(renderer.enabled,v=>renderer.enabled=v);Renderers.Remove(renderer);}}
        public static void Color(object plugin,GuideSelect select,int chain)
        {
            if(!select)return;
            var state=Styles.GetValue(select,s=>new Colors{Material=s.material});
            Color normal=(Color)Call(plugin,"GetChainColor",chain,((ConfigEntry<float>)Field(plugin,"nodeSaturation")).Value,((ConfigEntry<float>)Field(plugin,"nodeBrightness")).Value,((ConfigEntry<float>)Field(plugin,"nodeOpacity")).Value);
            Color high=(Color)Call(plugin,"GetHighlightColor",normal);
            state.Normal.Apply((Color)Normal.GetValue(select),normal,v=>Normal.SetValue(select,v));
            state.Highlight.Apply((Color)Highlight.GetValue(select),high,v=>Highlight.SetValue(select,v));
            if(state.Material&&select.material==state.Material)
            {
                if(state.Shown.Has&&state.Material.color.Equals(state.Highlight.Last))state.Shown.Last=state.Highlight.Last;
                state.Shown.Apply(state.Material.color,normal,v=>state.Material.color=v);
                if(state.Material.HasProperty("_EmissionColor")){if(state.Emission.Has&&state.Material.GetColor("_EmissionColor").Equals(state.Highlight.Last))state.Emission.Last=state.Highlight.Last;state.Emission.Apply(state.Material.GetColor("_EmissionColor"),normal,v=>state.Material.SetColor("_EmissionColor",v));}
            }
        }
        private static void RestoreStyle(GuideSelect select)
        {
            Colors state;if(!select||!Styles.TryGetValue(select,out state))return;
            if(state.Material&&select.material==state.Material)
            {
                if(state.Shown.Has&&state.Material.color.Equals(state.Highlight.Last))state.Shown.Last=state.Highlight.Last;
                state.Shown.Restore(state.Material.color,v=>state.Material.color=v);
                if(state.Material.HasProperty("_EmissionColor")){if(state.Emission.Has&&state.Material.GetColor("_EmissionColor").Equals(state.Highlight.Last))state.Emission.Last=state.Highlight.Last;state.Emission.Restore(state.Material.GetColor("_EmissionColor"),v=>state.Material.SetColor("_EmissionColor",v));}
            }
            state.Normal.Restore((Color)Normal.GetValue(select),v=>Normal.SetValue(select,v));state.Highlight.Restore((Color)Highlight.GetValue(select),v=>Highlight.SetValue(select,v));Styles.Remove(select);
        }
        public static void SyncActive(object data)
        {
            if(!IsLive(data))return;var c=Character(data);bool wanted=c.oiCharInfo.enableFK&&c.oiCharInfo.activeFK[6];
            foreach(object entry in Entries(data)){var bone=(OCIChar.BoneInfo)Field(entry,"boneInfo");if((bool)Field(entry,"isSkirt")&&Valid(bone)&&c.listBones.Contains(bone)&&bone.boneWeight)Active.GetOrCreateValue(bone).Apply(bone.active,wanted,v=>bone.active=v);}
        }
        public static void VisibilityForData(object plugin,object data)
        {
            if(!IsLive(data))return;SyncActive(data);var c=Character(data);var hidden=(bool[])Field(data,"hiddenChains");var ends=(int[])Field(data,"hiddenLastNodeIndices");bool last=(bool)Field(data,"lastVisibleNodesHidden"),tails=(bool)plugin.GetType().GetProperty("ShouldHideTails",All).GetValue(plugin,null);
            foreach(object entry in Entries(data))
            {
                if(!(bool)Field(entry,"isSkirt"))continue;var bone=(OCIChar.BoneInfo)Field(entry,"boneInfo");if(!Valid(bone)||!c.listBones.Contains(bone))continue;
                int chain=(int)Field(entry,"chainIdx"),node=(int)Field(entry,"nodeIdx");bool hide=tails&&node>=4||last&&node==ends[chain]||hidden[chain];SetVisible(bone.guideObject,!hide);
            }
        }
        private static void RestoreEntry(object plugin,object entry)
        {
            if(!(bool)Field(entry,"isSkirt"))return;
            Call(plugin,"DestroyDirectionalGuide",entry);
            var bone=(OCIChar.BoneInfo)Field(entry,"boneInfo");if(bone!=null){Owned<bool> state;if(Valid(bone)){if(Visibility.TryGetValue(bone.guideObject,out state)){state.Restore(bone.guideObject.visible,v=>bone.guideObject.visible=v);Visibility.Remove(bone.guideObject);}if(Active.TryGetValue(bone,out state)){state.Restore(bone.active,v=>bone.active=v);Active.Remove(bone);}}}
            RestoreStyle(Field(entry,"guideSelect") as GuideSelect);
        }
        public static void Restore(object plugin,OCIChar c)
        {
            var tracked=(IDictionary)Field(plugin,"tracked");if(c==null||!tracked.Contains(c))return;object data=tracked[c];var errors=new List<Exception>();
            foreach(object entry in Entries(data))try{RestoreEntry(plugin,entry);}catch(Exception e){errors.Add(e);}
            if(errors.Count>0)throw new AggregateException("Some V2 skirt state could not be restored",errors);
            Stamps.Remove(data);
        }
        private static bool Reconcile(object plugin,object data)
        {
            var c=Character(data);IList entries=Entries(data);bool changed=false;var current=new HashSet<OCIChar.BoneInfo>(c.listBones);var known=new HashSet<OCIChar.BoneInfo>();
            for(int i=entries.Count-1;i>=0;i--){object e=entries[i];var b=(OCIChar.BoneInfo)Field(e,"boneInfo");if(!current.Contains(b)||!Valid(b)){RestoreEntry(plugin,e);entries.RemoveAt(i);changed=true;}else known.Add(b);}
            Type entryType=plugin.GetType().GetNestedType("BoneEntry",BindingFlags.NonPublic);
            foreach(var bone in c.listBones)
            {
                if(!Valid(bone)||known.Contains(bone))continue;object e=Activator.CreateInstance(entryType,true);Put(e,"boneInfo",bone);Put(e,"chainIdx",-1);Put(e,"nodeIdx",-1);
                object[] args={bone.guideObject.transformTarget.name,-1,-1};bool skirt=bone.boneGroup==(OIBoneInfo.BoneGroup)1024&&(bool)plugin.GetType().GetMethod("TryParseSkirtBone",All).Invoke(null,args);
                Put(e,"isSkirt",skirt);if(skirt){Put(e,"chainIdx",args[1]);Put(e,"nodeIdx",args[2]);Put(e,"guideSelect",bone.guideObject.gameObject.GetComponentInChildren<GuideSelect>());}entries.Add(e);changed=true;
            }
            return changed;
        }
        public static void Tick(object plugin)
        {
            var tracked=(IDictionary)Field(plugin,"tracked");var keys=new List<OCIChar>();foreach(OCIChar key in tracked.Keys)keys.Add(key);
            var selected=Call(plugin,"FindSelectedCharacter") as OCIChar;
            foreach(var c in keys)
            {
                object data=tracked[c];if(!IsLive(data)){Restore(plugin,c);tracked.Remove(c);continue;}
                bool changed=Reconcile(plugin,data);bool color=Config(plugin,"enableColoring");Put(data,"chainColored",color);
                foreach(object e in Entries(data)){if(!(bool)Field(e,"isSkirt"))continue;var select=Field(e,"guideSelect") as GuideSelect;if(color)Color(plugin,select,(int)Field(e,"chainIdx"));else RestoreStyle(select);}
                if(changed){foreach(object e in Entries(data))Call(plugin,"DestroyDirectionalGuide",e);Call(plugin,"SetDirectionalGuideMode",data,Config(plugin,"enableDirectionalCones"));Call(plugin,"SetLastVisibleNodesHidden",data,Config(plugin,"enableHideLastVisibleNode"));}
                if(!Config(plugin,"enableDepthHiding"))Put(data,"hiddenChains",new bool[8]);else if(ReferenceEquals(c,selected)&&c.treeNodeObject&&c.treeNodeObject.visible)Call(plugin,"UpdateDepthForChar",data);
                VisibilityForData(plugin,data);
            }
        }
        public static void Update(object plugin)
        {
            var log=Log(plugin);
            try{if(((ConfigEntry<KeyboardShortcut>)Field(plugin,"toggleHotkey")).Value.IsDown())Call(plugin,"ToggleSelectedCharacter");}catch(Exception e){log.LogError("SKIRT_V2_TOGGLE_FAILED "+e);}
            if(((IDictionary)Field(plugin,"tracked")).Count==0)return;
            float timer=(float)Field(plugin,"depthTimer")+Time.deltaTime;Put(plugin,"depthTimer",timer);if(timer<.15f)return;Put(plugin,"depthTimer",0f);
            try{Tick(plugin);}catch(Exception e){log.LogError("SKIRT_V2_UPDATE_FAILED "+e);}
        }
        public static void RestoreAll(object plugin)
        {
            var tracked=(IDictionary)Field(plugin,"tracked");var keys=new List<OCIChar>();foreach(OCIChar key in tracked.Keys)keys.Add(key);
            foreach(var c in keys)try{Restore(plugin,c);}catch(Exception e){Log(plugin).LogError("SKIRT_V2_CLEANUP_FAILED "+e);}
            tracked.Clear();
        }
    }
}
