using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace XRMultiplayer
{
    public class OfflineMenu : MonoBehaviour
    {
        /// <summary>
        /// Colors to choose from for the player.
        /// </summary>
        [SerializeField, Tooltip("Default name for the player")] Color[] m_PlayerColors;

        [Header("Player Info")]
        /// <summary>
        /// Default name for the player.
        /// </summary>
        [SerializeField, Tooltip("Default name for the player")] string m_DefaultPlayerName = "Unity Creator";
        [SerializeField] TMP_Text m_PlayerNameText;
        [SerializeField] TMP_Text m_PlayerInitialText;
        [SerializeField] Image[] m_PlayerColorIcons;
        [SerializeField] Image m_VolumeIndicator;
        [SerializeField] Image m_MicIcon;
        [SerializeField] Sprite m_MutedSprite;
        [SerializeField] Sprite m_UnmutedSprite;

        [Header("Panel Objects")]
        [SerializeField] GameObject m_CustomizationPanel;
        [SerializeField] GameObject m_ConnectionPanel;

        VoiceChatManager m_VoiceChatManager;

        private void Awake()
        {
            XRINetworkGameManager.Connected.Subscribe(OnConnected);
            XRINetworkGameManager.LocalPlayerName.Subscribe(SetPlayerName);
            XRINetworkGameManager.LocalPlayerColor.Subscribe(SetPlayerColor);

            OfflinePlayerAvatar.voiceAmp.Subscribe(UpdateMicIcon);

            m_VoiceChatManager = FindFirstObjectByType<VoiceChatManager>();
            if (m_VoiceChatManager != null)
                m_VoiceChatManager.selfMuted.Subscribe(MutedChanged);
            else
                Debug.LogWarning("[VR-DEBUG] VoiceChatManager not found in scene — voice mute icon will be inactive.");
            SetupPlayerDefaults();
        }

        private void Start()
        {
            Debug.Log("[VR-DEBUG] OfflineMenu.Start() called. GameObject active: " + gameObject.activeInHierarchy);
            Debug.Log("[VR-DEBUG] CustomizationPanel active: " + m_CustomizationPanel.activeInHierarchy + " | ConnectionPanel active: " + m_ConnectionPanel.activeInHierarchy);
            ShowCustomization();
            XRINetworkGameManager.Instance.OnConnectionFailedAction += ConnectionFailed;
        }

#if UNITY_EDITOR
        private void Update()
        {
            if (Keyboard.current == null) return;
            // Press J = Join Local (as client)
            if (Keyboard.current.jKey.wasPressedThisFrame)
            {
                Debug.Log("[VR-DEBUG] Editor shortcut J: calling JoinLocalConnection()");
                XRINetworkGameManager.Instance.JoinLocalConnection();
            }
            // Press H = Host Local
            if (Keyboard.current.hKey.wasPressedThisFrame)
            {
                Debug.Log("[VR-DEBUG] Editor shortcut H: calling HostLocalConnection()");
                XRINetworkGameManager.Instance.HostLocalConnection();
            }
        }
#endif

        private void OnDestroy()
        {
            XRINetworkGameManager.Connected.Unsubscribe(OnConnected);
            XRINetworkGameManager.LocalPlayerName.Unsubscribe(SetPlayerName);
            XRINetworkGameManager.LocalPlayerColor.Unsubscribe(SetPlayerColor);
            OfflinePlayerAvatar.voiceAmp.Unsubscribe(UpdateMicIcon);
            if (m_VoiceChatManager != null)
                m_VoiceChatManager.selfMuted.Subscribe(MutedChanged);

            XRINetworkGameManager.Instance.OnConnectionFailedAction -= ConnectionFailed;
        }

        void SetupPlayerDefaults()
        {
            XRINetworkGameManager.LocalPlayerName.Value = m_DefaultPlayerName;
            XRINetworkGameManager.LocalPlayerColor.Value = m_PlayerColors[Random.Range(0, m_PlayerColors.Length)];
        }

        void SetPlayerName(string name)
        {
            if (name == string.Empty)
            {
                SetupPlayerDefaults();
                return;
            }

            m_PlayerNameText.text = name;
            m_PlayerInitialText.text = name.Substring(0, 1);
            m_PlayerNameText.rectTransform.sizeDelta = new Vector2(m_PlayerNameText.preferredWidth * .25f, m_PlayerNameText.rectTransform.sizeDelta.y);
        }

        void SetPlayerColor(Color color)
        {
            foreach (var c in m_PlayerColorIcons)
            {
                c.color = color;
            }
        }

        void UpdateMicIcon(float amp)
        {
            m_VolumeIndicator.fillAmount = amp;
        }

        void ShowCustomization()
        {
            Debug.Log("[VR-DEBUG] ShowCustomization() — showing avatar name/color picker panel first.");
            m_CustomizationPanel.SetActive(true);
            m_ConnectionPanel.SetActive(false);
        }

        public void CompleteCustomization()
        {
            Debug.Log("[VR-DEBUG] CompleteCustomization() — Confirm clicked, switching to IP connection panel.");
            m_CustomizationPanel.SetActive(false);
            m_ConnectionPanel.SetActive(true);
        }

        void OnConnected(bool connected)
        {
            Debug.Log("[VR-DEBUG] OnConnected called: " + connected);
            if (connected)
            {
                Debug.Log("[VR-DEBUG] Connected! Hiding customization panel.");
                m_CustomizationPanel.SetActive(false);
            }
            else
            {
                Debug.Log("[VR-DEBUG] Disconnected — showing offline menu again.");
                gameObject.SetActive(true);
                ShowCustomization();
            }
        }

        void MutedChanged(bool muted)
        {
            m_MicIcon.sprite = muted ? m_MutedSprite : m_UnmutedSprite;
        }

        void ConnectionFailed(string reason)
        {
            CompleteCustomization();
        }
    }
}
