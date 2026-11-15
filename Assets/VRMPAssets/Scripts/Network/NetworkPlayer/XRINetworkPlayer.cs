using UnityEngine;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using Unity.Collections;
using System;
using System.Collections.Generic;
using Unity.Services.Vivox;
using Unity.XR.CoreUtils.Bindings.Variables;
using UnityEngine.XR.Templates.VRMultiplayer;

namespace XRMultiplayer
{
    /// <summary>
    /// XRINetworkPlayer class used for simple interactions.
    /// </summary>
    public class XRINetworkPlayer : NetworkBehaviour
    {
        /// <summary>
        /// Speed at which voice amplitude changes.
        /// </summary>
        const float k_VoiceAmplitudeSpeed = 15.0f;

        /// <summary>
        /// Singleton Reference for the Local Player.
        /// </summary>
        public static XRINetworkPlayer LocalPlayer;

        [Header("Avatar Transform References"), Tooltip("Assign to local avatar transform.")]
        /// <summary>
        /// Non-Local player transforms.
        /// </summary>
        public Transform head;

        /// <summary>
        /// Non-Local player transforms.
        /// </summary>
        public Transform leftHand;

        /// <summary>
        /// Non-Local player transforms.
        /// </summary>
        public Transform rightHand;

        /// <summary>
        /// Action called when the player name is updated.
        /// </summary>
        public Action<string> onNameUpdated;

        /// <summary>
        /// Action called when the player color is updated.
        /// </summary>
        public Action<Color> onColorUpdated;

        /// <summary>
        /// Action called when the Local Player is finished spawning in.
        /// </summary>
        public Action onSpawnedLocal;

        /// <summary>
        /// Action called when the Local Player is finished spawning in.
        /// </summary>
        public Action onSpawnedAll;

        /// <summary>
        /// Action called when the player color is updated.
        /// </summary>
        public Action<XRINetworkPlayer> onDisconnected;

        /// <summary>
        /// Bindable Variable used for other clients to mute this user locally.
        /// </summary>
        public BindableVariable<bool> squelched = new BindableVariable<bool>(false);

        /// <summary>
        /// Current Voice Amplitude driven from Vivox.
        /// </summary>
        public float playerVoiceAmp { get => m_VoiceAmplitudeCurrent; }
        float m_VoiceAmplitudeCurrent;

        /// <summary>
        /// Player Voice Id string that reads from the internal NetworkVariable for the Player Voice Id.
        /// </summary>
        public string playerVoiceId { get => m_PlayerVoiceId.Value.ToString(); }
        readonly NetworkVariable<FixedString128Bytes> m_PlayerVoiceId = new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>
        /// Player Name string that reads from the internal NetworkVariable for the Player Name.
        /// </summary>
        public string playerName { get => m_PlayerName.Value.ToString(); }
        readonly NetworkVariable<FixedString128Bytes> m_PlayerName = new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<int> m_ActiveMapIndexNetVar = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>
        /// Player Color that reads from the internal NetworkVariable for the Player Color.
        /// </summary>
        public Color playerColor { get => m_PlayerColor.Value; }
        readonly NetworkVariable<Color> m_PlayerColor = new(Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public NetworkVariable<int> platformType => m_PlatformType;
        readonly NetworkVariable<int> m_PlatformType = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        [HideInInspector] public readonly NetworkVariable<bool> selfMuted = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);


        /// <summary>
        /// Player Name Tag.
        /// </summary>
        [Header("Player Name Tag"), SerializeField, Tooltip("Player Name Tag.")] protected bool m_UpdateObjectName = true;


        // /// <summary>
        // /// Head Renderers to change rendering mode for local players.
        // /// </summary>
        // [SerializeField, Tooltip("Head Renderers to change rendering mode for local players.")] protected Renderer[] m_HeadRends;

        /// <summary>
        /// Hand Objects to be disabled for the local player.
        /// </summary>
        [Header("Networked Hands"), SerializeField, Tooltip("Hand Objects to be disabled for the local player.")] protected GameObject[] m_handsObjects;

        /// <summary>
        /// Player Name Tag.
        /// </summary>
        [Header("Player Name Tag"), SerializeField, Tooltip("Player Name Tag.")] protected PlayerNameTag m_PlayerNameTag;

