using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;
namespace XRMultiplayer
{
    public class GridMapManager : NetworkBehaviour
    {
        public static GridMapManager Instance { get; private set; }

        [Header("Grid Settings")]
        [SerializeField] private float m_CellSize = 3.0f;
        public float CellSize => m_CellSize;

        [Header("Room Box Reference")]
        [SerializeField] private GameObject m_HangarBox;
        [SerializeField] private float m_BoxHeight = 10.0f;

        [Header("Pre-Baked Environment Map References")]
        [Tooltip("Pre-baked Warehouse GameObject in the scene. Synced across all networked clients.")]
        [SerializeField] private GameObject m_WarehouseObject;
        [Tooltip("Fallback Warehouse Prefab if missing in scene hierarchy.")]
        [SerializeField] private GameObject m_WarehousePrefab;

        [Tooltip("Pre-baked Terrain Map GameObject in the scene. Synced across all networked clients.")]
        [SerializeField] private GameObject m_TerrainMapObject;
        [Tooltip("Fallback Terrain Map Prefab if missing in scene hierarchy.")]
        [SerializeField] private GameObject m_TerrainMapPrefab;

        [Header("Spawnable Prefabs")]
        [Tooltip("0=Wall, 1=Window, 2=Door, 3=Corner, 4=BreachHouse, 5=Watchtower, 6=Container, 7=Concrete, 8=Sandbags")]
        [SerializeField] private List<GameObject> m_PlaceablePrefabs = new List<GameObject>();

        [Header("Layout Design Config Templates")]
        [SerializeField] private List<ExConLayoutTemplate> m_LayoutTemplates = new List<ExConLayoutTemplate>();

