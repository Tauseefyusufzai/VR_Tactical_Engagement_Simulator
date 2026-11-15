using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace XRMultiplayer
{
    /// <summary>
    /// Attaches a networked weapon to the player avatar's right hand.
    /// Manages proper weapon orientation, hides controller visuals when holding weapon,
    /// and handles XR Right Controller Trigger input and PC shooting input.
    /// </summary>
    [RequireComponent(typeof(XRINetworkPlayer))]
    public class NetworkPlayerWeaponHolder : NetworkBehaviour
    {
        [Header("Weapon Settings")]
        [SerializeField, Tooltip("Prefab of the weapon to instantiate on right hand. If unassigned, will load M1911 from Resources.")]
        public GameObject weaponPrefab;

        [SerializeField, Tooltip("Local position offset of weapon relative to right hand transform.")]
        public Vector3 handPositionOffset = new Vector3(0f, -0.03f, 0.05f);

        [SerializeField, Tooltip("Local rotation offset of weapon relative to right hand transform (Default 0, 180, 0 fixes opposite facing).")]
        public Vector3 handRotationOffset = new Vector3(0f, 180f, 0f);

        [Header("Input Configuration")]
        [SerializeField, Tooltip("Optional XR Input Action for shooting (Right Hand Trigger/Activate).")]
        public InputActionProperty fireInputAction;

        /// <summary>
        /// Reference to the equipped NetworkWeapon component.
        /// </summary>
        public NetworkWeapon equippedWeapon { get; private set; }

        /// <summary>
        /// Attached XRINetworkPlayer reference.
        /// </summary>
        private XRINetworkPlayer m_NetworkPlayer;

        /// <summary>
        /// Root container object holding the weapon model.
        /// </summary>
        private GameObject m_WeaponInstance;

        private bool m_WasTriggerPressedLastFrame = false;

        private void Awake()
        {
            m_NetworkPlayer = GetComponent<XRINetworkPlayer>();
        }

        private void OnEnable()
        {
            if (fireInputAction.action != null)
            {
                fireInputAction.action.Enable();
            }
        }

        private void OnDisable()
        {
            if (fireInputAction.action != null)
            {
                fireInputAction.action.Disable();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            SetupWeaponOnRightHand();
        }

        private void Start()
        {
            if (equippedWeapon == null)
            {
                SetupWeaponOnRightHand();
            }
        }

        /// <summary>
        /// Instantiates and attaches the weapon to the avatar's right hand transform.
        /// Hides hand/controller renderers and sets up correct weapon orientation.
        /// </summary>
        public void SetupWeaponOnRightHand()
        {
            if (equippedWeapon != null) return;

            Transform rightHandTarget = m_NetworkPlayer != null && m_NetworkPlayer.rightHand != null
                ? m_NetworkPlayer.rightHand
                : transform;

            // 1. Instantiate or load weapon prefab
            GameObject prefabToUse = weaponPrefab;
            if (prefabToUse == null)
            {
                prefabToUse = Resources.Load<GameObject>("Weapons/M1911") ?? Resources.Load<GameObject>("M1911");
            }

            if (prefabToUse != null)
            {
                m_WeaponInstance = Instantiate(prefabToUse, rightHandTarget);
                m_WeaponInstance.name = "RightHand_Weapon_M1911";
            }
            else
            {
                m_WeaponInstance = CreateProceduralPistol(rightHandTarget);
            }

            // 2. Position and rotate relative to right hand (180 deg Y rotation fixes opposite facing)
            m_WeaponInstance.transform.SetParent(rightHandTarget, false);
            m_WeaponInstance.transform.localPosition = handPositionOffset;
            m_WeaponInstance.transform.localRotation = Quaternion.Euler(handRotationOffset);

            // 3. Ensure NetworkWeapon component exists and barrel points forward
            equippedWeapon = m_WeaponInstance.GetComponent<NetworkWeapon>();
            if (equippedWeapon == null)
            {
                equippedWeapon = m_WeaponInstance.AddComponent<NetworkWeapon>();
            }

            // 4. Hide right hand/controller visual renderers so controller is not visible
            HideRightHandControllerVisuals();

            Debug.Log($"[NetworkPlayerWeaponHolder] Equipped weapon '{m_WeaponInstance.name}' on right hand. Controller hidden. OwnerId: {OwnerClientId}.");
        }

        private void LateUpdate()
        {
            // Continuously ensure controller renderers remain hidden and weapon remains visible
            if (equippedWeapon != null)
            {
                HideRightHandControllerVisuals();
            }
        }

        /// <summary>
        /// Disables renderers of the right hand controller / hand mesh so only the gun is visible.
        /// </summary>
        private void HideRightHandControllerVisuals()
        {
            Transform rightHandTarget = m_NetworkPlayer != null && m_NetworkPlayer.rightHand != null
                ? m_NetworkPlayer.rightHand
                : transform;

            Renderer[] renderers = rightHandTarget.GetComponentsInChildren<Renderer>(true);
            foreach (var rend in renderers)
            {
                if (m_WeaponInstance != null && rend.transform.IsChildOf(m_WeaponInstance.transform))
                {
                    // Weapon mesh renderer remains enabled and visible
                    rend.enabled = true;
                    rend.gameObject.layer = LayerMask.NameToLayer("Default");
                }
                else
                {
                    // Hide hand / controller visual meshes
                    rend.enabled = false;
                }
            }
        }

        private void Update()
        {
            // Only local player owner processes input to trigger shooting
            if (!IsOwner && IsSpawned) return;

            if (equippedWeapon == null) return;

            bool triggerPressed = ReadRightHandTriggerInput();

            // Detect button down or continuous hold
            if (triggerPressed && !m_WasTriggerPressedLastFrame)
            {
                equippedWeapon.TryShoot();
            }
            else if (triggerPressed)
            {
                // Automatic fire check
                equippedWeapon.TryShoot();
            }

            m_WasTriggerPressedLastFrame = triggerPressed;
        }

        /// <summary>
        /// Reads XR Right Hand Controller Trigger input using XR Device API, Input System, and Keyboard/Mouse.
        /// </summary>
        private bool ReadRightHandTriggerInput()
        {
            // 1. Check XR Node RightHand Trigger Device input (Meta Quest / XR Controller)
            UnityEngine.XR.InputDevice rightController = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (rightController.isValid)
            {
                if (rightController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool triggerButton) && triggerButton)
                {
                    return true;
                }
                if (rightController.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float triggerVal) && triggerVal > 0.5f)
                {
                    return true;
                }
            }

            // 2. Check Input Action Property if assigned
            if (fireInputAction.action != null)
            {
                if (fireInputAction.action.WasPressedThisFrame() || fireInputAction.action.ReadValue<float>() > 0.5f)
                {
                    return true;
                }
            }

            // 3. Fallback Mouse / Keyboard input for PC testing using Unity New Input System
            if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                return true;
            }
            if (Keyboard.current != null && (Keyboard.current.spaceKey.isPressed || Keyboard.current.fKey.isPressed))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates a clean procedural 3D Pistol model if prefab asset is not found.
        /// </summary>
        private GameObject CreateProceduralPistol(Transform parent)
        {
            GameObject gunRoot = new GameObject("RightHand_Weapon_ProceduralPistol");
            gunRoot.transform.SetParent(parent, false);

            Material gunMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            gunMat.color = new Color(0.15f, 0.15f, 0.18f);

            // Slide / Body
            GameObject slide = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slide.name = "Pistol_Slide";
            slide.transform.SetParent(gunRoot.transform, false);
            slide.transform.localScale = new Vector3(0.035f, 0.045f, 0.18f);
            slide.transform.localPosition = new Vector3(0f, 0.03f, 0.05f);
            slide.GetComponent<Renderer>().material = gunMat;
            Destroy(slide.GetComponent<Collider>());

            // Grip / Handle
            GameObject grip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            grip.name = "Pistol_Grip";
            grip.transform.SetParent(gunRoot.transform, false);
            grip.transform.localScale = new Vector3(0.03f, 0.1f, 0.04f);
            grip.transform.localPosition = new Vector3(0f, -0.03f, -0.01f);
            grip.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            grip.GetComponent<Renderer>().material = gunMat;
            Destroy(grip.GetComponent<Collider>());

            // Barrel
            GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = "Barrel";
            barrel.transform.SetParent(gunRoot.transform, false);
            barrel.transform.localScale = new Vector3(0.018f, 0.08f, 0.018f);
            barrel.transform.localPosition = new Vector3(0f, 0.035f, 0.12f);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            barrel.GetComponent<Renderer>().material = gunMat;
            Destroy(barrel.GetComponent<Collider>());

            return gunRoot;
        }
    }
}