        /// <summary>
        /// Internal references to the Local Player Transforms.
        /// </summary>
        protected Transform m_LeftHandOrigin, m_RightHandOrigin, m_HeadOrigin;

        /// <summary>
        /// Reference to the local player XR Origin
        /// </summary>
        protected XROrigin m_XROrigin;

        /// <summary>
        /// If the player has been connected to the the game.
        /// </summary>
        protected bool m_InitialConnected = false;

        /// <summary>
        /// Reference to the VoiceChatManager.
        /// </summary>
        protected VoiceChatManager m_VoiceChat;

        /// <summary>
        /// Reference to the VivoxParticipant.
        /// </summary>
        protected VivoxParticipant m_VivoxParticipant;

        /// <summary>
        /// Time to update the voice position.
        /// </summary>
        protected float m_VoicePositionUpdateTime = .1f, m_VoiceUpdatePosotionDelta = .05f;

        /// <summary>
        /// Destination for the voice amplitude.
        /// </summary>
        protected float m_VoiceAmplitudeDestination;

        /// <summary>
        /// Timer to check the voice position.
        /// </summary>
        protected float m_VoicePositionCheckTimer;

        /// <summary>
        /// Previous position of the head.
        /// </summary>
        protected Vector3 m_PrevHeadPos;

        protected void Awake()
        {
            m_VoiceChat = FindFirstObjectByType<VoiceChatManager>();
            m_VoicePositionCheckTimer = m_VoicePositionUpdateTime;

            if (!TryGetComponent<NetworkPlayerWeaponHolder>(out _))
            {
                gameObject.AddComponent<NetworkPlayerWeaponHolder>();
            }
        }

        ///<inheritdoc/>
        protected virtual void OnEnable()
        {
            m_PlayerName.OnValueChanged += UpdatePlayerName;
            m_PlayerColor.OnValueChanged += UpdatePlayerColor;
            m_ActiveMapIndexNetVar.OnValueChanged += OnActiveMapIndexNetVarChanged;
        }

        ///<inheritdoc/>
        protected virtual void OnDisable()
        {
            m_PlayerName.OnValueChanged -= UpdatePlayerName;
            m_PlayerColor.OnValueChanged -= UpdatePlayerColor;
            m_ActiveMapIndexNetVar.OnValueChanged -= OnActiveMapIndexNetVarChanged;
        }

        private void OnActiveMapIndexNetVarChanged(int prev, int current)
        {
            Debug.Log($"[XRINetworkPlayer-LOG] OnActiveMapIndexNetVarChanged: {prev} -> {current}");
            if (WarehouseNetworkSync.Instance != null)
            {
                WarehouseNetworkSync.Instance.ApplyStateLocally(current);
            }
        }

        ///<inheritdoc/>
        protected virtual void Update()
        {
            if (IsOwner && XRINetworkGameManager.Instance.positionalVoiceChat)
            {
                if (Time.time > m_VoicePositionCheckTimer)
                {
                    m_VoicePositionCheckTimer += m_VoicePositionUpdateTime;

                    if (m_HeadOrigin != null && Vector3.Distance(m_PrevHeadPos, m_HeadOrigin.position) > m_VoiceUpdatePosotionDelta)
                    {
                        m_PrevHeadPos = m_HeadOrigin.position;
                        if (XRINetworkGameManager.Instance.positionalVoiceChat && m_VoiceChat != null)
                        {
                            m_VoiceChat.Set3DAudio(m_HeadOrigin);
                        }
                    }
                }
            }

            m_VoiceAmplitudeCurrent = Mathf.Lerp(m_VoiceAmplitudeCurrent, m_VoiceAmplitudeDestination, Time.deltaTime * k_VoiceAmplitudeSpeed);
        }

        ///<inheritdoc/>
        protected virtual void LateUpdate()
        {
            if (!IsOwner) return;

            if (m_HeadOrigin != null)
                head.SetPositionAndRotation(m_HeadOrigin.position, m_HeadOrigin.rotation);

            if (m_LeftHandOrigin != null)
                leftHand.SetPositionAndRotation(m_LeftHandOrigin.position, m_LeftHandOrigin.rotation);

            if (m_RightHandOrigin != null)
                rightHand.SetPositionAndRotation(m_RightHandOrigin.position, m_RightHandOrigin.rotation);
        }

