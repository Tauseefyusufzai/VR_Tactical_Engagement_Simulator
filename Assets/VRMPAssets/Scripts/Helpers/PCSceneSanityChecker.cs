using UnityEngine;
using Unity.Netcode;
using UnityEngine.XR;
using System.Collections.Generic;

namespace XRMultiplayer
{
    /// <summary>
    /// Diagnostic helper script to validate the PC ExCon scene and identify redundant VR components or sandbox objects.
    /// Attach this to an empty GameObject in your PC_ExCon_Scene.
    /// Right-click the component in the Inspector and select "Run PC Scene Validation" to view the report.
    /// </summary>
    public class PCSceneSanityChecker : MonoBehaviour
    {
        [Header("Validation Settings")]
        [Tooltip("If true, validation will automatically run when the scene starts.")]
        [SerializeField] bool m_RunOnStart = true;

        private void Start()
        {
            if (m_RunOnStart)
            {
                RunValidation();
            }
        }

        [ContextMenu("Run PC Scene Validation")]
        public void RunValidation()
        {
            Debug.Log("<color=#00FFCC><b>[PC Scene Validator]</b> Starting validation for PC ExCon Host scene...</color>");

            int issuesCount = 0;
            int passesCount = 0;

            // --- 1. Validate Crucial PC Host Components (Must exist) ---
            Debug.Log("<b>[PC Scene Validator]</b> Checking for crucial Host components...");

            // NetworkManager
            NetworkManager netManager = FindFirstObjectByType<NetworkManager>();
            if (netManager != null)
            {
                Debug.Log("<color=green>✔ [PASSED]</color> <b>NetworkManager</b> is present in the scene.");
                passesCount++;
            }
            else
            {
                Debug.LogError("❌ [FAILED] <b>NetworkManager</b> is missing! PC Host requires NetworkManager to start the server.");
                issuesCount++;
            }

            // XRINetworkGameManager
            XRINetworkGameManager gameManager = FindFirstObjectByType<XRINetworkGameManager>();
            if (gameManager != null)
            {
                Debug.Log("<color=green>✔ [PASSED]</color> <b>XRINetworkGameManager</b> is present.");
                passesCount++;
            }
            else
            {
                Debug.LogError("❌ [FAILED] <b>XRINetworkGameManager</b> is missing! Required for handling session setups.");
                issuesCount++;
            }

            // SessionManager
            SessionManager sessionManager = FindFirstObjectByType<SessionManager>();
            if (sessionManager != null)
            {
                Debug.Log("<color=green>✔ [PASSED]</color> <b>SessionManager</b> is present.");
                passesCount++;
            }
            else
            {
                Debug.LogError("❌ [FAILED] <b>SessionManager</b> is missing! Required for local network connections.");
                issuesCount++;
            }

            // --- 2. Check for Redundant VR Rigs / Components (Must NOT exist) ---
            Debug.Log("<b>[PC Scene Validator]</b> Checking for redundant VR/XR objects in PC scene...");

            // Search for typical XR setup game objects
            GameObject xrSetup = GameObject.Find("XR Interaction Setup (MP Variant)") ?? 
                                 GameObject.Find("XR Interaction Setup") ?? 
                                 GameObject.Find("XR Origin");
            
            if (xrSetup != null)
            {
                Debug.LogWarning($"<color=orange>⚠ [WARNING]</color> <b>{xrSetup.name}</b> is active in the PC scene. " +
                                 "Since the PC build runs on a flat desktop screen, this VR Rig is redundant. " +
                                 "You should disable or delete it and use a standard 2D camera instead.");
                issuesCount++;
            }
            else
            {
                Debug.Log("<color=green>✔ [PASSED]</color> No active VR/XR player rigs found.");
                passesCount++;
            }

            // Check for locomotion providers (Teleport, Continuous Move, etc.)
            var teleportAreas = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            List<string> vrComponentsFound = new List<string>();
            foreach (var comp in teleportAreas)
            {
                string typeName = comp.GetType().Name;
                if (typeName.Contains("Teleportation") || typeName.Contains("Locomotion") || 
                    typeName.Contains("ActionBasedController") || typeName.Contains("SnapTurn") || 
                    typeName.Contains("ContinuousMove"))
                {
                    if (!vrComponentsFound.Contains(typeName))
                        vrComponentsFound.Add(typeName);
                }
            }

            if (vrComponentsFound.Count > 0)
            {
                Debug.LogWarning($"<color=orange>⚠ [WARNING]</color> Found active VR components in the scene: " +
                                 $"<b>{string.Join(", ", vrComponentsFound)}</b>. These are unnecessary on PC and should be removed.");
                issuesCount++;
            }
            else
            {
                Debug.Log("<color=green>✔ [PASSED]</color> No VR movement/locomotion scripts detected.");
                passesCount++;
            }

            // --- 3. Check for Sandbox Toys / Bloat Objects (Must NOT exist) ---
            Debug.Log("<b>[PC Scene Validator]</b> Checking for active sandbox toys...");
            
            GameObject[] allGo = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            List<string> sandboxToysFound = new List<string>();
            foreach (var go in allGo)
            {
                string nameLower = go.name.ToLower();
                if (nameLower.Contains("toy_") || nameLower.Contains("bouncepad") || 
                    nameLower.Contains("domino") || nameLower.Contains("halfpipe") || 
                    nameLower.Contains("pipefan") || nameLower.Contains("music player ui"))
                {
                    sandboxToysFound.Add(go.name);
                }
            }

            if (sandboxToysFound.Count > 0)
            {
                Debug.LogWarning($"<color=orange>⚠ [WARNING]</color> Found active sandbox toys/objects: " +
                                 $"<b>{string.Join(", ", sandboxToysFound)}</b>. These are not needed for a tactical ExCon build and can be deleted.");
                issuesCount++;
            }
            else
            {
                Debug.Log("<color=green>✔ [PASSED]</color> No sandbox sandbox toys/bloat objects found.");
                passesCount++;
            }

            // --- 4. Validate PC Camera Setup ---
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                Debug.LogError("❌ [FAILED] No Main Camera found! Make sure you add a standard desktop camera with a Tag of 'MainCamera'.");
                issuesCount++;
            }
            else
            {
                if (mainCam.transform.parent != null && mainCam.transform.parent.name.Contains("XR"))
                {
                    Debug.LogWarning("<color=orange>⚠ [WARNING]</color> The Main Camera is currently a child of a VR Rig. " +
                                     "For PC Standalone build, unparent the camera or create a standard desktop camera.");
                    issuesCount++;
                }
                else
                {
                    Debug.Log("<color=green>✔ [PASSED]</color> Standard Main Camera is present.");
                    passesCount++;
                }
            }

            // --- 5. Final Report Summary ---
            Debug.Log($"<color=#00FFCC><b>[PC Scene Validator Summary]</b> Checks run complete. " +
                      $"Passed: {passesCount} | Action items: {issuesCount}</color>");
        }
    }
}
