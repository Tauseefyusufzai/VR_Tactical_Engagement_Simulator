#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace XRMultiplayer
{
    [CustomEditor(typeof(ExConLayoutTemplate))]
    public class ExConLayoutTemplateEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            ExConLayoutTemplate template = (ExConLayoutTemplate)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("Use this button during PLAY MODE to capture all currently placed objects into this layout.", MessageType.Info);

            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button("Capture Current Scene Layout", GUILayout.Height(40)))
            {
                CaptureLayout(template);
            }
            GUI.enabled = true;
        }

        private void CaptureLayout(ExConLayoutTemplate template)
        {
            if (GridMapManager.Instance == null)
            {
                Debug.LogError("[LayoutCapture] GridMapManager not found in scene!");
                return;
            }

            Undo.RecordObject(template, "Capture Layout");

            template.PlacedObjects.Clear();

            foreach (var kvp in GridMapManager.Instance.PlacedObjects)
            {
                Vector2Int gridPos = kvp.Key;
                Unity.Netcode.NetworkObject netObj = kvp.Value;

                if (netObj == null) continue;

                GridObjectState state = netObj.GetComponent<GridObjectState>();
                if (state == null) continue;

                PlacedObjectRecord record = new PlacedObjectRecord
                {
                    GridPosition = gridPos,
                    PrefabIndex = state.PrefabIndex.Value,
                    YRotation = netObj.transform.rotation.eulerAngles.y,
                    Scale = state.Scale.Value
                };

                template.PlacedObjects.Add(record);
            }

            EditorUtility.SetDirty(template);
            AssetDatabase.SaveAssets();

            Debug.Log($"[LayoutCapture] Successfully captured {template.PlacedObjects.Count} objects into layout '{template.LayoutName}'.");
        }
    }
}
#endif