        // Syncs hangar scale: X = Width, Y = Height, Z = Length
        public readonly NetworkVariable<Vector3> HangarScale = new NetworkVariable<Vector3>(
            new Vector3(30f, 10f, 30f),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // Syncs active status of pre-baked Warehouse environment across all clients
        public readonly NetworkVariable<bool> IsWarehouseActive = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // Syncs active map index (0=Room, 1=Warehouse, 2=Terrain Map) across all clients
        public readonly NetworkVariable<int> ActiveMapIndex = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // Syncs spawned hangar network object reference across clients
        private readonly NetworkVariable<NetworkObjectReference> m_HangarNetObjectRef = new NetworkVariable<NetworkObjectReference>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // Tracks placed elements by grid coordinate
        private readonly Dictionary<Vector2Int, NetworkObject> m_PlacedObjects = new Dictionary<Vector2Int, NetworkObject>();
        public IReadOnlyDictionary<Vector2Int, NetworkObject> PlacedObjects => m_PlacedObjects;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public GameObject GetRoomBox()
        {
            return m_HangarBoxInstance != null ? m_HangarBoxInstance : m_HangarBox;
        }

        private GameObject m_HangarBoxInstance;

        public override void OnNetworkSpawn()
        {
            Debug.Log($"[GridMapManager-LOG] OnNetworkSpawn called on {(IsServer ? "Server/Host" : "Client")}. ActiveMapIndex={ActiveMapIndex.Value}, IsWarehouseActive={IsWarehouseActive.Value}");
            HangarScale.OnValueChanged += OnHangarScaleChanged;
            IsWarehouseActive.OnValueChanged += OnWarehouseActiveChanged;
            ActiveMapIndex.OnValueChanged += OnActiveMapIndexChanged;
            m_HangarNetObjectRef.OnValueChanged += OnHangarNetObjectRefChanged;

            EnsureHangarInstance();
            ApplyHangarScale(HangarScale.Value);
            ApplyMapState(ActiveMapIndex.Value);
        }

        public override void OnNetworkDespawn()
        {
            Debug.Log($"[GridMapManager-LOG] OnNetworkDespawn called on {(IsServer ? "Server/Host" : "Client")}");
            HangarScale.OnValueChanged -= OnHangarScaleChanged;
            IsWarehouseActive.OnValueChanged -= OnWarehouseActiveChanged;
            ActiveMapIndex.OnValueChanged -= OnActiveMapIndexChanged;
            m_HangarNetObjectRef.OnValueChanged -= OnHangarNetObjectRefChanged;
            if (m_HangarBoxInstance != null && IsServer)
            {
                Destroy(m_HangarBoxInstance);
            }
        }

        private void OnHangarScaleChanged(Vector3 previousValue, Vector3 newValue)
        {
            ApplyHangarScale(newValue);
        }

        private void OnHangarNetObjectRefChanged(NetworkObjectReference previousValue, NetworkObjectReference newValue)
        {
            EnsureHangarInstance();
            ApplyHangarScale(HangarScale.Value);
        }

        private void OnWarehouseActiveChanged(bool previousValue, bool newValue)
        {
            Debug.Log($"[GridMapManager-LOG] OnWarehouseActiveChanged fired! Prev={previousValue} -> New={newValue} on {(IsServer ? "Server/Host" : "Client")}");
            ApplyWarehouseState(newValue);
        }

        private void OnActiveMapIndexChanged(int previousValue, int newValue)
        {
            Debug.Log($"[GridMapManager-LOG] OnActiveMapIndexChanged fired! Prev={previousValue} -> New={newValue} on {(IsServer ? "Server/Host" : "Client")}");
            ApplyMapState(newValue);
        }

        private void ApplyWarehouseState(bool active)
        {
            ApplyMapState(active ? 1 : 0);
        }

        private void ApplyMapState(int mapIndex)
        {
            Debug.Log($"[GridMapManager-LOG] ApplyMapState(mapIndex={mapIndex}) called on {(IsServer ? "Server/Host" : "Client")}. Scene='{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}'");

            if (m_WarehouseObject == null)
            {
                m_WarehouseObject = FindMapGameObject("WareHouse Layout") ?? FindMapGameObject("Warehouse") ?? FindMapGameObject("hangar");
            }
            if (m_WarehouseObject != null)
            {
                m_WarehouseObject.SetActive(mapIndex == 1);
            }

            if (m_TerrainMapObject == null)
            {
                m_TerrainMapObject = FindMapGameObject("Terrain Map") ?? FindMapGameObject("TERRAIN MAP") ?? FindMapGameObject("TerrainMap");
            }
            if (m_TerrainMapObject != null)
            {
                m_TerrainMapObject.SetActive(mapIndex == 2);
            }

            GameObject roomBox = GetRoomBox();
            if (roomBox != null)
            {
                roomBox.SetActive(mapIndex == 0);
            }
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

        public void SetWarehouseActive(bool active)
        {
            SetMapActive(active ? 1 : 0);
        }

        public void SetMapActive(int mapIndex)
        {
            Debug.Log($"[GridMapManager-LOG] SetMapActive({mapIndex}) called on {(IsServer ? "Server/Host" : "Client")}");
            if (WarehouseNetworkSync.Instance != null)
            {
                WarehouseNetworkSync.Instance.SetMapActive(mapIndex);
            }

            if (IsServer)
            {
                ActiveMapIndex.Value = mapIndex;
                IsWarehouseActive.Value = (mapIndex == 1);
                ApplyWarehouseState(mapIndex == 1);
            }
            else
            {
                SetMapActiveServerRpc(mapIndex);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void SetWarehouseActiveServerRpc(bool active)
        {
            SetMapActiveServerRpc(active ? 1 : 0);
        }

        [ServerRpc(RequireOwnership = false)]
        public void SetMapActiveServerRpc(int mapIndex)
        {
            Debug.Log($"[GridMapManager-LOG] SetMapActiveServerRpc({mapIndex}) received on Server");
            ActiveMapIndex.Value = mapIndex;
            IsWarehouseActive.Value = (mapIndex == 1);
            if (WarehouseNetworkSync.Instance != null)
            {
                WarehouseNetworkSync.Instance.SetMapActive(mapIndex);
            }
            ApplyWarehouseState(mapIndex == 1);
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void SetHangarSize(float width, float height, float length)
        {
            if (!IsServer) return;
            float clampedHeight = Mathf.Max(height, 10f);
            HangarScale.Value = new Vector3(width, clampedHeight, length);
        }

        // ── Wall / Floor Positioning ──────────────────────────────────────────

        private void EnsureHangarInstance()
        {
            if (m_HangarBox == null) return;

            // If m_HangarBox is already a scene instance, nothing extra needed
            if (m_HangarBox.scene.IsValid())
            {
                return;
            }

            // On Server: instantiate and spawn if not done yet
            if (IsServer)
            {
                if (m_HangarBoxInstance == null)
                {
                    m_HangarBoxInstance = Instantiate(m_HangarBox, Vector3.zero, Quaternion.identity);
                    NetworkObject netObj = m_HangarBoxInstance.GetComponent<NetworkObject>();
                    if (netObj != null && !netObj.IsSpawned)
                    {
                        netObj.Spawn();
                        m_HangarNetObjectRef.Value = netObj;
                    }
                }
            }
            // On Client: resolve the NetworkObject spawned by the server
            else
            {
                if (m_HangarNetObjectRef.Value.TryGet(out NetworkObject netObj))
                {
                    m_HangarBoxInstance = netObj.gameObject;
                }
            }
        }

        private void ApplyHangarScale(Vector3 scale)
        {
            EnsureHangarInstance();

            float w = scale.x;
            float h = scale.y;
            float l = scale.z;

            GameObject targetObj = m_HangarBoxInstance != null ? m_HangarBoxInstance : (m_HangarBox != null && m_HangarBox.scene.IsValid() ? m_HangarBox : null);

            if (targetObj != null)
            {
                // Only show room box if Warehouse is NOT active
                bool showRoomBox = !IsWarehouseActive.Value;
                targetObj.SetActive(showRoomBox);
                targetObj.transform.position = Vector3.zero;
                targetObj.transform.rotation = Quaternion.identity;
                // Real life 1m means unity scale 1, so apply directly (X = Width, Y = Height, Z = Length)
                targetObj.transform.localScale = new Vector3(w, h, l);

                // Snap the room itself so its floor rests exactly at Y=0!
                // This fixes the issue where scaling the room by Height (e.g. 10) pushes the floor down to Y=-5.
                AutoAlignPrefabToFloor(targetObj);

                // Notify ExConGridEditor to adjust its camera view to match the new size
                if (ExConGridEditor.Instance != null)
                {
                    ExConGridEditor.Instance.SetCameraView(ExConGridEditor.Instance.IsOrthographic);
                }
            }
        }

        // ── Grid Coordinate Helpers ───────────────────────────────────────────

        public Vector3 GetWorldPosition(Vector2Int gridPos)
        {
            return new Vector3(gridPos.x * m_CellSize, 0f, gridPos.y * m_CellSize);
        }

        public Vector2Int GetGridPosition(Vector3 worldPos)
        {
            return new Vector2Int(
                Mathf.RoundToInt(worldPos.x / m_CellSize),
                Mathf.RoundToInt(worldPos.z / m_CellSize)
            );
        }

        public NetworkObject GetGridObject(Vector2Int gridPos)
        {
            if (m_PlacedObjects.TryGetValue(gridPos, out NetworkObject netObj))
            {
                return netObj;
            }
            return null;
        }

        // ── RPCs ──────────────────────────────────────────────────────────────

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SpawnGridObjectServerRpc(Vector2Int gridPos, int prefabIndex, float yRotation, Vector3 localScale)
        {
            if (!IsServer) return;

            if (m_PlacedObjects.ContainsKey(gridPos))
            {
                DestroyGridObject(gridPos);
            }

            if (prefabIndex < 0 || prefabIndex >= m_PlaceablePrefabs.Count) return;

            GameObject prefab = m_PlaceablePrefabs[prefabIndex];
            if (prefab == null) return;

            Vector3    spawnPos = GetWorldPosition(gridPos);
            Quaternion spawnRot = Quaternion.Euler(0f, yRotation, 0f);

            GameObject obj = Instantiate(prefab, spawnPos, spawnRot);

            // 1. Set scale visually immediately so bounding box calculations are mathematically correct
            obj.transform.localScale = localScale;

            // 2. Programmatically snap mesh colliders to the floor
            AutoAlignPrefabToFloor(obj);

            // 3. Spawn onto network FIRST
            NetworkObject netObj = obj.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.Spawn();
                
                // 4. NOW it is safe to write to NetworkVariables!
                GridObjectState state = obj.GetComponent<GridObjectState>();
                if (state != null)
                {
                    state.Scale.Value = localScale;
                    state.PrefabIndex.Value = prefabIndex;
                }

                m_PlacedObjects[gridPos] = netObj;
            }
            else
            {
                Destroy(obj);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void UpdateGridObjectScaleServerRpc(Vector2Int gridPos, Vector3 newScale)
        {
            if (!IsServer) return;

            if (m_PlacedObjects.TryGetValue(gridPos, out NetworkObject netObj))
            {
                if (netObj != null)
                {
                    GridObjectState state = netObj.GetComponent<GridObjectState>();
                    if (state != null)
                    {
                        state.Scale.Value = newScale;
                    }
                    else
                    {
                        netObj.transform.localScale = newScale;
                    }
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void DestroyGridObjectServerRpc(Vector2Int gridPos)
        {
            if (!IsServer) return;
            DestroyGridObject(gridPos);
        }

        private void DestroyGridObject(Vector2Int gridPos)
        {
            if (m_PlacedObjects.TryGetValue(gridPos, out NetworkObject netObj))
            {
                if (netObj != null && netObj.IsSpawned)
                {
                    netObj.Despawn(true);
                }
                m_PlacedObjects.Remove(gridPos);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void DestroyGridObjectByIdServerRpc(ulong networkObjectId)
        {
            if (!IsServer) return;
            DestroyGridObjectById(networkObjectId);
        }

        private void DestroyGridObjectById(ulong networkObjectId)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
            {
                // Remove from dictionary if it exists
                var item = m_PlacedObjects.FirstOrDefault(kvp => kvp.Value == netObj);
                if (item.Value != null)
                {
                    m_PlacedObjects.Remove(item.Key);
                }

                if (netObj.IsSpawned)
                {
                    netObj.Despawn(true);
                }
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ClearAllGridObjectsServerRpc()
        {
            if (!IsServer) return;

            List<Vector2Int> keys = new List<Vector2Int>(m_PlacedObjects.Keys);
            foreach (var key in keys)
            {
                DestroyGridObject(key);
            }
        }
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void LoadLayoutTemplateServerRpc(string templateName)
        {
            if (!IsServer) return;

            // 1. Clear any existing placed props in the room
            ClearAllGridObjectsServerRpc();

            if (string.IsNullOrEmpty(templateName))
            {
                return; // Fresh layout requested, done!
            }

            // 2. Find the template by name
            ExConLayoutTemplate template = m_LayoutTemplates.Find(t => t != null && t.name == templateName);
            if (template == null)
            {
                Debug.LogError($"[GridMapManager] Server failed to find layout template named '{templateName}'!");
                return;
            }

            // 3. Spawn each record in the template
            int spawnedCount = 0;
            foreach (var record in template.PlacedObjects)
            {
                Vector3 spawnPos = GetWorldPosition(record.GridPosition);

                // Verify it fits in the room bounds (skip if outside)
                if (ExConGridEditor.Instance != null && !ExConGridEditor.Instance.IsPositionInsideRoom(spawnPos))
                {
                    continue; 
                }

                if (record.PrefabIndex >= 0 && record.PrefabIndex < m_PlaceablePrefabs.Count)
                {
                    GameObject prefab = m_PlaceablePrefabs[record.PrefabIndex];
                    if (prefab != null)
                    {
                        Quaternion spawnRot = Quaternion.Euler(0f, record.YRotation, 0f);
                        GameObject obj = Instantiate(prefab, spawnPos, spawnRot);
                        
                        // 1. Set scale visually immediately so bounding box calculations are mathematically correct
                        obj.transform.localScale = record.Scale;

                        // 2. Programmatically snap mesh colliders to the floor
                        AutoAlignPrefabToFloor(obj);

                        // 3. Spawn onto network FIRST
                        NetworkObject netObj = obj.GetComponent<NetworkObject>();
                        if (netObj != null)
                        {
                            netObj.Spawn();
                            
                            // 4. NOW it is safe to write to NetworkVariables!
                            GridObjectState state = obj.GetComponent<GridObjectState>();
                            if (state != null)
                            {
                                state.Scale.Value = record.Scale;
                                state.PrefabIndex.Value = record.PrefabIndex;
                            }

                            m_PlacedObjects[record.GridPosition] = netObj;
                            spawnedCount++;
                        }
                    }
                }
            }
        }

        private void AutoAlignPrefabToFloor(GameObject obj)
        {
            // Use Renderers instead of Colliders to find the visual bottom of the mesh!
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            float lowestY = float.MaxValue;
            foreach (var rend in renderers)
            {
                if (rend.bounds.min.y < lowestY)
                    lowestY = rend.bounds.min.y;
            }

            if (lowestY != float.MaxValue && Mathf.Abs(lowestY) > 0.001f)
            {
                obj.transform.position -= new Vector3(0f, lowestY, 0f);
            }
        }
    }
}
