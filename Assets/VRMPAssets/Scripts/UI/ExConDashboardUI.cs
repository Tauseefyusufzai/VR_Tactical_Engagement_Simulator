using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace XRMultiplayer
{
    /// <summary>
    /// Top-level orchestrator for the PC ExCon UI flow.
    ///
    /// Flow:
    ///   Homepage (m_ConnectionPanel)
    ///     └─► User clicks Host
    ///           └─► Dashboard bar (m_DashboardPanel) appears
    ///                 └─► Room Config panel shown immediately (via ExConRoomConfigUI)
    ///                       └─► User confirms room → Editor button becomes interactable
    ///                             └─► User clicks Editor → ExConEditorPanelUI shown/hidden
    ///
    /// This script knows about the other UI scripts but does not perform their jobs.
    /// </summary>
    public class ExConDashboardUI : MonoBehaviour
    {
        // ── Homepage ──────────────────────────────────────────────────────────

        [Header("Homepage — Connection Panel")]
        [SerializeField] private GameObject     m_ConnectionPanel;
        [SerializeField] private TMP_InputField m_RoomNameInput;
        [SerializeField] private TMP_Text       m_LocalIpText;
        [SerializeField] private Button         m_HostButton;

        // ── Dashboard Bar ─────────────────────────────────────────────────────

        [Header("Dashboard Bar (visible after host)")]
        [SerializeField] private GameObject m_DashboardPanel;
        [SerializeField] private TMP_Text   m_DashboardRoomNameText;
        [SerializeField] private TMP_Text   m_DashboardRoomCodeText;
        [SerializeField] private Button     m_DisconnectButton;

        [Tooltip("Button that opens/closes the Editor panel. Becomes interactable once room config is confirmed.")]
        [SerializeField] private Button m_EditorToggleButton;

        // ── Child UI Controllers ──────────────────────────────────────────────

        [Header("Child UI Controllers")]
        [Tooltip("Attach the GameObject that has ExConRoomConfigUI on it.")]
        [SerializeField] private ExConRoomConfigUI m_RoomConfigUI;

        [Tooltip("Attach the GameObject that has ExConEditorPanelUI on it.")]
        [SerializeField] private ExConEditorPanelUI m_EditorPanelUI;

        // ── Internal state ────────────────────────────────────────────────────

        private bool m_RoomConfigured  = false;
        private bool m_EditorPanelOpen = false;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            if (m_HostButton != null)
            {
                m_HostButton.onClick.AddListener(HostLocalRoom);
            }

            if (m_DisconnectButton != null)
            {
                m_DisconnectButton.onClick.AddListener(DisconnectLocalRoom);
            }

        }

        private void Start()
        {
            if (XRINetworkGameManager.Instance != null)
            {
                string ip = XRINetworkGameManager.Instance.GetLocalIPAddress();
                if (m_LocalIpText != null)
                    m_LocalIpText.text = $"Host LAN IP: <b>{ip}</b>";
            }

            if (m_RoomNameInput != null)
                m_RoomNameInput.text = "ExCon Session";

            if (m_RoomConfigUI != null)
            {
                m_RoomConfigUI.OnRoomConfigConfirmed += OnRoomConfigConfirmed;
            }

            ShowHomepage();
        }

        private void OnEnable()
        {
            XRINetworkGameManager.Connected.Subscribe(OnNetworkStateChanged);
            XRINetworkGameManager.ConnectedRoomName.Subscribe(UpdateRoomName);
        }

        private void OnDisable()
        {
            XRINetworkGameManager.Connected.Unsubscribe(OnNetworkStateChanged);
            XRINetworkGameManager.ConnectedRoomName.Unsubscribe(UpdateRoomName);
        }

        private void UpdateRoomName(string roomName)
        {
            if (m_DashboardRoomNameText != null)
            {
                m_DashboardRoomNameText.text = $"Room: <b>{roomName}</b>";
            }
        }

        private void OnDestroy()
        {
            if (m_RoomConfigUI != null)
                m_RoomConfigUI.OnRoomConfigConfirmed -= OnRoomConfigConfirmed;
        }

        // ── Public Actions ────────────────────────────────────────────────────

        public void HostLocalRoom()
        {
            if (XRINetworkGameManager.Instance == null)
            {
                return;
            }

            string roomName = (m_RoomNameInput != null && !string.IsNullOrEmpty(m_RoomNameInput.text))
                ? m_RoomNameInput.text.Trim()
                : "ExCon Session";

            bool success = XRINetworkGameManager.Instance.HostLocalConnection();

            if (success)
            {
                // Set the custom room name AFTER HostLocalConnection() runs,
                // otherwise it gets overridden by the default "Local Room" inside that call.
                XRINetworkGameManager.ConnectedRoomName.Value = roomName;
            }
        }

        public void DisconnectLocalRoom()
        {
            if (XRINetworkGameManager.Instance != null)
            {
                XRINetworkGameManager.Instance.LeaveLocalConnection();
            }
        }

        // ── Network Events ────────────────────────────────────────────────────

        private void OnNetworkStateChanged(bool isConnected)
        {
            if (isConnected) OnConnected();
            else             OnDisconnected();
        }

        private void OnConnected()
        {
            if (m_ConnectionPanel != null) m_ConnectionPanel.SetActive(false);
            if (m_DashboardPanel  != null) m_DashboardPanel .SetActive(true);

            string roomName = XRINetworkGameManager.ConnectedRoomName.Value;
            string roomCode = XRINetworkGameManager.ConnectedRoomCode;

            UpdateRoomName(roomName);

            if (m_DashboardRoomCodeText != null)
                m_DashboardRoomCodeText.text = $"IP: <b>{roomCode}</b>";

            m_RoomConfigured  = false;
            m_EditorPanelOpen = true;

            if (m_RoomConfigUI != null)
            {
                m_RoomConfigUI.Show();
            }

            if (m_EditorPanelUI != null)
            {
                m_EditorPanelUI.Show();
            }
        }

        private void OnDisconnected()
        {
            m_RoomConfigured  = false;
            m_EditorPanelOpen = false;

            if (m_RoomConfigUI  != null) m_RoomConfigUI .Hide();
            if (m_EditorPanelUI != null) m_EditorPanelUI.Hide();

            ShowHomepage();
        }

        // ── Room Config Callback ──────────────────────────────────────────────

        private void OnRoomConfigConfirmed()
        {
            m_RoomConfigured = true;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void ShowHomepage()
        {
            if (m_ConnectionPanel != null) m_ConnectionPanel.SetActive(true);
            if (m_DashboardPanel  != null) m_DashboardPanel .SetActive(false);
        }
    }
}
