using UnityEngine;
using UnityEngine.UI;

namespace XRMultiplayer
{
    /// <summary>
    /// Manages the Inventory Panel where users can drag and drop items onto the room.
    /// </summary>
    public class ExConInventoryPanelUI : MonoBehaviour
    {
        [Header("Panel Root")]
        [SerializeField] private GameObject m_Panel;

        [Header("Close Button")]
        [SerializeField] private Button m_CloseButton;

        private void Start()
        {
            if (m_CloseButton != null)
            {
                m_CloseButton.onClick.AddListener(Hide);
            }
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
    }
}
