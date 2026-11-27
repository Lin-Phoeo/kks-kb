using System;
using System.Collections.Generic;
using System.Reflection;

namespace KKS_ExpressionStudio
{
    internal static class PoseEditorLookupRepair
    {
        internal static void Harvest(object oci, List<object> found, Type editorType)
        {
            if (oci == null || editorType == null || found.Count != 0) return;
            UnityEngine.Object character = ReadMember(oci, "charInfo") as UnityEngine.Object;
            if (character == null) return;
            UnityEngine.Object guide = ReadMember(oci, "guideObject") as UnityEngine.Object;
            if (guide == null) return;
            UnityEngine.Component transform = ReadMember(guide, "transformTarget") as UnityEngine.Component;
            if (transform == null) return;
            Type poseType = editorType.Assembly.GetType("HSPE.PoseController", false);
            if (poseType == null) return;
            UnityEngine.Component controller = transform.GetComponent(poseType);
            if (controller == null) return;
            object editor = ReadMember(controller, "_blendShapesEditor");
            if (editor == null || !editorType.IsInstanceOfType(editor)) return;
            for (int i = 0; i < found.Count; i++) if (Object.ReferenceEquals(found[i], editor)) return;
            found.Add(editor);
        }

        private static object ReadMember(object host, string name)
        {
            if (host == null) return null;
            for (Type type = host.GetType(); type != null; type = type.BaseType)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                FieldInfo field = type.GetField(name, flags);
                if (field != null) return field.GetValue(host);
                PropertyInfo property = type.GetProperty(name, flags);
                if (property != null) return property.GetValue(host, null);
            }
            return null;
        }
    }
}
