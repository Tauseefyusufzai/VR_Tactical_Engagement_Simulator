using UnityEngine;
using Unity.Netcode;

namespace XRMultiplayer
{
    /// <summary>
    /// Attached to a NetworkObject in the scene.
    /// When a VR Client (non-host) connects, it disables all PC-only GameObjects
    /// (e.g. the Map Editor UI, the PC top-down camera) so they don't block the VR view.
    /// </summary>
    public class RoleUIController : NetworkBehaviour
    {
        [Tooltip("GameObjects that should ONLY be active for the PC Host (e.g. Map Editor UI, Top-Down Camera).")]
        [SerializeField] private GameObject[] m_PCOnlyObjects;

        public override void OnNetworkSpawn()
        {
            // A VR Client is: connected as a Client but NOT the Host
            if (IsClient && !IsHost)
            {
                foreach (GameObject obj in m_PCOnlyObjects)
                {
                    if (obj != null)
                        obj.SetActive(false);
                }

                Debug.Log("[RoleUIController] VR Client detected — PC UI disabled.");
            }
            else if (IsHost)
            {
                Debug.Log("[RoleUIController] PC Host detected — keeping all PC UI active.");
            }
        }
    }
}
