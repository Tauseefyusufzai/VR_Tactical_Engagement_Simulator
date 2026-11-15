using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace XRMultiplayer
{
    /// <summary>
    /// Handles all map-editor gameplay logic for the PC ExCon operator.
    /// Responsibilities: camera panning/zooming, ghost preview snapping,
    /// grid-plane raycast, and calling placement/erase RPCs on GridMapManager.
    /// Zero UI panel management — that belongs to ExConEditorPanelUI.
    /// </summary>
    public class ExConGridEditor : MonoBehaviour
    {
        public static ExConGridEditor Instance { get; private set; }

        [Header("Editor Camera")]
        [SerializeField] private Camera m_EditorCamera;
        [SerializeField] private float  m_PanSpeed  = 15.0f;
        [SerializeField] private float  m_ZoomSpeed = 5.0f;

        [Header("Ghost Prefabs (same order as GridMapManager.m_PlaceablePrefabs)")]
        [SerializeField] private List<GameObject> m_GhostPrefabs = new List<GameObject>();

        private int     m_SelectedToolIndex  = -1;
        private bool    m_IsEraserActive     = false;
        private float   m_CurrentRotation   = 0f;
        private Vector3 m_ActiveScale        = Vector3.one;
        private float   m_GhostFloorOffset   = 0f; // Y offset so ghost sits on floor, not through it

        private GameObject m_ActiveGhostInstance;
        private bool       m_WasServerLastFrame = false;

        private float m_CameraPitch = 90f;
        private float m_CameraYaw   = 0f;
        [SerializeField] private float m_RotationSpeed = 0.15f;
        [SerializeField] private string m_BlockCameraTag = "BlockCamera";

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                if (Instance.m_GhostPrefabs.Count == 0 && this.m_GhostPrefabs.Count > 0)
                {
                    Destroy(Instance);
                    Instance = this;
                }
                else
                {
                    Destroy(this);
                    return;
                }
            }
            Instance = this;
        }

        private void Start()
        {
            if (m_EditorCamera == null)
            {
                m_EditorCamera = Camera.main;
            }

            if (m_EditorCamera != null)
            {
                // Programmatically hide the player avatar from the operator's view
                int playerLayer = LayerMask.NameToLayer("Player");
                if (playerLayer != -1)
                {
                    m_EditorCamera.cullingMask &= ~(1 << playerLayer);
                }

                int mirrorLayer = LayerMask.NameToLayer("Mirror");
                if (mirrorLayer != -1)
                {
                    m_EditorCamera.cullingMask &= ~(1 << mirrorLayer);
                }

                SetCameraView(false); // default top-down perspective view
            }

            UpdateGhostPreview();
        }

        private void Update()
        {
            bool isServer = GridMapManager.Instance != null && GridMapManager.Instance.IsServer;

            if (isServer != m_WasServerLastFrame)
            {
                m_WasServerLastFrame = isServer;
            }

            if (!isServer)
            {
                HideGhost();
                return;
            }


            HandleCameraMovement();
            HandleKeyboardInput();
            HandlePlacementRaycast();
        }

        // ── Camera ────────────────────────────────────────────────────────────

        public bool IsOrthographic => m_EditorCamera != null && m_EditorCamera.orthographic;

        public bool IsDraggingFromUI { get; set; } = false;

        public void SetCameraView(bool orthographic)
        {
            if (m_EditorCamera == null)
            {
                return;
            }

            float roomW = 30f, roomL = 30f;
            Vector3 centerPos = Vector3.zero;
            float floorY = 0f;
            float ceilingY = 5.5f;

            if (GridMapManager.Instance != null)
            {
                roomW = GridMapManager.Instance.HangarScale.Value.x;
                roomL = GridMapManager.Instance.HangarScale.Value.z;

                GameObject roomBox = GridMapManager.Instance.GetRoomBox();
                if (roomBox != null)
                {
                    // Try to get bounds from the renderer of the room box to determine exact center, floor, and ceiling height
                    Renderer boxRenderer = roomBox.GetComponentInChildren<Renderer>();
                    if (boxRenderer != null)
                    {
                        Bounds bounds = boxRenderer.bounds;
                        centerPos = bounds.center;
                        floorY = bounds.min.y;
                        ceilingY = bounds.max.y;
                        
                        // Override roomW and roomL with actual world-space mesh dimensions
                        roomW = bounds.size.x;
                        roomL = bounds.size.z;
                    }
                    else
                    {
                        // Fallback to transform position if no renderer is found
                        centerPos = roomBox.transform.position;
                        floorY = centerPos.y;
                        ceilingY = centerPos.y + GridMapManager.Instance.HangarScale.Value.y; // dynamic height
                    }
                }
            }

            m_EditorCamera.orthographic = orthographic;

            // Place camera exactly at the ceiling height (top wall)
            float cameraY = ceilingY;

            if (orthographic)
            {
                // Ensure the camera viewport is large enough to cover both the width and length of the room
                float size = Mathf.Max(roomW / (m_EditorCamera.aspect * 2f), roomL * 0.5f);
                size = Mathf.Max(size, 1f); // enforce a safe minimum size

                // Center the camera over the box center, exactly at the ceiling looking straight down
                Vector3 targetPos = new Vector3(centerPos.x, cameraY, centerPos.z);
                m_EditorCamera.transform.position = targetPos;
                m_EditorCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                m_EditorCamera.orthographicSize   = size;
            }
            else
            {
                // Place camera at the center of the width, right at the back wall (entrance), looking straight front (positive Z)
                float margin = 1.0f;
                float minZ = centerPos.z - (roomL * 0.5f);
                if (GridMapManager.Instance != null)
                {
                    GameObject roomBox = GridMapManager.Instance.GetRoomBox();
                    if (roomBox != null)
                    {
                        Renderer boxRenderer = roomBox.GetComponentInChildren<Renderer>();
                        if (boxRenderer != null)
                        {
                            minZ = boxRenderer.bounds.min.z;
                        }
                    }
                }

                Vector3 targetPos = new Vector3(centerPos.x, floorY + 1.6f, minZ + margin);
                m_EditorCamera.transform.position = targetPos;
                m_EditorCamera.transform.rotation = Quaternion.Euler(0f, 0f, 0f);

                m_CameraPitch = 0f;
                m_CameraYaw   = 0f;
            }
        }

        private void HandleCameraMovement()
        {
            if (m_EditorCamera == null) return;

            // If the mouse is hovering over a GameObject tagged to block camera, block movement completely
            if (!IsDraggingFromUI && IsHoveringBlockedObject())
            {
                return;
            }

            // If the camera is in orthographic mode, lock it to the room center and exit early (no movement or right click allowed!)
            if (m_EditorCamera.orthographic)
            {
                float roomW = 30f, roomL = 30f;
                Vector3 centerPos = Vector3.zero;
                float ceilingY = 6.0f;

                if (GridMapManager.Instance != null)
                {
                    roomW = GridMapManager.Instance.HangarScale.Value.x;
                    roomL = GridMapManager.Instance.HangarScale.Value.z;

                    GameObject roomBox = GridMapManager.Instance.GetRoomBox();
                    if (roomBox != null)
                    {
                        Renderer boxRenderer = roomBox.GetComponentInChildren<Renderer>();
                        if (boxRenderer != null)
                        {
                            Bounds bounds = boxRenderer.bounds;
                            centerPos = bounds.center;
                            ceilingY = bounds.max.y;
                            roomW = bounds.size.x;
                            roomL = bounds.size.z;
                        }
                        else
                        {
                            centerPos = roomBox.transform.position;
                            ceilingY = centerPos.y + GridMapManager.Instance.HangarScale.Value.y;
                        }
                    }
                }

                // Force center position, straight down rotation, and dynamic aspect-constrained zoom size
                float size = Mathf.Max(roomW / (m_EditorCamera.aspect * 2f), roomL * 0.5f);
                size = Mathf.Max(size, 1f);

                m_EditorCamera.transform.position = new Vector3(centerPos.x, ceilingY, centerPos.z);
                m_EditorCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                m_EditorCamera.orthographicSize = size;
                return;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                // Right Mouse Button is held -> Look around (Yaw/Pitch rotation) - only in perspective mode!
                if (mouse.rightButton.isPressed)
                {
                    Vector2 mouseDelta = mouse.delta.ReadValue();
                    m_CameraYaw   += mouseDelta.x * m_RotationSpeed;
                    m_CameraPitch -= mouseDelta.y * m_RotationSpeed;
                    m_CameraPitch  = Mathf.Clamp(m_CameraPitch, -89f, 89f);

                    m_EditorCamera.transform.rotation = Quaternion.Euler(m_CameraPitch, m_CameraYaw, 0f);
                }

                // Scroll Wheel -> Zoom / Size Adjust
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    // If holding a prop, scroll wheel SCALES the prop!
                    if (m_SelectedToolIndex >= 0)
                    {
                        var kb = Keyboard.current;
                        float deltaScale = Mathf.Sign(scroll) * 0.5f;

                        if (kb != null && kb.leftCtrlKey.isPressed)
                        {
                            // Left Ctrl + Scroll -> Scale X axis only (Width)
                            AdjustActiveScale(new Vector3(deltaScale, 0f, 0f));
                        }
                        else if (kb != null && kb.leftAltKey.isPressed)
                        {
                            // Left Alt + Scroll -> Scale Y axis only (Height)
                            AdjustActiveScale(new Vector3(0f, deltaScale, 0f));
                        }
                        else
                        {
                            // Plain Scroll -> Uniform scale (all axes)
                            AdjustActiveScale(new Vector3(deltaScale, deltaScale, deltaScale));
                        }
                    }
                    // Otherwise, scroll wheel ZOOMS the camera
                    else
                    {
                        m_EditorCamera.transform.Translate(Vector3.forward * scroll * 0.01f * m_ZoomSpeed, Space.Self);
                    }
                }
                
                // Middle Click -> Rotate active ghost
                if (m_SelectedToolIndex >= 0 && mouse.middleButton.wasPressedThisFrame)
                {
                    RotateActiveGhost();
                }
            }

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                float h = 0f;
                float v = 0f;
                float yMove = 0f;

                if (keyboard.wKey.isPressed || keyboard.upArrowKey   .isPressed) v += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey .isPressed) v -= 1f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey .isPressed) h -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) h += 1f;
                
                // Q & E keys for going Down & Up
                if (keyboard.qKey.isPressed) yMove -= 1f;
                if (keyboard.eKey.isPressed) yMove += 1f;

                if (h != 0f || v != 0f || yMove != 0f)
                {

                    // Calculate horizontal forward and right vectors to prevent W/S from altering height (diving into the ground)
                    Vector3 forward = m_EditorCamera.transform.forward;
                    forward.y = 0f;
                    if (forward.sqrMagnitude < 0.001f)
                    {
                        // Fallback when looking straight down: use negative up vector projected on XZ
                        forward = -m_EditorCamera.transform.up;
                        forward.y = 0f;
                    }
                    forward.Normalize();

                    Vector3 right = m_EditorCamera.transform.right;
                    right.y = 0f;
                    right.Normalize();

                    // Calculate direction on XZ plane
                    Vector3 moveDir = (forward * v + right * h).normalized;

                    // Translate globally along the horizontal plane
                    m_EditorCamera.transform.Translate(moveDir * m_PanSpeed * Time.deltaTime, Space.World);

                    // Move up/down globally using Q & E
                    if (yMove != 0f)
                    {
                        m_EditorCamera.transform.Translate(Vector3.up * yMove * m_PanSpeed * Time.deltaTime, Space.World);
                    }
                }
            }

            // Clamp camera position inside room boundaries to prevent flying outside
            if (GridMapManager.Instance != null)
            {
                GameObject roomBox = GridMapManager.Instance.GetRoomBox();
                if (roomBox != null)
                {
                    Renderer boxRenderer = roomBox.GetComponentInChildren<Renderer>();
                    bool ortho = m_EditorCamera.orthographic;
                    float marginX = 1.0f;
                    float marginZ = 1.0f;
                    if (ortho)
                    {
                        marginZ = m_EditorCamera.orthographicSize;
                        marginX = marginZ * m_EditorCamera.aspect;
                    }

                    if (boxRenderer != null)
                    {
                        Bounds bounds = boxRenderer.bounds;

                        float minX = bounds.min.x + marginX;
                        float maxX = bounds.max.x - marginX;
                        float minZ = bounds.min.z + marginZ;
                        float maxZ = bounds.max.z - marginZ;

                        // Ensure minimum constraints don't cross max constraints for wide screens or tiny rooms
                        if (minX > maxX) { float temp = minX; minX = maxX; maxX = temp; }
                        if (minZ > maxZ) { float temp = minZ; minZ = maxZ; maxZ = temp; }

                        Vector3 pos = m_EditorCamera.transform.position;

                        pos.x = Mathf.Clamp(pos.x, minX, maxX);
                        pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
                        
                        if (ortho)
                        {
                            pos.y = bounds.max.y;
                        }
                        else
                        {
                            pos.y = Mathf.Clamp(pos.y, bounds.min.y + 0.5f, bounds.max.y - 0.5f);
                        }

                        m_EditorCamera.transform.position = pos;
                    }
                    else
                    {
                        // Fallback using HangarScale if renderer is missing
                        float roomW = GridMapManager.Instance.HangarScale.Value.x;
                        float roomL = GridMapManager.Instance.HangarScale.Value.z;
                        Vector3 centerPos = roomBox.transform.position;
                        float floorY = centerPos.y;
                        float ceilingY = centerPos.y + GridMapManager.Instance.HangarScale.Value.y;

                        float minX = centerPos.x - (roomW * 0.5f) + marginX;
                        float maxX = centerPos.x + (roomW * 0.5f) - marginX;
                        float minZ = centerPos.z - (roomL * 0.5f) + marginZ;
                        float maxZ = centerPos.z + (roomL * 0.5f) - marginZ;

                        if (minX > maxX) { float temp = minX; minX = maxX; maxX = temp; }
                        if (minZ > maxZ) { float temp = minZ; minZ = maxZ; maxZ = temp; }

                        Vector3 pos = m_EditorCamera.transform.position;

                        pos.x = Mathf.Clamp(pos.x, minX, maxX);
                        pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
                        
                        if (ortho)
                        {
                            pos.y = ceilingY;
                        }
                        else
                        {
                            pos.y = Mathf.Clamp(pos.y, floorY + 0.5f, ceilingY - 0.5f);
                        }

                        m_EditorCamera.transform.position = pos;
                    }
                }
            }
        }

        // ── Keyboard Input ────────────────────────────────────────────────────

        private void HandleKeyboardInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;



            // Press 'R' to rotate ghost
            if (keyboard.rKey.wasPressedThisFrame) RotateActiveGhost();

            // Press 'Escape' to empty hand
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                SelectTool(-1);
            }
        }

        // ── Tool Selection ────────────────────────────────────────────────────

        public void SelectTool(int index)
        {
            m_IsEraserActive    = false;
            m_SelectedToolIndex = index;
            m_ActiveScale       = Vector3.one; // Reset scale when changing tools
            UpdateGhostPreview();
        }

        public void ToggleEraser()
        {
            m_IsEraserActive    = !m_IsEraserActive;
            m_SelectedToolIndex = -1;
            UpdateGhostPreview();
        }

        // ── Ghost Preview ─────────────────────────────────────────────────────

        public void RotateActiveGhost()
        {
            m_CurrentRotation = (m_CurrentRotation + 90f) % 360f;
            if (m_ActiveGhostInstance != null)
                m_ActiveGhostInstance.transform.rotation = Quaternion.Euler(0f, m_CurrentRotation, 0f);
        }

        public void AdjustActiveScale(Vector3 delta)
        {
            m_ActiveScale.x = Mathf.Clamp(m_ActiveScale.x + delta.x, 0.5f, 10f);
            m_ActiveScale.y = Mathf.Clamp(m_ActiveScale.y + delta.y, 0.5f, 10f);
            m_ActiveScale.z = Mathf.Clamp(m_ActiveScale.z + delta.z, 0.5f, 10f);
            if (m_ActiveGhostInstance != null)
            {
                m_ActiveGhostInstance.transform.localScale = m_ActiveScale;
                // Recalculate floor offset whenever scale changes so ghost doesn't sink
                m_GhostFloorOffset = CalculateGhostFloorOffset(m_ActiveGhostInstance);
            }
        }

        public void SetActiveScale(Vector3 scale)
        {
            m_ActiveScale = scale;
            if (m_ActiveGhostInstance != null)
            {
                m_ActiveGhostInstance.transform.localScale = m_ActiveScale;
                m_GhostFloorOffset = CalculateGhostFloorOffset(m_ActiveGhostInstance);
            }
        }

        private void UpdateGhostPreview()
        {
            if (m_ActiveGhostInstance != null)
            {
                Destroy(m_ActiveGhostInstance);
                m_ActiveGhostInstance = null;
            }

            m_GhostFloorOffset = 0f;

            if (m_IsEraserActive) return;
            if (m_SelectedToolIndex < 0) return;
            if (m_SelectedToolIndex >= m_GhostPrefabs.Count) return;

            GameObject ghostPrefab = m_GhostPrefabs[m_SelectedToolIndex];
            if (ghostPrefab == null) return;

            m_ActiveGhostInstance = Instantiate(ghostPrefab);
            m_ActiveGhostInstance.transform.rotation   = Quaternion.Euler(0f, m_CurrentRotation, 0f);
            m_ActiveGhostInstance.transform.localScale = m_ActiveScale;

            // Calculate how much to raise the ghost so its bottom sits at Y=0 (same logic as AutoAlignPrefabToFloor)
            m_GhostFloorOffset = CalculateGhostFloorOffset(m_ActiveGhostInstance);

            foreach (var col in m_ActiveGhostInstance.GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }
        }

        /// <summary>Returns the Y offset needed to lift a ghost so its bottom renderer edge sits exactly on the floor (Y=0).</summary>
        private float CalculateGhostFloorOffset(GameObject ghost)
        {
            Renderer[] renderers = ghost.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 0f;

            float lowestY = float.MaxValue;
            foreach (var rend in renderers)
            {
                if (rend.bounds.min.y < lowestY)
                    lowestY = rend.bounds.min.y;
            }

            // Convert world-space lowestY to local-space relative to the pivot
            float localLowestY = lowestY - ghost.transform.position.y;

            // localLowestY is negative when pivot is above the bottom of the mesh
            return (localLowestY < 0f) ? -localLowestY : 0f;
        }

        // ── Placement Raycast ─────────────────────────────────────────────────

        private bool GetHoveredGridPos(out Vector2Int gridPos)
        {
            gridPos = Vector2Int.zero;
            if (m_EditorCamera == null || GridMapManager.Instance == null) return false;
            var mouse = Mouse.current;
            if (mouse == null) return false;
            
            Ray ray = m_EditorCamera.ScreenPointToRay(mouse.position.ReadValue());
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float enter))
            {
                gridPos = GridMapManager.Instance.GetGridPosition(ray.GetPoint(enter));
                return true;
            }
            return false;
        }

        private void HandlePlacementRaycast()
        {
            if (!IsDraggingFromUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                HideGhost();
                return;
            }

            if (m_EditorCamera == null || GridMapManager.Instance == null) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 mousePos = mouse.position.ReadValue();
            Ray     ray      = m_EditorCamera.ScreenPointToRay(mousePos);
            Plane   ground   = new Plane(Vector3.up, Vector3.zero);

            // Reject degenerate ground hits (near-parallel ray hits millions of units away)
            bool hitGround = ground.Raycast(ray, out float enter) && enter < 300f;
            Vector3 hitPoint = hitGround ? ray.GetPoint(enter) : Vector3.zero;
            Vector2Int gridPos = hitGround ? GridMapManager.Instance.GetGridPosition(hitPoint) : Vector2Int.zero;

            // Ghost Preview updating (only if we hit ground at a sane distance)
            if (hitGround && !m_IsEraserActive && m_ActiveGhostInstance != null)
            {
                if (!m_ActiveGhostInstance.activeSelf) m_ActiveGhostInstance.SetActive(true);
                // Clamp XZ inside room, then apply floor offset so ghost sits on top of floor
                Vector3 rawSnappedPos = GridMapManager.Instance.GetWorldPosition(gridPos);
                Vector3 clampedWorldPos = ClampWorldPosToRoomXZ(rawSnappedPos);
                clampedWorldPos.y = m_GhostFloorOffset;
                m_ActiveGhostInstance.transform.position = clampedWorldPos;
            }
            else if (!hitGround && m_ActiveGhostInstance != null)
            {
                // Don't hide ghost when camera angle is extreme — keep it at its last valid position
            }

            // Mouse Click Handling
            if (mouse.leftButton.wasPressedThisFrame)
            {
                bool hasPhysicsHit = Physics.Raycast(ray, out RaycastHit globalHit);

                if (m_IsEraserActive)
                {
                    if (hasPhysicsHit)
                    {
                        var eraseObj = globalHit.collider.GetComponentInParent<Unity.Netcode.NetworkObject>();
                        if (eraseObj != null && eraseObj.GetComponent<GridObjectState>() != null)
                        {
                            GridMapManager.Instance.DestroyGridObjectByIdServerRpc(eraseObj.NetworkObjectId);
                            return;
                        }
                    }
                    
                    if (hitGround)
                    {
                        GridMapManager.Instance.DestroyGridObjectServerRpc(gridPos);
                    }
                }
                else if (m_SelectedToolIndex >= 0 && m_SelectedToolIndex < m_GhostPrefabs.Count)
                {
                    // Placement Mode
                    if (hitGround)
                    {
                        Vector3 spawnPos = GridMapManager.Instance.GetWorldPosition(gridPos);
                        if (IsPositionInsideRoom(spawnPos))
                        {
                            GridMapManager.Instance.SpawnGridObjectServerRpc(gridPos, m_SelectedToolIndex, m_CurrentRotation, m_ActiveScale);
                            SelectTool(-1); // Instantly drop the prop and clear the hand!
                        }
                    }
                }
                else if (m_SelectedToolIndex == -1 && !m_IsEraserActive)
                {
                    // Pick Up Mode
                    Unity.Netcode.NetworkObject placedObj = null;

                    if (hasPhysicsHit)
                    {
                        placedObj = globalHit.collider.GetComponentInParent<Unity.Netcode.NetworkObject>();
                    }

                    // 2. Fallback to mathematical floor click (if they clicked near the bottom)
                    if (placedObj == null && hitGround)
                    {
                        placedObj = GridMapManager.Instance.GetGridObject(gridPos);
                    }

                    if (placedObj != null)
                    {
                        GridObjectState state = placedObj.GetComponent<GridObjectState>();
                        if (state != null)
                        {
                            if (state.PrefabIndex.Value >= 0)
                            {
                                int prefabIndex = state.PrefabIndex.Value;
                                Vector3 scale = state.Scale.Value;
                                float rotation = placedObj.transform.rotation.eulerAngles.y;

                                // Cache the prop's world position BEFORE destroying it — used to place ghost immediately
                                Vector3 pickupPos = new Vector3(placedObj.transform.position.x, 0f, placedObj.transform.position.z);

                                GridMapManager.Instance.DestroyGridObjectByIdServerRpc(placedObj.NetworkObjectId);
                                m_SelectedToolIndex = prefabIndex;
                                m_ActiveScale = scale;
                                m_CurrentRotation = rotation;
                                UpdateGhostPreview();

                                // Always position ghost at prop's last known position (works even if camera isn't looking at floor)
                                if (m_ActiveGhostInstance != null)
                                {
                                    Vector3 clampedPickupPos = ClampWorldPosToRoomXZ(pickupPos);
                                    m_ActiveGhostInstance.transform.position = clampedPickupPos;
                                    m_ActiveGhostInstance.SetActive(true);
                                }
                            }
                            else
                            {
                                Debug.LogError($"[ExConGridEditor] ERROR: PrefabIndex is {state.PrefabIndex.Value}! This means the prop wasn't saved correctly when it spawned.");
                            }
                        }
                        // Non-grid objects (like AI enemies, players, weapons) do not have GridObjectState and should be ignored by the grid editor
                        else
                        {
                            // Ignore non-grid objects
                        }
                    }
                }
            }
        }

        private void HideGhost()
        {
            if (m_ActiveGhostInstance != null && m_ActiveGhostInstance.activeSelf)
                m_ActiveGhostInstance.SetActive(false);
        }

        public void DropActiveProp()
        {
            IsDraggingFromUI = false;

            // If drop occurred over UI or a blocked panel, cancel the drop
            if (IsHoveringBlockedObject())
            {
                Debug.LogWarning("[ExConGridEditor] DropActiveProp: Spawning cancelled. Dropped over a UI element or a blocked panel.");
                SelectTool(-1);
                return;
            }

            Debug.Log($"[ExConGridEditor] DropActiveProp: Attempting placement. SelectedIndex={m_SelectedToolIndex} | CurrentScale={m_ActiveScale} | Rotation={m_CurrentRotation}");

            if (m_SelectedToolIndex >= 0 && m_SelectedToolIndex < m_GhostPrefabs.Count)
            {
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    Vector2 mousePos = mouse.position.ReadValue();
                    Ray     ray      = m_EditorCamera.ScreenPointToRay(mousePos);
                    Plane   ground   = new Plane(Vector3.up, Vector3.zero);

                    if (ground.Raycast(ray, out float enter))
                    {
                        Vector3    hitPoint = ray.GetPoint(enter);
                        Vector2Int gridPos  = GridMapManager.Instance.GetGridPosition(hitPoint);
                        Vector3    spawnPos = GridMapManager.Instance.GetWorldPosition(gridPos);

                        bool inside = IsPositionInsideRoom(spawnPos);
                        Debug.Log($"[ExConGridEditor] DropActiveProp: HitPoint={hitPoint} | SnappedPosition={spawnPos} | GridPos={gridPos} | InsideRoomBounds={inside}");

                        // Enforce room boundaries - only place if inside
                        if (inside)
                        {
                            if (GridMapManager.Instance != null)
                            {
                                GridMapManager.Instance.SpawnGridObjectServerRpc(gridPos, m_SelectedToolIndex, m_CurrentRotation, m_ActiveScale);
                            }
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[ExConGridEditor] DropActiveProp: Raycast failed to hit ground plane! ray={ray}");
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[ExConGridEditor] DropActiveProp: SelectedIndex {m_SelectedToolIndex} is OUT OF BOUNDS of m_GhostPrefabs count ({m_GhostPrefabs.Count})!");
            }

            // Deselect tool and hide/destroy the ghost preview
            SelectTool(-1);
        }

        public bool IsPositionInsideRoom(Vector3 position)
        {
            if (GridMapManager.Instance == null) return true;
            GameObject roomBox = GridMapManager.Instance.GetRoomBox();
            if (roomBox == null) return true;

            Renderer boxRenderer = roomBox.GetComponentInChildren<Renderer>();
            if (boxRenderer != null)
            {
                Bounds bounds = boxRenderer.bounds;
                // Allow a small 0.05m tolerance for coordinates exactly on the room edge
                return position.x >= (bounds.min.x - 0.05f) && position.x <= (bounds.max.x + 0.05f) &&
                       position.z >= (bounds.min.z - 0.05f) && position.z <= (bounds.max.z + 0.05f);
            }
            return true;
        }

        /// <summary>Clamp a world XZ position so it stays strictly inside the room boundaries on the ground plane (Y stays 0).</summary>
        private Vector3 ClampWorldPosToRoomXZ(Vector3 worldPos)
        {
            if (GridMapManager.Instance == null) return worldPos;
            GameObject roomBox = GridMapManager.Instance.GetRoomBox();
            if (roomBox == null) return worldPos;

            Renderer boxRenderer = roomBox.GetComponentInChildren<Renderer>();
            if (boxRenderer != null)
            {
                Bounds bounds = boxRenderer.bounds;
                worldPos.x = Mathf.Clamp(worldPos.x, bounds.min.x, bounds.max.x);
                worldPos.z = Mathf.Clamp(worldPos.z, bounds.min.z, bounds.max.z);
                worldPos.y = 0f;
            }
            else
            {
                Debug.LogError($"[ExConGridEditor] ClampWorldPosToRoomXZ: No Renderer found on roomBox '{roomBox.name}' - cannot clamp ghost position!");
            }
            return worldPos;
        }

        private bool IsHoveringBlockedObject()
        {
            var mouse = Mouse.current;
            if (mouse == null) return false;
            Vector2 mousePos = mouse.position.ReadValue();

            // 1. Raycast UI elements first using EventSystem
            if (EventSystem.current != null)
            {
                PointerEventData eventData = new PointerEventData(EventSystem.current);
                eventData.position = mousePos;
                List<RaycastResult> results = new List<RaycastResult>();
                EventSystem.current.RaycastAll(eventData, results);
                foreach (var result in results)
                {
                    if (result.gameObject != null && result.gameObject.CompareTag(m_BlockCameraTag))
                    {
                        return true;
                    }
                }
            }

            // 2. Raycast 3D elements in the scene
            if (m_EditorCamera != null)
            {
                Ray ray = m_EditorCamera.ScreenPointToRay(mousePos);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    if (hit.collider.gameObject.CompareTag(m_BlockCameraTag))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void OnDestroy()
        {
            if (m_ActiveGhostInstance != null)
            {
                Destroy(m_ActiveGhostInstance);
            }
        }
    }
}
