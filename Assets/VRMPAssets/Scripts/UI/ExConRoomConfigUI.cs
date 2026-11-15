using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Unity.XR.CoreUtils;

namespace XRMultiplayer
{
    /// <summary>
    /// Handles the Room Configuration panel that appears immediately after hosting.
    /// Lets the user set hangar width and length via sliders, with a live 2D rectangle
    /// preview that scales to represent the room proportions visually.
    /// Also supports applying a pre-baked Warehouse environment and spawning the VR client.
    /// Fires OnRoomConfigConfirmed once the user clicks an Apply button — one-time only.
    /// </summary>
    public class ExConRoomConfigUI : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject m_Panel;

        [Header("Width Controls")]
        [SerializeField] private Slider   m_WidthSlider;
        [SerializeField] private TMP_Text m_WidthLabel;

        [Header("Length Controls")]
        [SerializeField] private Slider   m_LengthSlider;
        [SerializeField] private TMP_Text m_LengthLabel;

        [Header("Live Preview")]
        [Tooltip("RectTransform of a UI Image used as the 2D room preview box.")]
        [SerializeField] private RectTransform m_PreviewRect;
        [SerializeField] private float         m_PreviewMaxSize = 200f;
        [SerializeField] private TMP_Text      m_PreviewWidthLabel;
        [SerializeField] private TMP_Text      m_PreviewLengthLabel;

        [Tooltip("Optional 3D GameObject in the scene/UI that acts as the 3D room preview container.")]
        [SerializeField] private Transform     m_Preview3DObject;
        [SerializeField] private float         m_Preview3DHeight = 6f;

        [Header("Height Controls")]
        [SerializeField] private TMP_InputField m_HeightInputField;

        [Header("Confirm Buttons")]
        [Tooltip("Apply button for custom procedural room sizing.")]
        [SerializeField] private Button m_ApplyButton;

        [Tooltip("Second Apply button for activating the pre-baked Warehouse environment.")]
        [SerializeField] private Button m_WarehouseApplyButton;

        [Tooltip("Third Apply button for activating the pre-baked Terrain Map environment.")]
        [SerializeField] private Button m_TerrainMapApplyButton;

        [Header("Warehouse Environment Settings")]
        [Tooltip("The pre-baked Warehouse GameObject in the scene (initially off). Enabled when Warehouse Apply is clicked.")]
        [SerializeField] private GameObject m_WarehouseObject;

        [Tooltip("Transform position where the VR Client / Rig should spawn when selecting the Warehouse.")]
        [SerializeField] private Transform m_WarehouseSpawnPoint;

        [Header("Terrain Map Environment Settings")]
        [Tooltip("The pre-baked Terrain Map GameObject in the scene (initially off). Enabled when Terrain Map Apply is clicked.")]
        [SerializeField] private GameObject m_TerrainMapObject;

        [Tooltip("Transform position where the VR Client / Rig should spawn when selecting the Terrain Map.")]
        [SerializeField] private Transform m_TerrainMapSpawnPoint;

        [Header("Hover Image Preview Settings")]
        [Tooltip("Button or UI element that triggers the image preview when mouse hovers over it. (If empty, defaults to Warehouse Apply Button).")]
        [SerializeField] private Button m_HoverPreviewButton;

        [Tooltip("GameObject containing the Image preview. Activated on mouse hover, deactivated when mouse leaves.")]
        [SerializeField] private GameObject m_HoverImageObject;

        [Header("Terrain Hover Image Preview Settings")]
        [SerializeField] private Button m_TerrainHoverPreviewButton;
        [SerializeField] private GameObject m_TerrainHoverImageObject;

        [Header("UI Panel Links")]
        [Tooltip("Standard Layout selection UI shown when procedural hangar size is applied.")]
        [SerializeField] private ExConLayoutSelectUI m_LayoutSelectUI;

        [Tooltip("Editor panel UI shown directly when Warehouse environment is applied (skipping layout selection).")]
        [SerializeField] private ExConEditorPanelUI m_EditorPanelUI;

        private float Width  => m_WidthSlider  != null ? m_WidthSlider.value  : 30f;
        private float Length => m_LengthSlider != null ? m_LengthSlider.value : 30f;
        private float Height
        {
            get
            {
                if (m_HeightInputField != null && float.TryParse(m_HeightInputField.text, out float h))
                {
                    return Mathf.Max(h, 10f); // Minimum height is 10
                }
                return 10f; // default is 10
            }
        }

