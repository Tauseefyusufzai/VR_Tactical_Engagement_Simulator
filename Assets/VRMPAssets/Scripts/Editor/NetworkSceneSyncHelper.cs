#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Netcode;
using System.IO;

namespace XRMultiplayer
{
    public class NetworkSceneSyncHelper
    {
        [MenuItem("Tools/Fix Netcode Scene Objects & Warehouse Sync")]
        public static void FixNetworkObjectsAndSyncWarehouse()
        {
            Debug.Log("[NetworkSceneSyncHelper] Starting automatic Netcode sync fix...");

            // 1. Check NetworkManager in active scene
            NetworkManager netManager = Object.FindFirstObjectByType<NetworkManager>();
            if (netManager != null)
            {
                string prefabFolder = "Assets/VRMPAssets/Prefabs/GridObjects";
                if (!Directory.Exists(prefabFolder))
                {
                    Directory.CreateDirectory(prefabFolder);
                    AssetDatabase.Refresh();
                }

                string gridMapPrefabPath = $"{prefabFolder}/GridMapManagerPrefab.prefab";
                GameObject gridMapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(gridMapPrefabPath);

                if (gridMapPrefab == null)
                {
                    GameObject tempObj = new GameObject("GridMapManager");
                    tempObj.AddComponent<GridMapManager>();
                    tempObj.AddComponent<NetworkObject>();
                    gridMapPrefab = PrefabUtility.SaveAsPrefabAsset(tempObj, gridMapPrefabPath);
                    Object.DestroyImmediate(tempObj);
                    AssetDatabase.Refresh();
                    Debug.Log("[NetworkSceneSyncHelper] Created GridMapManagerPrefab.prefab");
                }

                if (gridMapPrefab != null)
                {
                    var prefabs = netManager.NetworkConfig.Prefabs;
                    if (!prefabs.Contains(gridMapPrefab))
                    {
                        prefabs.Add(new NetworkPrefab { Prefab = gridMapPrefab });
                        EditorUtility.SetDirty(netManager);
                        Debug.Log("[NetworkSceneSyncHelper] Registered GridMapManagerPrefab into NetworkManager NetworkPrefabs list!");
                    }
                }
            }

            // 2. Open VRtac Clent.unity and ensure GridMapManager and WareHouse Layout exist
            string clientScenePath = "Assets/Scenes/VRtac Clent.unity";

            if (File.Exists(clientScenePath))
            {
                string originalScenePath = EditorSceneManager.GetActiveScene().path;
                var clientScene = EditorSceneManager.OpenScene(clientScenePath, OpenSceneMode.Single);

                GridMapManager clientGridMgr = Object.FindFirstObjectByType<GridMapManager>();
                if (clientGridMgr == null)
                {
                    GameObject mgrObj = new GameObject("GridMapManager");
                    mgrObj.AddComponent<GridMapManager>();
                    mgrObj.AddComponent<NetworkObject>();
                    EditorSceneManager.MarkSceneDirty(clientScene);
                    Debug.Log("[NetworkSceneSyncHelper] Added GridMapManager to VRtac Clent.unity scene!");
                }

                // Check WareHouse object in VR Client scene
                GameObject warehouseInClient = GameObject.Find("WareHouse Layout ");
                if (warehouseInClient == null) warehouseInClient = GameObject.Find("Warehouse");

                if (warehouseInClient == null)
                {
                    GameObject warehousePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VRMPAssets/Prefabs/GridObjects/room/WareHouse Layout .prefab");
                    if (warehousePrefab != null)
                    {
                        GameObject spawnedWarehouse = (GameObject)PrefabUtility.InstantiatePrefab(warehousePrefab, clientScene);
                        spawnedWarehouse.name = "WareHouse Layout ";
                        spawnedWarehouse.SetActive(false); // Disabled by default
                        EditorSceneManager.MarkSceneDirty(clientScene);
                        Debug.Log("[NetworkSceneSyncHelper] Instantiated 'WareHouse Layout ' into VRtac Clent.unity (disabled by default)!");
                    }
                }

                // Check TERRAIN MAP object in VR Client scene
                GameObject terrainInClient = GameObject.Find("TERRAIN MAP");
                if (terrainInClient == null) terrainInClient = GameObject.Find("Terrain Map");

                if (terrainInClient == null)
                {
                    GameObject terrainPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VRMPAssets/Prefabs/GridObjects/room/TERRAIN MAP.prefab");
                    if (terrainPrefab != null)
                    {
                        GameObject spawnedTerrain = (GameObject)PrefabUtility.InstantiatePrefab(terrainPrefab, clientScene);
                        spawnedTerrain.name = "TERRAIN MAP";
                        spawnedTerrain.SetActive(false); // Disabled by default
                        EditorSceneManager.MarkSceneDirty(clientScene);
                        Debug.Log("[NetworkSceneSyncHelper] Instantiated 'TERRAIN MAP' into VRtac Clent.unity (disabled by default)!");
                    }
                }

                EditorSceneManager.SaveScene(clientScene);

                if (!string.IsNullOrEmpty(originalScenePath) && originalScenePath != clientScenePath)
                {
                    EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[NetworkSceneSyncHelper] Fix Complete! Rebuild your Android APK and test.");
        }
    }
}
#endif
