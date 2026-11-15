using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace XRMultiplayer
{
    /// <summary>
    /// Synchronizes health, hit reactions, and death behavior for networked enemies across Host and Clients.
    /// </summary>
    public class NetworkEnemyHealth : NetworkBehaviour, IDamageable
    {
        [Header("Health Settings")]
        [SerializeField] private float maxHealth = 100f;

        [Header("Effects")]
        [SerializeField] private GameObject hitEffectPrefab;
        [SerializeField] private float despawnDelayAfterDeath = 5.0f;

        public NetworkVariable<float> currentHealth = new NetworkVariable<float>(
            100f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public bool IsDead => currentHealth.Value <= 0f;

        public event Action<float, float> OnHealthChanged; // (current, max)
        public event Action OnDeath;

        private Animator m_Animator;
        private Collider[] m_Colliders;

        private void Awake()
        {
            m_Animator = GetComponentInChildren<Animator>();
            m_Colliders = GetComponentsInChildren<Collider>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                currentHealth.Value = maxHealth;
            }

            currentHealth.OnValueChanged += HandleHealthChanged;
        }

        public override void OnNetworkDespawn()
        {
            currentHealth.OnValueChanged -= HandleHealthChanged;
        }

        private void HandleHealthChanged(float previousValue, float newValue)
        {
            OnHealthChanged?.Invoke(newValue, maxHealth);

            if (newValue <= 0f && previousValue > 0f)
            {
                HandleDeathVisuals();
            }
        }

        /// <summary>
        /// IDamageable implementation. Called by NetworkWeapon on bullet raycast hit.
        /// </summary>
        public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal, ulong attackerId)
        {
            if (IsDead) return;

            if (!IsSpawned || IsServer)
            {
                currentHealth.Value = Mathf.Max(0f, currentHealth.Value - amount);
                Debug.Log($"[NetworkEnemyHealth] Enemy '{gameObject.name}' took {amount} damage from shooter #{attackerId}! Current Health: {currentHealth.Value}/{maxHealth}");

                if (IsSpawned)
                {
                    PlayHitReactionClientRpc(hitPoint, hitNormal);
                }
                else
                {
                    PlayHitVisualsLocal(hitPoint, hitNormal);
                }

                if (currentHealth.Value <= 0f)
                {
                    Debug.Log($"[NetworkEnemyHealth] Enemy '{gameObject.name}' has been KILLED!");
                    OnDeathServer();
                }
            }
        }

        private void PlayHitVisualsLocal(Vector3 hitPoint, Vector3 hitNormal)
        {
            if (hitEffectPrefab != null)
            {
                Instantiate(hitEffectPrefab, hitPoint, Quaternion.LookRotation(hitNormal));
            }
            if (m_Animator != null && !IsDead)
            {
                m_Animator.SetTrigger("Hit");
            }
        }

        [ClientRpc]
        private void PlayHitReactionClientRpc(Vector3 hitPoint, Vector3 hitNormal)
        {
            PlayHitVisualsLocal(hitPoint, hitNormal);
        }

        private void OnDeathServer()
        {
            OnDeath?.Invoke();

            // Disable navigation and physics on death
            var navAgent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (navAgent != null)
            {
                navAgent.enabled = false;
            }

            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true; // Keep kinematic during death animation
            }

            foreach (var col in m_Colliders)
            {
                col.enabled = false;
            }

            StartCoroutine(DespawnRoutine());
        }

        private void HandleDeathVisuals()
        {
            if (m_Animator != null)
            {
                m_Animator.SetTrigger("Die");
                m_Animator.SetBool("IsDead", true);
            }
        }

        private IEnumerator DespawnRoutine()
        {
            yield return new WaitForSeconds(despawnDelayAfterDeath);

            if (IsSpawned && IsServer)
            {
                NetworkObject.Despawn(true);
            }
        }
    }
}
