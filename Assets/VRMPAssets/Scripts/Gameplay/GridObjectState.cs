using UnityEngine;
using Unity.Netcode;

namespace XRMultiplayer
{
    /// <summary>
    /// Attached to every spawned grid object prefab.
    /// Synchronises localScale across all clients via a NetworkVariable.
    /// Late-joining clients receive the correct scale automatically on spawn.
    /// </summary>
    public class GridObjectState : NetworkBehaviour
    {
        /// <summary>Networked scale applied to this object's transform.</summary>
        public readonly NetworkVariable<Vector3> Scale = new NetworkVariable<Vector3>(
            Vector3.one,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        /// <summary>Index of the prefab in the GridMapManager's placeable prefabs list.</summary>
        public readonly NetworkVariable<int> PrefabIndex = new NetworkVariable<int>(
            -1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // ── Netcode Lifecycle ─────────────────────────────────────────────────

        public override void OnNetworkSpawn()
        {
            // Subscribe AFTER the object is live on the network
            Scale.OnValueChanged += OnScaleChanged;

            // Apply immediately — covers late-joiners who missed the initial set
            transform.localScale = Scale.Value;
        }

        public override void OnNetworkDespawn()
        {
            // Always unsubscribe when the object is despawned
            Scale.OnValueChanged -= OnScaleChanged;
        }

        // ── Handler ───────────────────────────────────────────────────────────

        private void OnScaleChanged(Vector3 oldScale, Vector3 newScale)
        {
            transform.localScale = newScale;
        }
    }
}