        ///<inheritdoc/>
        public override void OnDestroy()
        {
            base.OnDestroy();

            if (IsOwner)
            {
                // Local Name unsubscribe.
                XRINetworkGameManager.LocalPlayerName.Unsubscribe(UpdateLocalPlayerName);
                XRINetworkGameManager.LocalPlayerColor.Unsubscribe(UpdateLocalPlayerColor);
                if (m_VoiceChat != null)
                    m_VoiceChat.selfMuted.Unsubscribe(SelfMutedChanged);
            }
            else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
            {
                // Inform Network Manager that player left current session.
                XRINetworkGameManager.Instance.PlayerLeft(NetworkObject.OwnerClientId);
            }

            // Unsubscribe from color updating.
            m_PlayerColor.OnValueChanged -= UpdatePlayerColor;
        }

        ///<inheritdoc/>
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsLocalPlayer)
            {
                // Set Local Player.
                LocalPlayer = this;
                XRINetworkGameManager.Instance.OnLocalClientStarted(NetworkObject.OwnerClientId);

                // Setup Platform Type
                m_PlatformType.Value = (int)XRPlatformUnderstanding.CurrentPlatform;
                Debug.Log($"XRINetworkPlayer: Platform type set to {m_PlatformType.Value}");

                // Get Origin and set head.
                m_XROrigin = FindFirstObjectByType<XROrigin>();
                if (m_XROrigin != null)
                {
                    m_HeadOrigin = m_XROrigin.Camera.transform;
                }
                else
                {
                    Utils.Log("No XR Rig Available", 1);
                }

                SetupLocalPlayer();
                TeleportToSpawnAreaTag();
            }
            CompleteSetup();

            if (IsServer)
            {
                int activeMap = WarehouseNetworkSync.ActiveMapIndex;
                m_ActiveMapIndexNetVar.Value = activeMap;
                Debug.Log($"[XRINetworkPlayer-LOG] OnNetworkSpawn on Server/Host. Set m_ActiveMapIndexNetVar={activeMap} for player {OwnerClientId}");
            }

            if (WarehouseNetworkSync.Instance != null)
            {
                WarehouseNetworkSync.Instance.ApplyStateLocally(m_ActiveMapIndexNetVar.Value);
            }

