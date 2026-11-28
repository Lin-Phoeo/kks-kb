using System;
using System.Runtime.CompilerServices;
using Timeline;
using UnityEngine;

namespace AutoKeyframeRepair
{
    public sealed class Notice { }
    public static class ValueAvailability
    {
        private static readonly ConditionalWeakTable<Interpolable, Notice> Unavailable = new ConditionalWeakTable<Interpolable, Notice>();
        public static object ReadOrKeep(Interpolable track, Timeline.Keyframe frame)
        {
            try
            {
                object value = track.GetValue();
                if (Unavailable.Remove(track))
                    Debug.Log("[AutoKeyframe] Current-value source recovered; automatic updates resumed. owner=" + track.owner + " id=" + track.id);
                return value;
            }
            catch (NullReferenceException) { return Keep(track, frame, "NullReferenceException"); }
            catch (MissingReferenceException) { return Keep(track, frame, "MissingReferenceException"); }
        }
        private static object Keep(Interpolable track, Timeline.Keyframe frame, string reason)
        {
            Notice ignored;
            if (track != null && !Unavailable.TryGetValue(track, out ignored))
            {
                Unavailable.Add(track, new Notice());
                Debug.LogWarning("[AutoKeyframe] Current-value source unavailable (" + reason + "); existing keyframe kept and other tracks continue. This source will be retried. owner=" + track.owner + " id=" + track.id);
            }
            return frame.value;
        }
    }
}
