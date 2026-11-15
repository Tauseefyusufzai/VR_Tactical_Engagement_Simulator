using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace XRMultiplayer
{
    /// <summary>
    /// Manages the Layout Selection screen during the ExCon operator setup onboarding.
    /// </summary>
    public class ExConLayoutSelectUI : MonoBehaviour
    {
        [Header("UI Panels")]
        [SerializeField] private GameObject m_Panel;
        [SerializeField] private ExConEditorPanelUI m_EditorPanelUI;

        [Header("Layout Buttons")]
        [SerializeField] private Button m_FreshButton;
        [SerializeField] private Button m_LayoutAButton;
        [SerializeField] private Button m_LayoutBButton;
        [SerializeField] private Button m_LayoutCButton;

        [Header("Preconfigured Layout Templates")]
        [SerializeField] private ExConLayoutTemplate m_LayoutATemplate;
        [SerializeField] private ExConLayoutTemplate m_LayoutBTemplate;
        [SerializeField] private ExConLayoutTemplate m_LayoutCTemplate;

        [Header("Layout Card Previews")]
        [SerializeField] private Image m_FreshPreviewImage;
        [SerializeField] private Sprite m_FreshPreviewSprite;
        [SerializeField] private Image m_LayoutAPreviewImage;
        [SerializeField] private Image m_LayoutBPreviewImage;
        [SerializeField] private Image m_LayoutCPreviewImage;

        private void Start()
        {
            if (m_FreshButton != null) m_FreshButton.onClick.AddListener(() => OnLayoutSelected(null));
            if (m_LayoutAButton != null) m_LayoutAButton.onClick.AddListener(() => OnLayoutSelected(m_LayoutATemplate));
            if (m_LayoutBButton != null) m_LayoutBButton.onClick.AddListener(() => OnLayoutSelected(m_LayoutBTemplate));
            if (m_LayoutCButton != null) m_LayoutCButton.onClick.AddListener(() => OnLayoutSelected(m_LayoutCTemplate));

            // Set visual card preview images
            if (m_FreshPreviewImage != null && m_FreshPreviewSprite != null)
                m_FreshPreviewImage.sprite = m_FreshPreviewSprite;

            if (m_LayoutAPreviewImage != null && m_LayoutATemplate != null)
                m_LayoutAPreviewImage.sprite = m_LayoutATemplate.LayoutPreviewSprite;

            if (m_LayoutBPreviewImage != null && m_LayoutBTemplate != null)
                m_LayoutBPreviewImage.sprite = m_LayoutBTemplate.LayoutPreviewSprite;

            if (m_LayoutCPreviewImage != null && m_LayoutCTemplate != null)
                m_LayoutCPreviewImage.sprite = m_LayoutCTemplate.LayoutPreviewSprite;
        }

        public void Show()
        {
            if (m_Panel != null)
            {
                m_Panel.SetActive(true);
            }
        }

        public void Hide()
        {
            if (m_Panel != null)
            {
                m_Panel.SetActive(false);
            }
        }

        private void OnLayoutSelected(ExConLayoutTemplate template)
        {
            if (GridMapManager.Instance != null)
            {
                // Clear any existing grid objects and spawn layout objects
                GridMapManager.Instance.LoadLayoutTemplateServerRpc(template != null ? template.name : string.Empty);
            }

            Hide();

            if (m_EditorPanelUI != null)
            {
                m_EditorPanelUI.Show();
            }
        }
    }
}
