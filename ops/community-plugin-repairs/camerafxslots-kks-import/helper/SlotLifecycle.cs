using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CameraFXSlotsRepair
{
    public sealed class FxOwnership : MonoBehaviour
    {
        public Component Owner;
    }
    public sealed class SlotEntry
    {
        public Transform Root, Save;
        public Button Load;
        public UnityEvent Event;
        public UnityAction Callback;
        public GameObject Fx;
        public Image Image;
    }
    public sealed class LayoutLease
    {
        public RectTransform Target;
        public float Before, Last;
        public bool Owned;
        public void Acquire(RectTransform target)
        {
            Target=target;if(!Target)return;Before=Target.sizeDelta.y;Last=64f;
            if(Before==Last)return;
            Vector2 size=Target.sizeDelta;size.y=Last;Target.sizeDelta=size;Owned=true;
        }
        public void Release()
        {
            if(Owned&&Target&&Target.sizeDelta.y==Last){Vector2 size=Target.sizeDelta;size.y=Before;Target.sizeDelta=size;}
            Owned=false;Target=null;
        }
    }
    public sealed class SlotSession
    {
        public Transform Content;
        public float NextPoll;
        public readonly Dictionary<int,SlotEntry> Entries=new Dictionary<int,SlotEntry>();
        public readonly LayoutLease ContentLayout=new LayoutLease(),ScrollLayout=new LayoutLease();
        public SlotMetadata Metadata;
    }
    public sealed class SlotMetadata
    {
        public readonly Dictionary<string,FieldInfo> Fields=new Dictionary<string,FieldInfo>();
        public MethodInfo Add;
        public SlotMetadata(Type type)
        {
            const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
            foreach(string name in new[]{"addedButtons","fxButtonImages","uiInitialized","lastKnownSlotCount"})Fields.Add(name,type.GetField(name,flags));
            Add=type.GetMethod("AddButtonsForNewSlots",flags);
        }
    }
    public static class SlotLifecycle
    {
        private const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        private static readonly Dictionary<object,SlotSession> Sessions=new Dictionary<object,SlotSession>();
        private static FieldInfo Field(object runner,string name){return Session(runner).Metadata.Fields[name];}
        private static IList Buttons(object runner){return (IList)Field(runner,"addedButtons").GetValue(null);}
        private static Image[] Images(object runner){return (Image[])Field(runner,"fxButtonImages").GetValue(null);}
        private static SlotSession Session(object runner){SlotSession s;if(!Sessions.TryGetValue(runner,out s)){s=new SlotSession{Metadata=new SlotMetadata(runner.GetType())};Sessions.Add(runner,s);}return s;}
        public static void Update(object runner)
        {
            var s=Session(runner);if(Time.realtimeSinceStartup<s.NextPoll)return;s.NextPoll=Time.realtimeSinceStartup+.25f;
            var content=GameObject.Find("StudioScene/Canvas System Menu/02_Camera/Scroll/Viewport/Content");
            Reconcile(runner,content?content.transform:null);
        }
        public static void Reconcile(object runner,Transform content)
        {
            var s=Session(runner);
            if(!content)content=null;
            if(!ReferenceEquals(s.Content,content)){ReleaseEntries(runner,s);s.ContentLayout.Release();s.ScrollLayout.Release();s.Content=content;
                if(content){s.ContentLayout.Acquire(content.GetComponent<RectTransform>());if(content.parent&&content.parent.parent)s.ScrollLayout.Acquire(content.parent.parent.GetComponent<RectTransform>());}}
            if(!content){Field(runner,"uiInitialized").SetValue(runner,false);Field(runner,"lastKnownSlotCount").SetValue(runner,0);return;}
            int count=Math.Min(content.childCount,20);var remove=new List<int>();foreach(var pair in s.Entries)if(pair.Key>=count)remove.Add(pair.Key);
            foreach(int i in remove){ReleaseEntry(runner,i,s.Entries[i]);s.Entries.Remove(i);}
            for(int i=0;i<count;i++)
            {
                var root=content.GetChild(i);var save=root?root.Find("Button Save"):null;var loadRoot=root?root.Find("Button Load"):null;var load=loadRoot?loadRoot.GetComponent<Button>():null;
                SlotEntry e;bool exists=s.Entries.TryGetValue(i,out e);
                bool same=exists&&e.Root==root&&e.Save==save&&e.Load==load&&e.Fx&&e.Fx.transform.parent==root&&(!load||ReferenceEquals(e.Event,load.onClick));
                if(same)continue;
                if(exists){ReleaseEntry(runner,i,e);s.Entries.Remove(i);}
                if(!root||!save||!save.GetComponent<Button>()||!save.GetComponent<Image>()||!save.GetComponent<RectTransform>()||loadRoot&&!load)continue;
                for(int child=root.childCount-1;child>=0;child--){var inherited=root.GetChild(child).GetComponent<FxOwnership>();if(inherited&&ReferenceEquals(inherited.Owner,runner)){inherited.gameObject.SetActive(false);UnityEngine.Object.Destroy(inherited.gameObject);}}
                e=new SlotEntry{Root=root,Save=save,Load=load};s.Entries.Add(i,e);
                try{s.Metadata.Add.Invoke(runner,new object[]{content,i,i+1});var image=Images(runner)[i];e.Image=image;e.Fx=image?image.gameObject:null;if(e.Fx)e.Fx.AddComponent<FxOwnership>().Owner=runner as Component;}
                catch(TargetInvocationException ex){ReleaseEntry(runner,i,e);s.Entries.Remove(i);throw ex.InnerException;}
            }
            Field(runner,"uiInitialized").SetValue(runner,true);Field(runner,"lastKnownSlotCount").SetValue(runner,count);
        }
        public static void AttachLoad(UnityEvent target,UnityAction callback,object runner,int index)
        {
            var s=Session(runner);SlotEntry e;if(!s.Entries.TryGetValue(index,out e))throw new InvalidOperationException("CameraFX slot ownership missing");
            target.AddListener(callback);e.Event=target;e.Callback=callback;
        }
        private static void ReleaseEntry(object runner,int index,SlotEntry e)
        {
            if(e.Event!=null&&e.Callback!=null)e.Event.RemoveListener(e.Callback);
            var images=Images(runner);if(ReferenceEquals(images[index],e.Image))images[index]=null;
            if(!ReferenceEquals(e.Fx,null)){Buttons(runner).Remove(e.Fx);if(e.Fx)UnityEngine.Object.Destroy(e.Fx);}
            e.Callback=null;e.Event=null;e.Fx=null;e.Image=null;
        }
        private static void ReleaseEntries(object runner,SlotSession s){foreach(var pair in s.Entries)ReleaseEntry(runner,pair.Key,pair.Value);s.Entries.Clear();}
        public static void Release(object runner)
        {
            SlotSession s;if(!Sessions.TryGetValue(runner,out s))return;
            ReleaseEntries(runner,s);s.ContentLayout.Release();s.ScrollLayout.Release();
            Field(runner,"uiInitialized").SetValue(runner,false);Field(runner,"lastKnownSlotCount").SetValue(runner,0);
            Sessions.Remove(runner);
        }
        public static void ReleaseAll(){var keys=new List<object>(Sessions.Keys);foreach(var runner in keys)Release(runner);}
        public static void IgnoreLegacyLayoutWrite(RectTransform target,Vector2 ignored){}
    }
}
