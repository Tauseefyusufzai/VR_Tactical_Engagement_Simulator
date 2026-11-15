using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using System.Collections;

namespace XRMultiplayer
{
    public class WarehouseNetworkSync : MonoBehaviour
    {
        public static WarehouseNetworkSync Instance { get; private set; }

        public static int ActiveMapIndex { get; private set; } = 0; // 0=Room, 1=Warehouse, 2=Terrain Map
        public static bool IsWarehouseActive => ActiveMapIndex == 1;

        private const string k_MessageName = "SyncMapState_VRMP";

        [Header("Map References (Assign in Unity Inspector)")]
        [Tooltip("Inspector reference for Warehouse environment. If assigned, this is used directly.")]
        [SerializeField] private GameObject m_WarehouseObject;

        [Tooltip("Inspector reference for Terrain Map environment. If assigned, this is used directly.")]
        [SerializeField] private GameObject m_TerrainMapObject;

        public GameObject WarehouseObject => m_WarehouseObject;
        public GameObject TerrainMapObject => m_TerrainMapObject;

        Coroutine m_DelayedTeleportCoroutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Instance == null)
            {
                GameObject go = new GameObject("WarehouseNetworkSync");
                go.AddComponent<WarehouseNetworkSync>();
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnServerStarted += OnServerStarted;
                NetworkManager.Singleton.OnClientStarted += OnClientStarted;
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

                if (NetworkManager.Singleton.IsListening)
                {
                    RegisterHandler();
                }
            }
        }

        private bool m_IsHandlerRegistered = false;

        private void Update()
        {
            if (!m_IsHandlerRegistered && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.CustomMessagingManager != null)
            {
                RegisterHandler();
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
                NetworkManager.Singleton.OnClientStarted -= OnClientStarted;
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;

                if (NetworkManager.Singleton.CustomMessagingManager != null)
                {
                    NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(k_MessageName);
                }
            }
        }

        private void OnServerStarted()
        {
            RegisterHandler();
        }

        private void OnClientStarted()
        {
            RegisterHandler();
        }

        private void RegisterHandler()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.CustomMessagingManager != null)
            {
                try
                {
                    NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(k_MessageName, OnWarehouseMessageReceived);
                    m_IsHandlerRegistered = true;
                    Debug.Log("[WarehouseNetworkSync-LOG] CustomMessagingManager registered for " + k_MessageName);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[WarehouseNetworkSync-LOG] Error registering CustomMessagingManager handler: " + ex.Message);
                }
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                Debug.Log($"[WarehouseNetworkSync-LOG] Client connected ({clientId}). Sending current ActiveMapIndex={ActiveMapIndex} to client...");
                SendStateToClient(clientId, ActiveMapIndex);
            }
        }

        private void OnWarehouseMessageReceived(ulong senderClientId, FastBufferReader reader)
        {
            reader.ReadValueSafe(out int mapIndex);
            Debug.Log($"[WarehouseNetworkSync-LOG] OnWarehouseMessageReceived mapIndex={mapIndex} from sender={senderClientId}");
            ActiveMapIndex = mapIndex;
            ApplyStateLocally(mapIndex);
        }

        public void SetWarehouseActive(bool active)
        {
            SetMapActive(active ? 1 : 0);
        }

        public void SetMapActive(int mapIndex)
        {
            ActiveMapIndex = mapIndex;
            Debug.Log($"[WarehouseNetworkSync-LOG] SetMapActive({mapIndex}) called. IsServer={(NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)}");

            ApplyStateLocally(mapIndex);

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                SendStateToAll(mapIndex);
                XRINetworkPlayer.BroadcastMapState(mapIndex);
            }
        }

        private void SendStateToAll(int mapIndex)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.CustomMessagingManager == null) return;

            using (var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<int>(), Allocator.Temp))
            {
                writer.WriteValueSafe(mapIndex);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(k_MessageName, writer);
                Debug.Log($"[WarehouseNetworkSync-LOG] Sent SendNamedMessageToAll({k_MessageName}, mapIndex={mapIndex})");
            }
        }

        private void SendStateToClient(ulong clientId, int mapIndex)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.CustomMessagingManager == null) return;

            using (var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<int>(), Allocator.Temp))
            {
                writer.WriteValueSafe(mapIndex);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(k_MessageName, clientId, writer);
                Debug.Log($"[WarehouseNetworkSync-LOG] Sent SendNamedMessage to client {clientId} ({k_MessageName}, mapIndex={mapIndex})");
            }
        }

        public void ApplyStateLocally(bool active)
        {
            ApplyStateLocally(active ? 1 : 0);
        }

        public void ApplyStateLocally(int mapIndex)
        {
            ActiveMapIndex = mapIndex;

            // 1. Handle Warehouse (Map Index 1)
            if (m_WarehouseObject == null)
            {
                m_WarehouseObject = FindMapGameObject("WareHouse Layout") ?? FindMapGameObject("Warehouse") ?? FindMapGameObject("hangar");
            }
            if (m_WarehouseObject != null)
            {
                m_WarehouseObject.SetActive(mapIndex == 1);
            }

            // 2. Handle Terrain Map (Map Index 2)
            if (m_TerrainMapObject == null)
            {
                m_TerrainMapObject = FindMapGameObject("Terrain Map") ?? FindMapGameObject("TERRAIN MAP") ?? FindMapGameObject("TerrainMap");
            }
            if (m_TerrainMapObject != null)
            {
                m_TerrainMapObject.SetActive(mapIndex == 2);
                if (mapIndex == 2)
                {
                    EnsureTerrainRendering(m_TerrainMapObject);
                }
            }

            Debug.Log($"[WarehouseNetworkSync-LOG] Applied mapIndex={mapIndex} locally. WarehouseObj={(m_WarehouseObject != null ? m_WarehouseObject.name : "NULL")}, TerrainObj={(m_TerrainMapObject != null ? m_TerrainMapObject.name : "NULL")}");

            // Teleport player to tagged 'SpwanArea' / 'SpawnArea' plane if present in active map
            // Wait one frame so that child objects (including the SpawnArea tag) are fully activated
            if (mapIndex > 0)
            {
                if (m_DelayedTeleportCoroutine != null)
                    StopCoroutine(m_DelayedTeleportCoroutine);
                m_DelayedTeleportCoroutine = StartCoroutine(DelayedTeleportToSpawnArea());
            }

            // Hide standard room box if pre-baked map is active (mapIndex > 0)
            if (GridMapManager.Instance != null && GridMapManager.Instance.GetRoomBox() != null)
            {
                GridMapManager.Instance.GetRoomBox().SetActive(mapIndex == 0);
            }
        }

        /// <summary>
        /// Waits one frame for SetActive(true) to propagate to child objects,
        /// then teleports the player to the SpawnArea tag.
        /// </summary>
        IEnumerator DelayedTeleportToSpawnArea()
        {
            yield return null; // Wait one frame for children to activate
            bool success = XRINetworkPlayer.TeleportToSpawnAreaTag();
            Debug.Log($"[WarehouseNetworkSync-LOG] DelayedTeleportToSpawnArea: TeleportToSpawnAreaTag returned {success}");
            m_DelayedTeleportCoroutine = null;
        }

        private static GameObject FindMapGameObject(string targetName)
        {
            if (string.IsNullOrEmpty(targetName)) return null;

            UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            // Pass 1: EXACT name match, strictly excluding any object with "old" in its name
            foreach (GameObject root in activeScene.GetRootGameObjects())
            {
                if (root.name.IndexOf("old", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase))
                    return root;

                Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                foreach (Transform t in transforms)
                {
                    if (t.name.IndexOf("old", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (t.name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase))
                        return t.gameObject;
                }
            }

            // Pass 2: Partial name match, strictly excluding any object containing "old"
            foreach (GameObject root in activeScene.GetRootGameObjects())
            {
                if (root.name.IndexOf("old", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.name.IndexOf(targetName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return root;

                Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                foreach (Transform t in transforms)
                {
                    if (t.name.IndexOf("old", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (t.name.IndexOf(targetName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return t.gameObject;
                }
            }

            return null;
        }

        private void EnsureTerrainRendering(GameObject terrainObj)
        {
            if (terrainObj == null) return;

            Terrain[] terrains = terrainObj.GetComponentsInChildren<Terrain>(true);
            Debug.Log($"[WarehouseNetworkSync-LOG] EnsureTerrainRendering found {terrains.Length} Terrain components.");

            foreach (var terrain in terrains)
            {
                if (terrain == null) continue;

                terrain.enabled = true;
                terrain.drawHeightmap = true;

                // Fix for Android / VR Quest Client transparent ground:
                // 1. If materialTemplate has missing/stripped shader on mobile, assign URP Terrain Lit shader fallback
                Shader urpTerrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit") ?? Shader.Find("Nature/Terrain/Standard");

                if (terrain.materialTemplate == null || terrain.materialTemplate.shader == null || terrain.materialTemplate.shader.name == "Hidden/InternalErrorShader")
                {
                    if (urpTerrainShader != null)
                    {
                        Debug.Log($"[WarehouseNetworkSync-LOG] Fixing Terrain material shader with '{urpTerrainShader.name}'");
                        terrain.materialTemplate = new Material(urpTerrainShader);
                    }
                }
                else if (terrain.materialTemplate.shader != null && !terrain.materialTemplate.shader.isSupported)
                {
                    Debug.LogWarning($"[WarehouseNetworkSync-LOG] Terrain material shader '{terrain.materialTemplate.shader.name}' is NOT supported on this target platform! Applying URP Terrain fallback.");
                    if (urpTerrainShader != null)
                    {
                        terrain.materialTemplate = new Material(urpTerrainShader);
                    }
                }

                // 2. On Android/Quest, GPU instancing without uniform scaling can render terrain heightmap transparent
#if UNITY_ANDROID || UNITY_IOS
                terrain.drawInstanced = false;
#endif

                // 3. Force heightmap mesh refresh
                terrain.Flush();
            }
        }
    }
}
