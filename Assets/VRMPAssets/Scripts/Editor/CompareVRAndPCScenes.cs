#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

public class CompareVRAndPCScenes : EditorWindow
{
    private SceneAsset vrScene;
    private SceneAsset pcScene;

    [MenuItem("Tools/Compare VR and PC Scenes")]
    public static void ShowWindow()
    {
        GetWindow<CompareVRAndPCScenes>("Scene Compare");
    }

    private void OnGUI()
    {
        GUILayout.Label("Select your two scenes to find what is missing:", EditorStyles.boldLabel);
        
        vrScene = (SceneAsset)EditorGUILayout.ObjectField("VR Client Scene", vrScene, typeof(SceneAsset), false);
        pcScene = (SceneAsset)EditorGUILayout.ObjectField("PC ExCon Scene", pcScene, typeof(SceneAsset), false);

        if (GUILayout.Button("Find Missing Objects"))
        {
            if (vrScene == null || pcScene == null)
            {
                Debug.LogError("Please assign both scenes!");
                return;
            }

            CompareScenes();
        }
    }

    private void CompareScenes()
    {
        string vrPath = AssetDatabase.GetAssetPath(vrScene);
        string pcPath = AssetDatabase.GetAssetPath(pcScene);

        // Load both scenes into memory additively so we can read them
        Scene vrLoaded = EditorSceneManager.OpenScene(vrPath, OpenSceneMode.Additive);
        Scene pcLoaded = EditorSceneManager.OpenScene(pcPath, OpenSceneMode.Additive);

        List<string> pcObjectNames = new List<string>();
        foreach (GameObject obj in pcLoaded.GetRootGameObjects())
        {
            pcObjectNames.Add(obj.name);
        }

        Debug.Log($"<b>=== OBJECTS TO COPY FROM {vrScene.name} TO {pcScene.name} ===</b>");
        
        int missingCount = 0;
        foreach (GameObject vrObj in vrLoaded.GetRootGameObjects())
        {
            // Ignore common Unity objects that exist in every scene implicitly
            if (vrObj.name == "Main Camera" || vrObj.name == "Directional Light") continue;

            if (!pcObjectNames.Contains(vrObj.name))
            {
                Debug.Log($"<color=cyan>You should copy:</color> <b>{vrObj.name}</b>");
                missingCount++;
            }
        }

        if (missingCount == 0)
        {
            Debug.Log("<color=green>Both scenes have the exact same root GameObjects!</color>");
        }

        // Clean up by reopening just the PC scene
        EditorSceneManager.OpenScene(pcPath, OpenSceneMode.Single);
    }
}
#endif