            TeleportToSpawnAreaTag();
        }

        public static void BroadcastMapState(int mapIndex)
        {
            Debug.Log($"[XRINetworkPlayer-LOG] BroadcastMapState({mapIndex}) called.");
            foreach (var player in FindObjectsByType<XRINetworkPlayer>(FindObjectsSortMode.None))
            {
                if (player != null && player.IsSpawned && player.IsServer)
                {
                    player.m_ActiveMapIndexNetVar.Value = mapIndex;
                }
            }
        }

        public static void BroadcastWarehouseState(bool active)
        {
            BroadcastMapState(active ? 1 : 0);
        }

        public static bool TeleportToSpawnAreaTag()
        {
            GameObject spawnObj = null;

            // Collect all tagged objects
            List<GameObject> taggedList = new List<GameObject>();
            try { taggedList.AddRange(GameObject.FindGameObjectsWithTag("SpwanArea")); } catch {}
            try { taggedList.AddRange(GameObject.FindGameObjectsWithTag("SpawnArea")); } catch {}

            // Determine active map container if available
            GameObject activeMapContainer = null;
            if (WarehouseNetworkSync.Instance != null)
            {
                if (WarehouseNetworkSync.ActiveMapIndex == 1)
                    activeMapContainer = WarehouseNetworkSync.Instance.WarehouseObject;
                else if (WarehouseNetworkSync.ActiveMapIndex == 2)
                    activeMapContainer = WarehouseNetworkSync.Instance.TerrainMapObject;
            }

            // 1. First search for active tagged object inside the active map container
            if (activeMapContainer != null)
            {
                foreach (var obj in taggedList)
                {
                    if (obj != null && obj.activeInHierarchy && obj.transform.IsChildOf(activeMapContainer.transform))
                    {
                        spawnObj = obj;
                        break;
                    }
                }
            }

            // 2. Fallback to any active tagged object in the scene hierarchy
            if (spawnObj == null)
            {
                foreach (var obj in taggedList)
                {
                    if (obj != null && obj.activeInHierarchy)
                    {
                        spawnObj = obj;
                        break;
                    }
                }
            }

            if (spawnObj != null && spawnObj.activeInHierarchy)
            {
                Vector3 spawnPos = spawnObj.transform.position;
                Quaternion spawnRot = spawnObj.transform.rotation;

                // Try to get exact center from Collider (self or children) first
                Collider col = spawnObj.GetComponent<Collider>() ?? spawnObj.GetComponentInChildren<Collider>();
                if (col != null)
                {
                    Bounds b = col.bounds;
                    spawnPos = new Vector3(b.center.x, b.max.y + 0.05f, b.center.z);
                }
                else
                {
                    // Fallback to Renderer (self or children)
                    Renderer rend = spawnObj.GetComponent<Renderer>() ?? spawnObj.GetComponentInChildren<Renderer>();
                    if (rend != null)
                    {
                        Bounds b = rend.bounds;
                        spawnPos = new Vector3(b.center.x, b.max.y + 0.05f, b.center.z);
                    }
                }

                Debug.Log($"[SpawnArea-LOG] Teleporting player to center of tagged Spawn Area '{spawnObj.name}' at {spawnPos}");

                XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();
                if (xrOrigin != null)
                {
                    if (!xrOrigin.MoveCameraToWorldLocation(spawnPos))
                    {
                        xrOrigin.transform.SetPositionAndRotation(spawnPos, spawnRot);
                    }
                }

                if (LocalPlayer != null)
                {
                    LocalPlayer.transform.SetPositionAndRotation(spawnPos, spawnRot);
                }

                CharacterResetter resetter = FindFirstObjectByType<CharacterResetter>();
                if (resetter != null)
                {
                    resetter.SetResetPosition(spawnPos);
                }

                return true;
            }

            return false;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            if (PlayerHudNotification.Instance != null)
                PlayerHudNotification.Instance.ShowText($"<b>{m_PlayerName.Value}</b> left");
            onDisconnected?.Invoke(this);
        }

        /// <summary>
        /// Called from <see cref="XRHandPoseReplicator"/> when swapping between hand tracking and controllers.
        /// </summary>
        /// <param name="left">Transform for Left Hand.</param>
        /// <param name="right">Transform for Right Hand.</param>
        public void SetHandOrigins(Transform left, Transform right)
        {
            m_LeftHandOrigin = left;
            m_RightHandOrigin = right;
        }

        /// <summary>
        /// Hides and disables Renderers and GameObjects on the Local Player.
        /// Also sets the initial values for <see cref="m_PlayerColor"/> and <see cref="m_PlayerName"/>.
        /// Finally we subscribe to any updates for Color and Name.
        /// </summary>
        /// <remarks>Only called on the Local Player.</remarks>
        protected virtual void SetupLocalPlayer()
        {
            foreach (var hand in m_handsObjects)
            {
                hand.SetActive(false);
            }

            m_PlayerColor.Value = XRINetworkGameManager.LocalPlayerColor.Value;
            m_PlayerName.Value = new FixedString128Bytes(XRINetworkGameManager.LocalPlayerName.Value);
            XRINetworkGameManager.LocalPlayerColor.Subscribe(UpdateLocalPlayerColor);
            XRINetworkGameManager.LocalPlayerName.Subscribe(UpdateLocalPlayerName);
            if (m_VoiceChat != null)
            {
                m_VoiceChat.selfMuted.Subscribe(SelfMutedChanged);
                m_VoiceChat.ToggleSelfMute(true, true);
            }

            onSpawnedLocal?.Invoke();
        }

        /// <summary>
        /// Called from the local player only
        /// </summary>
        /// <param name="muted"></param>
        void SelfMutedChanged(bool muted)
        {
            selfMuted.Value = muted;
        }

        /// <summary>
        /// Callback for the bindable variable <see cref="XRINetworkGameManager.LocalPlayerColor"/>.
        /// </summary>
        /// <param name="color">New Color for player.</param>
        /// <remarks>Only called on Local Player.</remarks>
        protected virtual void UpdateLocalPlayerColor(Color color)
        {
            m_PlayerColor.Value = XRINetworkGameManager.LocalPlayerColor.Value;
        }

        /// <summary>
        /// Callback for the bindable variable <see cref="XRINetworkGameManager.LocalPlayerName"/>.
        /// </summary>
        /// <param name="name">New Name for player.</param>
        /// <remarks>Only called on Local Player.</remarks>
        protected virtual void UpdateLocalPlayerName(string name)
        {
            m_PlayerName.Value = new FixedString128Bytes(XRINetworkGameManager.LocalPlayerName.Value);
        }

        /// <summary>
        /// Called when the player object is finished being setup.
        /// </summary>
        void CompleteSetup()
        {
            // Add player to XRINetworkManager.
            XRINetworkGameManager.Instance.PlayerJoined(NetworkObject.OwnerClientId);

            // Update Color and Name.
            UpdatePlayerColor(Color.white, m_PlayerColor.Value);
            UpdatePlayerName(new FixedString128Bytes(""), m_PlayerName.Value);

            // Check if WorldCanvas exists
            WorldCanvas worldCanvas = FindFirstObjectByType<WorldCanvas>();
            if (worldCanvas != null)
            {
                // If we are using a World Canvas, reparent name tag and destroy local canvas.
                Canvas localCanvas = m_PlayerNameTag.GetComponentInParent<Canvas>();
                worldCanvas.SetupPlayerNameTag(this, m_PlayerNameTag);
                Destroy(localCanvas.gameObject);
            }
            else
            {
                // If we are not using a World Canvas, setup the name tag for local use.
                m_PlayerNameTag.SetupNameTag(this);
            }

            onSpawnedAll?.Invoke();
        }
        /// <summary>
        /// Callback anytime the local player sets <see cref="m_PlayerName"/>.
        /// </summary><remarks>Invokes the callback <see cref="onNameUpdated"/>.</remarks>
        void UpdatePlayerName(FixedString128Bytes oldName, FixedString128Bytes currentName)
        {
            onNameUpdated?.Invoke(currentName.ToString());

            if (!m_InitialConnected & !string.IsNullOrEmpty(currentName.ToString()))
            {
                m_InitialConnected = true;
                if (!IsLocalPlayer && PlayerHudNotification.Instance != null)
                    PlayerHudNotification.Instance.ShowText($"<b>{playerName}</b> joined");
            }

            if (m_UpdateObjectName)
                gameObject.name = currentName.ToString();
        }

        /// <summary>
        /// Callback when the local player sets <see cref="m_PlayerColor"/>.
        /// </summary><remarks>Invokes the callback <see cref="onColorUpdated"/>.</remarks>
        void UpdatePlayerColor(Color oldColor, Color newColor)
        {
            onColorUpdated?.Invoke(newColor);
        }

        void UpdatePlayerVoiceEnergy(float current)
        {
            m_VoiceAmplitudeDestination = Mathf.Clamp01(current);
        }

        /// <summary>
        /// Called when new players connect to the game and set their initial <see cref="m_PlayerVoiceId"/>
        /// and when <see cref="VoiceChatManager.OnParticipantAdded(VivoxParticipant)"/> is called for existing players.
        /// </summary>
        public void SetupVoicePlayer()
        {
            m_VivoxParticipant = m_VoiceChat.GetVivoxParticipantById(playerVoiceId);
            if (m_VivoxParticipant != null)
            {
                m_VivoxParticipant.ParticipantAudioEnergyChanged += ParticipantAudioEnergyChanged;
            }
            else
            {
                Utils.Log($"No Participant with id: {playerVoiceId}", 1);
            }

            if (!VoiceChatManager.m_PlayersDictionary.ContainsKey(playerVoiceId))
            {
                VoiceChatManager.AddNewVivoxPlayer(playerVoiceId, this);
            }
        }
        private void ParticipantAudioEnergyChanged()
        {
            UpdatePlayerVoiceEnergy((float)m_VivoxParticipant.AudioEnergy);
        }

        public void SetVoiceId(string voiceId)
        {
            if (!IsOwner) return;
            m_PlayerVoiceId.Value = new FixedString128Bytes(voiceId);
            SetupVoicePlayer();
            if (XRINetworkGameManager.Instance.positionalVoiceChat)
            {
                m_VoiceChat.Set3DAudio(m_HeadOrigin);
            }
        }

        /// <summary>
        /// Called from clients to mute this player locally for that client.
        /// </summary>
        public void ToggleSquelch()
        {
            if (m_VivoxParticipant != null)
            {
                squelched.Value = !squelched.Value;
                if (squelched.Value)
                    m_VivoxParticipant.MutePlayerLocally();
                else
                    m_VivoxParticipant.UnmutePlayerLocally();
            }
        }
    }
}
