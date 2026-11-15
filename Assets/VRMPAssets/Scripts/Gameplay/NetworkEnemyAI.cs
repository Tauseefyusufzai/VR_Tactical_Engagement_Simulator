using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace XRMultiplayer
{
    /// <summary>
    /// Host-authoritative AI logic for the Survivalist enemy.
    /// Finds nearest VR player, chases via NavMeshAgent, and syncs locomotion animations.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent)), RequireComponent(typeof(NetworkEnemyHealth))]
    public class NetworkEnemyAI : NetworkBehaviour
    {
        public enum AIState
        {
            Idle,
            Chase,
            Attack,
            Dead
        }

        [Header("AI Parameters")]
        [SerializeField] private float detectionRadius = 20.0f;
        [SerializeField] private float attackRange = 2.0f;
        [SerializeField] private float moveSpeed = 3.5f;
        [SerializeField] private float attackCooldown = 1.5f;
        [SerializeField] private float attackDamage = 15.0f;

        [Header("References")]
        [SerializeField] private Animator animator;

        private NavMeshAgent m_NavAgent;
        private NetworkEnemyHealth m_Health;
        private Transform m_TargetPlayer;
        private AIState m_CurrentState = AIState.Idle;
        private float m_NextAttackTime = 0f;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int MotionSpeedHash = Animator.StringToHash("MotionSpeed");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int AttackHash = Animator.StringToHash("Attack");

        private void Awake()
        {
            m_NavAgent = GetComponent<NavMeshAgent>();
            m_Health = GetComponent<NetworkEnemyHealth>();

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (animator != null)
            {
                animator.applyRootMotion = false; // Disable root motion so NavMeshAgent navigates smoothly
            }

            m_NavAgent.speed = moveSpeed;
        }

        private Vector3 m_LastPosition;

        public override void OnNetworkSpawn()
        {
            m_LastPosition = transform.position;

            if (IsServer)
            {
                // Snap agent cleanly to NavMesh surface so feet touch the floor
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 10.0f, NavMesh.AllAreas))
                {
                    m_NavAgent.Warp(hit.position);
                    transform.position = hit.position;
                }

                m_Health.OnDeath += HandleDeath;
            }
            else
            {
                // Disable NavMeshAgent physics component on clients; Host syncs transform
                if (m_NavAgent != null) m_NavAgent.enabled = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (m_Health != null && IsServer)
            {
                m_Health.OnDeath -= HandleDeath;
            }
        }

        private void Update()
        {
            if (m_Health != null && m_Health.IsDead) return;

            // 1. Update Locomotion Animation for ALL (Host & VR Clients)
            UpdateAnimatorLocomotion();

            // 2. Server-only AI Behavior State Machine
            if (!IsServer) return;

            FindNearestPlayer();

            switch (m_CurrentState)
            {
                case AIState.Idle:
                    UpdateIdleState();
                    break;
                case AIState.Chase:
                    UpdateChaseState();
                    break;
                case AIState.Attack:
                    UpdateAttackState();
                    break;
            }
        }

        private void UpdateAnimatorLocomotion()
        {
            if (animator == null) return;

            // Calculate movement speed from position delta (works for Host & Clients)
            float moveDelta = Vector3.Distance(transform.position, m_LastPosition);
            float currentSpeed = Time.deltaTime > 0f ? moveDelta / Time.deltaTime : 0f;
            m_LastPosition = transform.position;

            if (IsServer && m_NavAgent != null && m_NavAgent.enabled && m_NavAgent.isOnNavMesh)
            {
                currentSpeed = Mathf.Max(currentSpeed, m_NavAgent.velocity.magnitude);
            }

            animator.SetFloat(SpeedHash, currentSpeed);
            animator.SetFloat(MotionSpeedHash, 1.0f);
            animator.SetBool(GroundedHash, true);
        }

        private void FindNearestPlayer()
        {
            float closestDistance = float.MaxValue;
            Transform closestPlayer = null;

            // 1. Search for XRINetworkPlayer instances
            XRINetworkPlayer[] networkPlayers = FindObjectsOfType<XRINetworkPlayer>();
            foreach (var nPlayer in networkPlayers)
            {
                if (nPlayer == null) continue;
                float dist = Vector3.Distance(transform.position, nPlayer.transform.position);
                if (dist < closestDistance)
                {
                    closestDistance = dist;
                    closestPlayer = nPlayer.transform;
                }
            }

            // 2. Fallback search for GameObjects with "Player" tag
            if (closestPlayer == null)
            {
                GameObject[] playerObjects = GameObject.FindGameObjectsWithTag("Player");
                foreach (var pObj in playerObjects)
                {
                    if (pObj == null) continue;
                    float dist = Vector3.Distance(transform.position, pObj.transform.position);
                    if (dist < closestDistance)
                    {
                        closestDistance = dist;
                        closestPlayer = pObj.transform;
                    }
                }
            }

            // 3. Fallback search for Main Camera / VR headset
            if (closestPlayer == null && Camera.main != null)
            {
                closestPlayer = Camera.main.transform;
            }

            m_TargetPlayer = closestPlayer;
        }

        private void UpdateIdleState()
        {
            if (m_TargetPlayer != null)
            {
                float dist = Vector3.Distance(transform.position, m_TargetPlayer.position);
                if (dist <= detectionRadius)
                {
                    m_CurrentState = AIState.Chase;
                }
            }
        }

        private void UpdateChaseState()
        {
            if (m_TargetPlayer == null)
            {
                m_CurrentState = AIState.Idle;
                return;
            }

            float dist = Vector3.Distance(transform.position, m_TargetPlayer.position);

            if (dist <= attackRange)
            {
                m_CurrentState = AIState.Attack;
                if (m_NavAgent.isOnNavMesh) m_NavAgent.isStopped = true;
            }
            else if (dist > detectionRadius * 1.5f)
            {
                m_CurrentState = AIState.Idle;
                if (m_NavAgent.isOnNavMesh) m_NavAgent.isStopped = true;
            }
            else
            {
                if (m_NavAgent.isOnNavMesh)
                {
                    m_NavAgent.isStopped = false;
                    m_NavAgent.SetDestination(m_TargetPlayer.position);
                }
            }
        }

        private void UpdateAttackState()
        {
            if (m_TargetPlayer == null)
            {
                m_CurrentState = AIState.Idle;
                return;
            }

            float dist = Vector3.Distance(transform.position, m_TargetPlayer.position);

            if (dist > attackRange * 1.2f)
            {
                m_CurrentState = AIState.Chase;
                return;
            }

            // Face target player
            Vector3 direction = (m_TargetPlayer.position - transform.position).normalized;
            direction.y = 0f;
            if (direction != Vector3.zero)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 5f);
            }

            // Perform Attack
            if (Time.time >= m_NextAttackTime)
            {
                m_NextAttackTime = Time.time + attackCooldown;
                if (animator != null) animator.SetTrigger(AttackHash);

                // Deal damage if player is IDamageable
                var damageable = m_TargetPlayer.GetComponentInParent<IDamageable>() ?? m_TargetPlayer.GetComponent<IDamageable>();
                damageable?.TakeDamage(attackDamage, m_TargetPlayer.position, Vector3.up, NetworkManager.ServerClientId);
            }
        }

        private void HandleDeath()
        {
            m_CurrentState = AIState.Dead;
            if (m_NavAgent.isOnNavMesh)
            {
                m_NavAgent.isStopped = true;
                m_NavAgent.enabled = false;
            }
        }
    }
}
