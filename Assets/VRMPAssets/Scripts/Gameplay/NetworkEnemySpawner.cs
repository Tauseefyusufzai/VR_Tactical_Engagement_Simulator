using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace XRMultiplayer
{
    /// <summary>
    /// Spawns networked AI enemies on the Host/Server.
    /// Can automatically spawn enemies on Host start, on a timer, or manually via trigger/button.
    /// </summary>
    public class NetworkEnemySpawner : NetworkBehaviour
    {
        [Header("Enemy Spawner Settings")]
        [SerializeField, Tooltip("Networked enemy prefab to spawn.")]
        public GameObject enemyPrefab;

        [SerializeField, Tooltip("Number of enemies to spawn when Host starts.")]
        public int initialSpawnCount = 1;

        [SerializeField, Tooltip("Spawn point transforms. If empty, spawns near this GameObject's position.")]
        public Transform[] spawnPoints;

        [SerializeField, Tooltip("Auto-respawn enemy after death?")]
        public bool autoRespawn = true;

        [SerializeField, Tooltip("Delay in seconds before respawning a dead enemy.")]
        public float respawnDelay = 5.0f;

        [SerializeField, Tooltip("Random radius around spawn point for position variation.")]
        public float spawnRadius = 3.0f;

        private List<GameObject> m_ActiveEnemies = new List<GameObject>();

        private void Awake()
        {
            // Auto-load Survivalist enemy prefab if not assigned in Inspector
            if (enemyPrefab == null)
            {
#if UNITY_EDITOR
                enemyPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VRMPAssets/Prefabs/NetworkEmeraldSurvivalistEnemy.prefab")
                           ?? UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VRMPAssets/Resources/NetworkEmeraldSurvivalistEnemy.prefab")
                           ?? UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Survivalist/Prefab/NetworkEmeraldSurvivalistEnemy.prefab");
#endif
                if (enemyPrefab == null)
                {
                    enemyPrefab = Resources.Load<GameObject>("NetworkEmeraldSurvivalistEnemy")
                               ?? Resources.Load<GameObject>("NetworkSurvivalistEnemy");
                }
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return; // Only the Host/Server spawns enemies

            for (int i = 0; i < initialSpawnCount; i++)
            {
                SpawnSingleEnemy();
            }
        }

        /// <summary>
        /// Instantiates and network-spawns a single AI enemy.
        /// </summary>
        public GameObject SpawnSingleEnemy(Vector3? overridePosition = null)
        {
            if (!IsServer)
            {
                Debug.LogWarning("[NetworkEnemySpawner] Only the Host/Server can spawn enemies!");
                return null;
            }

            if (enemyPrefab == null)
            {
                Debug.LogError("[NetworkEnemySpawner] Enemy prefab is missing! Please assign NetworkSurvivalistEnemy prefab.");
                return null;
            }

            // Ensure prefab is registered in NetworkManager
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null)
            {
                bool isRegistered = false;
                foreach (var p in NetworkManager.Singleton.NetworkConfig.Prefabs.Prefabs)
                {
                    if (p.Prefab == enemyPrefab || (p.Prefab != null && p.Prefab.name == enemyPrefab.name))
                    {
                        isRegistered = true;
                        break;
                    }
                }
                if (!isRegistered)
                {
                    Debug.Log($"[NetworkEnemySpawner] Registering '{enemyPrefab.name}' in NetworkManager at runtime...");
                    NetworkManager.Singleton.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = enemyPrefab });
                }
            }

            Vector3 spawnPos = GetNextSpawnPosition();
            if (overridePosition.HasValue)
            {
                spawnPos = overridePosition.Value;
            }

            GameObject enemyInstance = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
            NetworkObject netObj = enemyInstance.GetComponent<NetworkObject>();

            if (netObj == null)
            {
                Debug.LogError($"[NetworkEnemySpawner] Enemy prefab '{enemyPrefab.name}' is missing a NetworkObject component!");
                Destroy(enemyInstance);
                return null;
            }

            netObj.Spawn();
            m_ActiveEnemies.Add(enemyInstance);

            // Hook death for auto-respawn
            var health = enemyInstance.GetComponent<NetworkEnemyHealth>();
            if (health != null && autoRespawn)
            {
                health.OnDeath += () => StartCoroutine(RespawnRoutine());
            }

            Debug.Log($"[NetworkEnemySpawner] Successfully spawned enemy '{enemyInstance.name}' (NetId: {netObj.NetworkObjectId}) at {spawnPos}");
            return enemyInstance;
        }

        private Vector3 GetNextSpawnPosition()
        {
            Vector3 basePos = transform.position;

            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                Transform selectedPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];
                if (selectedPoint != null)
                {
                    basePos = selectedPoint.position;
                }
            }

            // Add slight random offset inside spawn radius
            Vector2 randomCircle = Random.insideUnitCircle * spawnRadius;
            Vector3 offset = new Vector3(randomCircle.x, 0f, randomCircle.y);

            return basePos + offset;
        }

        private IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(respawnDelay);

            if (IsServer && IsSpawned)
            {
                SpawnSingleEnemy();
            }
        }
    }
}
