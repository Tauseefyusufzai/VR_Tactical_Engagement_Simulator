using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace XRMultiplayer
{
    public class CharacterResetter : MonoBehaviour
    {
        [SerializeField] Vector2 m_MinMaxHeight = new Vector2(-15.0f, 100.0f);
        [SerializeField] float m_ResetDistance = 500.0f;
        [SerializeField] Vector3 offlinePosition = new Vector3(0, .5f, -12.0f);
        [SerializeField] Vector3 onlinePosition = new Vector3(0, .15f, 0);
        TeleportationProvider m_TeleportationProvider;
        Vector3 m_ResetPosition;

        private void Start()
        {
            XRINetworkGameManager.Connected.Subscribe(UpdateResetPosition);
            m_TeleportationProvider = GetComponentInChildren<TeleportationProvider>();

            m_ResetPosition = offlinePosition;
            ResetPlayer();
        }

        public void SetResetPosition(Vector3 newPos)
        {
            m_ResetPosition = newPos;
            Debug.Log($"[CharacterResetter-LOG] Reset position set to {newPos}");
        }

        void UpdateResetPosition(bool connected)
        {
            if (connected)
            {
                m_ResetPosition = onlinePosition;
            }
            else
            {
                m_ResetPosition = offlinePosition;
                ResetPlayer();
            }
        }

        // Update is called once per frame
        void Update()
        {
            // Safety check: only reset if player falls deep below the map (e.g. y < -100)
            if (transform.position.y < -100.0f)
            {
                Debug.Log($"[CharacterResetter-LOG] Resetting player: Y position ({transform.position.y}) fell below -100");
                ResetPlayer();
            }
        }

        public void ResetPlayer()
        {
            ResetPlayer(m_ResetPosition);
        }

        void ResetPlayer(Vector3 destination)
        {
            if (m_TeleportationProvider == null)
            {
                // Try finding it globally if it wasn't found in children
                m_TeleportationProvider = FindFirstObjectByType<TeleportationProvider>();
            }

            if (m_TeleportationProvider == null)
            {
                // If it's STILL null, we can't teleport. We will just hard-set the transform position as a fallback.
                transform.position = destination;
                return;
            }

            TeleportRequest teleportRequest = new()
            {
                destinationPosition = destination,
                destinationRotation = Quaternion.identity
            };

            if (!m_TeleportationProvider.QueueTeleportRequest(teleportRequest))
            {
                Utils.LogWarning("Failed to queue teleport request");
            }
        }

        [ContextMenu("Set Player To Online Position")]
        void SetPlayerToOnlinePosition()
        {
            ResetPlayer(onlinePosition);
        }

        [ContextMenu("Set Player To Offline Position")]
        void SetPlayerToOfflinePosition()
        {
            ResetPlayer(offlinePosition);
        }
    }
}
