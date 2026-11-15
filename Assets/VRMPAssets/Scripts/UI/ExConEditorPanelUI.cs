using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace XRMultiplayer
{
    /// <summary>
    /// Manages the Map Editor panel — prop selection, wall scaling, camera views,
    /// eraser, and clear-all. Communicates exclusively via ExConGridEditor.Instance
    /// and GridMapManager.Instance.
    /// </summary>
    public class ExConEditorPanelUI : MonoBehaviour
    {
        [Header("Panel Root")]
        [SerializeField] private GameObject m_Panel;

        [Header("Prop Palette")]
        [Tooltip("Buttons in the same order as GridMapManager.m_PlaceablePrefabs")]
        [SerializeField] private Button[] m_PropButtons;

        [Tooltip("Optional highlight images per button — same length as m_PropButtons")]
        [SerializeField] private Image[] m_PropButtonHighlights;

        [Header("Wall Scale (indices 0, 1, 2 only)")]
        [SerializeField] private GameObject m_WallScalePanel;
        [SerializeField] private Slider     m_WallLengthSlider;
        [SerializeField] private Slider     m_WallHeightSlider;
        [SerializeField] private TMP_Text   m_WallScaleValueText;

        [Header("Camera View")]
        [SerializeField] private Button m_OrthoViewButton;
        [SerializeField] private Button m_PerspViewButton;

        [Header("Tool Buttons")]
        [SerializeField] private Button m_EraserButton;
        [SerializeField] private Button m_ClearAllButton;
        [SerializeField] private Button m_CloseButton;

        [Header("Inventory Panel Link")]
        [SerializeField] private Button m_OpenInventoryButton;
        [SerializeField] private ExConInventoryPanelUI m_InventoryPanelUI;

        [Header("Begin Session Button")]
        [SerializeField] private Button m_BeginSessionButton;

        private int  m_ActiveToolIndex = -1;
        private static readonly int[] k_WallTypeIndices = { 0, 1, 2 };

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Start()
        {
            if (m_PropButtons != null)
            {
                for (int i = 0; i < m_PropButtons.Length; i++)
                {
                    int capturedIndex = i;
                    if (m_PropButtons[i] != null)
                    {
                        m_PropButtons[i].onClick.AddListener(() => OnPropButtonClicked(capturedIndex));
                    }
                }
            }

            if (m_WallLengthSlider != null)
            {
                m_WallLengthSlider.onValueChanged.AddListener(_ => OnWallScaleChanged());
            }
            if (m_WallHeightSlider != null)
            {
                m_WallHeightSlider.onValueChanged.AddListener(_ => OnWallScaleChanged());
            }

            if (m_OrthoViewButton  != null) m_OrthoViewButton .onClick.AddListener(() => SetCameraView(true));
            if (m_PerspViewButton  != null) m_PerspViewButton .onClick.AddListener(() => SetCameraView(false));
            if (m_EraserButton     != null) m_EraserButton    .onClick.AddListener(OnEraserClicked);
            if (m_ClearAllButton   != null) m_ClearAllButton  .onClick.AddListener(OnClearAllClicked);
            if (m_CloseButton      != null) m_CloseButton     .onClick.AddListener(Hide);
            if (m_OpenInventoryButton != null) m_OpenInventoryButton.onClick.AddListener(OnOpenInventoryClicked);
            if (m_BeginSessionButton  != null) m_BeginSessionButton.onClick.AddListener(OnBeginSessionClicked);

            SetWallScalePanelVisible(false);
        }

        private void OnOpenInventoryClicked()
        {
            if (m_InventoryPanelUI != null)
            {
                m_InventoryPanelUI.Show();
            }
        }

        private void OnBeginSessionClicked()
        {
            // 1. Hide the editor panel UI and clean tool selection
            Hide();

            // 2. Hide inventory panel if open
            if (m_InventoryPanelUI != null)
            {
                m_InventoryPanelUI.Hide();
            }

            // 3. Disable ExConGridEditor interactions (locking the camera and disabling ghost/placement controls)
            if (ExConGridEditor.Instance != null)
            {
                ExConGridEditor.Instance.enabled = false;
            }

            Debug.Log("[ExConEditorPanelUI] Begin Session Clicked. Lock-in editor mode complete!");
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Show()
        {
            if (m_Panel != null)
            {
                m_Panel.SetActive(true);
            }

            ClearToolSelection();
        }

        public void Hide()
        {
            if (m_Panel != null)
            {
                m_Panel.SetActive(false);
            }
            ClearToolSelection();
        }

        // ── Button Handlers ───────────────────────────────────────────────────

        private void OnPropButtonClicked(int index)
        {
            m_ActiveToolIndex = index;
            bool isWallType   = System.Array.IndexOf(k_WallTypeIndices, index) >= 0;

            if (ExConGridEditor.Instance != null)
            {
                ExConGridEditor.Instance.SelectTool(index);
            }

            UpdatePropButtonHighlights(index);
            SetWallScalePanelVisible(isWallType);

            if (isWallType)
            {
                if (m_WallLengthSlider != null) m_WallLengthSlider.value = 1f;
                if (m_WallHeightSlider != null) m_WallHeightSlider.value = 1f;
                OnWallScaleChanged();
            }
        }

        private void OnWallScaleChanged()
        {
            float lengthScale = m_WallLengthSlider != null ? m_WallLengthSlider.value : 1f;
            float heightScale = m_WallHeightSlider != null ? m_WallHeightSlider.value : 1f;

            if (ExConGridEditor.Instance != null)
            {
                ExConGridEditor.Instance.SetActiveScale(new Vector3(lengthScale, heightScale, 1f));
            }

            if (m_WallScaleValueText != null)
                m_WallScaleValueText.text = $"L: {lengthScale:F1}x  H: {heightScale:F1}x";
        }

        private void SetCameraView(bool ortho)
        {
            if (ExConGridEditor.Instance != null)
            {
                ExConGridEditor.Instance.SetCameraView(ortho);
            }
        }

        private void OnEraserClicked()
        {
            if (ExConGridEditor.Instance != null)
            {
                ExConGridEditor.Instance.ToggleEraser();
            }

            UpdatePropButtonHighlights(-1);
            SetWallScalePanelVisible(false);
            m_ActiveToolIndex = -1;
        }

        private void OnClearAllClicked()
        {
            if (GridMapManager.Instance != null && GridMapManager.Instance.IsServer)
            {
                GridMapManager.Instance.ClearAllGridObjectsServerRpc();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void ClearToolSelection()
        {
            m_ActiveToolIndex = -1;
            if (ExConGridEditor.Instance != null)
            {
                ExConGridEditor.Instance.SelectTool(-1);
            }
            UpdatePropButtonHighlights(-1);
            SetWallScalePanelVisible(false);
        }

        private void SetWallScalePanelVisible(bool visible)
        {
            if (m_WallScalePanel != null)
            {
                m_WallScalePanel.SetActive(visible);
            }
        }

        private void UpdatePropButtonHighlights(int selectedIndex)
        {
            if (m_PropButtonHighlights == null) return;
            for (int i = 0; i < m_PropButtonHighlights.Length; i++)
            {
                if (m_PropButtonHighlights[i] != null)
                    m_PropButtonHighlights[i].enabled = (i == selectedIndex);
            }
        }
    }
}
