#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Reflection;
using System.Collections.Generic;

public class FixDuplicateSceneGuids : EditorWindow
{
    [MenuItem("Tools/Fix Duplicate Scene GUIDs")]
    public static void Fix()
    {
        // Use reflection to find all components of type SceneObjectGuid without needing a direct assembly reference
        System.Type guidType = null;
        foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            guidType = assembly.GetType("Unity.Tutorials.Core.SceneObjectGuid");
            if (guidType != null) break;
        }

        if (guidType == null)
        {
            Debug.LogError("[FixDuplicateSceneGuids] Could not find SceneObjectGuid type. Is the Tutorials package installed?");
            return;
        }

        Component[] allGuids = (Component[])Object.FindObjectsByType(guidType, FindObjectsSortMode.None);

        var seen = new HashSet<string>();
        int fixedCount = 0;

        FieldInfo idField = guidType.GetField("m_Id", BindingFlags.NonPublic | BindingFlags.Instance)
                         ?? guidType.GetField("Id",   BindingFlags.Public   | BindingFlags.Instance);

        if (idField == null)
        {
            Debug.LogError("[FixDuplicateSceneGuids] Could not find GUID field via reflection. Field name may differ.");
            return;
        }

        foreach (Component comp in allGuids)
        {
            string id = idField.GetValue(comp)?.ToString();
            if (string.IsNullOrEmpty(id)) continue;

            if (!seen.Add(id))
            {
                // Duplicate found — assign a brand new unique GUID
                idField.SetValue(comp, System.Guid.NewGuid().ToString());
                EditorUtility.SetDirty(comp);
                fixedCount++;
                Debug.Log($"[FixDuplicateSceneGuids] Regenerated GUID on: {comp.gameObject.name}");
            }
        }

        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[FixDuplicateSceneGuids] Done! Fixed {fixedCount} duplicate GUID(s). Try building now.");
    }
}
#endif
