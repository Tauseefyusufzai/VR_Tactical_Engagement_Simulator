#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.UI;

namespace XRMultiplayer.EditorTools
{
    public class VRSceneChecker : EditorWindow
    {
        [MenuItem("Tools/Virtac/Check VR Scene Setup")]
        public static void CheckScene()
        {
            Debug.Log("<b><color=orange>--- Running VR Scene Check ---</color></b>");
            int issuesFound = 0;

            // 1. Check XR Origin
            XROrigin[] xrOrigins = Resources.FindObjectsOfTypeAll<XROrigin>();
            foreach (var origin in xrOrigins)
            {
                if (origin.gameObject.scene.isLoaded) // Ensure it's in the actual scene
                {
                    // Build full hierarchy path
                    Transform current = origin.transform;
                    string path = current.name;
                    while (current.parent != null)
                    {
                        current = current.parent;
                        path = current.name + " -> " + path;
                    }

                    if (!origin.gameObject.activeInHierarchy)
                    {
                        Debug.LogError($"[ACTION REQUIRED] The XR Origin is DISABLED. You must enable the GameObject at path:\n<b>{path}</b>\n(Make sure all its parent GameObjects are also enabled!)", origin.gameObject);
                        issuesFound++;
                    }
                    else
                    {
                        Debug.Log($"[OK] XR Origin is active at: {path}");
                    }
                }
            }

            // 2. Check Cameras
            Camera[] allCameras = Resources.FindObjectsOfTypeAll<Camera>();
            foreach (var cam in allCameras)
            {
                if (cam.gameObject.scene.isLoaded)
                {
                    Transform current = cam.transform;
                    string path = current.name;
                    while (current.parent != null)
                    {
                        current = current.parent;
                        path = current.name + " -> " + path;
                    }

                    if (cam.CompareTag("MainCamera"))
                    {
                        if (!cam.gameObject.activeInHierarchy)
                        {
                            Debug.LogError($"[ACTION REQUIRED] The MainCamera is DISABLED. You must enable it at path:\n<b>{path}</b>", cam.gameObject);
                            issuesFound++;
                        }
                        else
                        {
                            Debug.Log($"[OK] MainCamera is active at: {path}");
                        }
                    }
                    else
                    {
                        if (cam.gameObject.activeInHierarchy)
                        {
                            Debug.LogError($"[ACTION REQUIRED] Found an extra interfering Camera. You must DISABLE or DELETE it at path:\n<b>{path}</b>", cam.gameObject);
                            issuesFound++;
                        }
                    }
                }
            }

            // 3. Check Canvas Raycasters
            Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
            foreach (var canvas in canvases)
            {
                if (canvas.gameObject.scene.isLoaded && canvas.renderMode == RenderMode.WorldSpace)
                {
                    Transform current = canvas.transform;
                    string path = current.name;
                    while (current.parent != null)
                    {
                        current = current.parent;
                        path = current.name + " -> " + path;
                    }

                    GraphicRaycaster oldRaycaster = canvas.GetComponent<GraphicRaycaster>();
                    TrackedDeviceGraphicRaycaster xrRaycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
                    
                    if (xrRaycaster == null)
                    {
                        Debug.LogError($"[ACTION REQUIRED] VR Controllers cannot click this Canvas. You must ADD 'Tracked Device Graphic Raycaster' (and remove the standard 'Graphic Raycaster' if present) on the GameObject at path:\n<b>{path}</b>", canvas.gameObject);
                        issuesFound++;
                    }
                }
            }

            if (issuesFound == 0)
            {
                Debug.Log("<b><color=green>No issues found! Your scene looks ready for VR.</color></b>");
            }
            else
            {
                Debug.Log($"<b><color=red>--- Found {issuesFound} issues that need to be manually fixed! ---</color></b>");
            }
        }
    }
}
#endif