        /// <summary>
        /// Raised once when the user confirms the room size or selects the warehouse.
        /// ExConDashboardUI listens to this to enable the Editor button.
        /// </summary>
        public event Action OnRoomConfigConfirmed;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Start()
        {
            if (m_WidthSlider  != null)
            {
                m_WidthSlider.onValueChanged.AddListener(_ => RefreshPreview());
            }

            if (m_LengthSlider != null)
            {
                m_LengthSlider.onValueChanged.AddListener(_ => RefreshPreview());
            }

            if (m_HeightInputField != null)
            {
                m_HeightInputField.text = "10";
                m_HeightInputField.onValueChanged.AddListener(_ => RefreshPreview());
            }

            if (m_ApplyButton != null)
            {
                m_ApplyButton.onClick.AddListener(OnApplyClicked);
            }

            if (m_WarehouseApplyButton != null)
            {
                m_WarehouseApplyButton.onClick.AddListener(OnWarehouseApplyClicked);
            }

            if (m_TerrainMapApplyButton != null)
            {
                m_TerrainMapApplyButton.onClick.AddListener(OnTerrainMapApplyClicked);
            }

            SetupHoverPreview();
            RefreshPreview();
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Show()
        {
            if (m_Panel != null)
            {
                m_Panel.SetActive(true);
            }
            if (m_Preview3DObject != null)
            {
                m_Preview3DObject.gameObject.SetActive(true);
            }
            RefreshPreview();
        }

        public void Hide()
        {
            if (m_Panel != null)
            {
                m_Panel.SetActive(false);
            }
            if (m_HoverImageObject != null)
            {
                m_HoverImageObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (m_Preview3DObject != null)
            {
                // Rotate the 3D preview container slowly (15 degrees per second) to look beautiful
                m_Preview3DObject.Rotate(Vector3.up, 15f * Time.deltaTime, Space.Self);
            }
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private void RefreshPreview()
        {
            float w = Width;
            float l = Length;

            if (m_WidthLabel  != null) m_WidthLabel .text = $"{w:F0} m";
            if (m_LengthLabel != null) m_LengthLabel.text = $"{l:F0} m";

            if (m_PreviewRect != null)
            {
                float maxVal   = Mathf.Max(w, l, 1f);
                float scale    = m_PreviewMaxSize / maxVal;
                float pxWidth  = w * scale;
                float pxLength = l * scale;
                m_PreviewRect.sizeDelta = new Vector2(pxWidth, pxLength);

                // Dynamically offset the labels so they stay cleanly outside the changing box edges
                if (m_PreviewWidthLabel != null)
                {
                    RectTransform wLabelRect = m_PreviewWidthLabel.rectTransform;
                    wLabelRect.anchoredPosition = new Vector2(0f, -pxLength * 0.5f - 25f);
                }

                if (m_PreviewLengthLabel != null)
                {
                    RectTransform lLabelRect = m_PreviewLengthLabel.rectTransform;
                    lLabelRect.anchoredPosition = new Vector2(pxWidth * 0.5f + 25f, 0f);
                }
            }

            if (m_Preview3DObject != null)
            {
                // Rescale the 3D preview container in real-time as the sliders/text fields change
                m_Preview3DObject.localScale = new Vector3(w, Height, l);
            }

            if (m_PreviewWidthLabel  != null) m_PreviewWidthLabel .text = $"{w:F0}m";
            if (m_PreviewLengthLabel != null) m_PreviewLengthLabel.text = $"{l:F0}m";
        }

        private void OnApplyClicked()
        {
            float w = Width;
            float h = Height;
            float l = Length;

            if (GridMapManager.Instance != null && GridMapManager.Instance.IsServer)
            {
                GridMapManager.Instance.SetHangarSize(w, h, l);
            }

            if (m_Preview3DObject != null)
            {
                m_Preview3DObject.gameObject.SetActive(false);
            }

            Hide();

            if (m_LayoutSelectUI != null)
            {
                m_LayoutSelectUI.Show();
            }

            OnRoomConfigConfirmed?.Invoke();
        }

        private void OnWarehouseApplyClicked()
        {
            Debug.Log("[ExConRoomConfigUI-LOG] OnWarehouseApplyClicked executed!");

            // 1. Sync warehouse activation across network via WarehouseNetworkSync & GridMapManager
            if (WarehouseNetworkSync.Instance != null)
            {
                Debug.Log("[ExConRoomConfigUI-LOG] Calling WarehouseNetworkSync.Instance.SetWarehouseActive(true)...");
                WarehouseNetworkSync.Instance.SetWarehouseActive(true);
            }

            if (GridMapManager.Instance != null)
            {
                Debug.Log("[ExConRoomConfigUI-LOG] Calling GridMapManager.Instance.SetWarehouseActive(true)...");
                GridMapManager.Instance.SetWarehouseActive(true);
            }
            else
            {
                Debug.LogWarning("[ExConRoomConfigUI-LOG] GridMapManager.Instance is NULL!");
            }

            // Fallback for local/offline if GridMapManager is not active
            if (m_WarehouseObject != null)
            {
                Debug.Log($"[ExConRoomConfigUI-LOG] Enabling local m_WarehouseObject '{m_WarehouseObject.name}'...");
                m_WarehouseObject.SetActive(true);
            }

            // 2. Hide standard procedural room box if active
            if (GridMapManager.Instance != null && GridMapManager.Instance.GetRoomBox() != null)
            {
                Debug.Log("[ExConRoomConfigUI-LOG] Disabling standard room box...");
                GridMapManager.Instance.GetRoomBox().SetActive(false);
            }

            // 3. Move / Spawn VR Client at the Warehouse Spawn Point
            TeleportVRClientToWarehouse();

            // 4. Disable 3D preview object
            if (m_Preview3DObject != null)
            {
                m_Preview3DObject.gameObject.SetActive(false);
            }

            // 5. Hide configuration UI panel
            Hide();

            // 6. Skip layout selection (Option A) and show Editor panel directly
            if (m_EditorPanelUI != null)
            {
                m_EditorPanelUI.Show();
            }

            // 7. Notify listeners
            OnRoomConfigConfirmed?.Invoke();
        }

        private void OnTerrainMapApplyClicked()
        {
            Debug.Log("[ExConRoomConfigUI-LOG] OnTerrainMapApplyClicked executed!");

            // 1. Sync Terrain Map activation across network (Map Index 2)
            if (WarehouseNetworkSync.Instance != null)
            {
                Debug.Log("[ExConRoomConfigUI-LOG] Calling WarehouseNetworkSync.Instance.SetMapActive(2)...");
                WarehouseNetworkSync.Instance.SetMapActive(2);
            }

            if (GridMapManager.Instance != null)
            {
                Debug.Log("[ExConRoomConfigUI-LOG] Calling GridMapManager.Instance.SetMapActive(2)...");
                GridMapManager.Instance.SetMapActive(2);
            }

            // Fallback for local/offline
            if (m_TerrainMapObject != null)
            {
                Debug.Log($"[ExConRoomConfigUI-LOG] Enabling local m_TerrainMapObject '{m_TerrainMapObject.name}'...");
                m_TerrainMapObject.SetActive(true);
            }

            // 2. Hide standard procedural room box
            if (GridMapManager.Instance != null && GridMapManager.Instance.GetRoomBox() != null)
            {
                GridMapManager.Instance.GetRoomBox().SetActive(false);
            }

            // 3. Teleport VR Client to Terrain Map spawn point
            TeleportVRClientToTerrainMap();

            // 4. Disable 3D preview
            if (m_Preview3DObject != null)
            {
                m_Preview3DObject.gameObject.SetActive(false);
            }

            // 5. Hide configuration UI
            Hide();

            // 6. Show Editor Panel directly
            if (m_EditorPanelUI != null)
            {
                m_EditorPanelUI.Show();
            }

            // 7. Notify listeners
            OnRoomConfigConfirmed?.Invoke();
        }

        private void TeleportVRClientToWarehouse()
        {
            if (XRINetworkPlayer.TeleportToSpawnAreaTag())
            {
                Debug.Log("[ExConRoomConfigUI-LOG] Teleported to tagged Spawn Area plane in Warehouse!");
                return;
            }

            Vector3 spawnPos = m_WarehouseSpawnPoint != null ? m_WarehouseSpawnPoint.position : Vector3.zero;
            Quaternion spawnRot = m_WarehouseSpawnPoint != null ? m_WarehouseSpawnPoint.rotation : Quaternion.identity;
            Debug.Log($"[ExConRoomConfigUI-LOG] TeleportVRClientToWarehouse to pos={spawnPos}, rot={spawnRot.eulerAngles}");

            XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();
            if (xrOrigin != null)
            {
                xrOrigin.transform.SetPositionAndRotation(spawnPos, spawnRot);
                Debug.Log("[ExConRoomConfigUI-LOG] Teleported XROrigin to Warehouse!");
            }

            if (XRINetworkPlayer.LocalPlayer != null)
            {
                XRINetworkPlayer.LocalPlayer.transform.SetPositionAndRotation(spawnPos, spawnRot);
                Debug.Log("[ExConRoomConfigUI-LOG] Teleported XRINetworkPlayer.LocalPlayer to Warehouse!");
            }

            CharacterResetter resetterWarehouse = FindFirstObjectByType<CharacterResetter>();
            if (resetterWarehouse != null)
            {
                resetterWarehouse.SetResetPosition(spawnPos);
            }
        }

        private void TeleportVRClientToTerrainMap()
        {
            if (XRINetworkPlayer.TeleportToSpawnAreaTag())
            {
                Debug.Log("[ExConRoomConfigUI-LOG] Teleported to tagged Spawn Area plane in Terrain Map!");
                return;
            }

            Vector3 spawnPos = m_TerrainMapSpawnPoint != null ? m_TerrainMapSpawnPoint.position : Vector3.zero;
            Quaternion spawnRot = m_TerrainMapSpawnPoint != null ? m_TerrainMapSpawnPoint.rotation : Quaternion.identity;

            Debug.Log($"[ExConRoomConfigUI-LOG] TeleportVRClientToTerrainMap to pos={spawnPos}, rot={spawnRot.eulerAngles}");

            XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();
            if (xrOrigin != null)
            {
                xrOrigin.transform.SetPositionAndRotation(spawnPos, spawnRot);
                Debug.Log("[ExConRoomConfigUI-LOG] Teleported XROrigin to Terrain Map!");
            }

            if (XRINetworkPlayer.LocalPlayer != null)
            {
                XRINetworkPlayer.LocalPlayer.transform.SetPositionAndRotation(spawnPos, spawnRot);
                Debug.Log("[ExConRoomConfigUI-LOG] Teleported XRINetworkPlayer.LocalPlayer to Terrain Map!");
            }

            CharacterResetter resetterTerrain = FindFirstObjectByType<CharacterResetter>();
            if (resetterTerrain != null)
            {
                resetterTerrain.SetResetPosition(spawnPos);
            }
        }

        // ── Hover Image Preview Logic ─────────────────────────────────────────

        private void SetupHoverPreview()
        {
            if (m_HoverImageObject != null)
            {
                m_HoverImageObject.SetActive(false); // Ensure off by default
            }

            // Warehouse hover preview
            Button targetButton = m_HoverPreviewButton != null ? m_HoverPreviewButton : m_WarehouseApplyButton;
            if (targetButton != null)
            {
                AddHoverListeners(targetButton.gameObject, m_HoverImageObject);
            }

            // Terrain hover preview
            if (m_TerrainHoverImageObject != null)
            {
                m_TerrainHoverImageObject.SetActive(false);
            }
            Button terrainTarget = m_TerrainHoverPreviewButton != null ? m_TerrainHoverPreviewButton : m_TerrainMapApplyButton;
            if (terrainTarget != null)
            {
                AddHoverListeners(terrainTarget.gameObject, m_TerrainHoverImageObject);
            }
        }

        private void AddHoverListeners(GameObject targetObject, GameObject hoverImageObj)
        {
            if (targetObject == null || hoverImageObj == null) return;

            EventTrigger trigger = targetObject.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = targetObject.AddComponent<EventTrigger>();
            }

            // Pointer Enter (Mouse Hover)
            EventTrigger.Entry entryEnter = new EventTrigger.Entry();
            entryEnter.eventID = EventTriggerType.PointerEnter;
            entryEnter.callback.AddListener((_) => hoverImageObj.SetActive(true));
            trigger.triggers.Add(entryEnter);

            // Pointer Exit (Mouse Leave)
            EventTrigger.Entry entryExit = new EventTrigger.Entry();
            entryExit.eventID = EventTriggerType.PointerExit;
            entryExit.callback.AddListener((_) => hoverImageObj.SetActive(false));
            trigger.triggers.Add(entryExit);
        }
    }
}

