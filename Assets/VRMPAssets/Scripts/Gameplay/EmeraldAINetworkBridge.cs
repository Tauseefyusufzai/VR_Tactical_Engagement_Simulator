using System.Collections;
using UnityEngine;
using Unity.Netcode;
using EmeraldAI;
using EmeraldAI.Utility;

namespace XRMultiplayer
{
    /// <summary>
    /// Network Bridge component for Emerald AI enemies in VR Multiplayer.
    /// Synchronizes locomotion parameters, client shooting damage, VR player targeting, and death animations over NGO.
    /// </summary>
    [RequireComponent(typeof(EmeraldAISystem))]
    public class EmeraldAINetworkBridge : NetworkBehaviour, IDamageable
    {
        private EmeraldAISystem m_EmeraldAI;
        private EmeraldAIDetection m_Detection;
        private EmeraldAIBehaviors m_Behaviors;

        private Vector3 m_LastPos;
        private Vector3 m_LastForward;

        private void Awake()
        {
            m_EmeraldAI = GetComponent<EmeraldAISystem>();
            m_Detection = GetComponent<EmeraldAIDetection>();
            m_Behaviors = GetComponent<EmeraldAIBehaviors>();

            // Remove BoxCollider if present - causes collision issues with Emerald AI
            var boxCol = GetComponent<BoxCollider>();
            if (boxCol != null) Destroy(boxCol);
        }

        public override void OnNetworkSpawn()
        {
            // Remove BoxCollider
            var boxCol = GetComponent<BoxCollider>();
            if (boxCol != null) { Debug.Log("[EmeraldAINetworkBridge] Removing BoxCollider"); Destroy(boxCol); }

            // Guarantee CapsuleCollider exists and is enabled for bullet raycasts
            var capCol = GetComponent<CapsuleCollider>();
            if (capCol == null)
            {
                capCol = gameObject.AddComponent<CapsuleCollider>();
            }
            capCol.center = new Vector3(0f, 0.9f, 0f);
            capCol.height = 1.8f;
            capCol.radius = 0.45f;
            capCol.isTrigger = false;
            capCol.enabled = true;
            Debug.Log($"[EmeraldAINetworkBridge] CapsuleCollider configured on '{gameObject.name}': center={capCol.center}, height={capCol.height}, radius={capCol.radius}, enabled={capCol.enabled}");

            // Configure Rigidbody: keep kinematic so physics gravity doesn't fight NavMeshAgent ground alignment during life
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                Debug.Log($"[EmeraldAINetworkBridge] Rigidbody configured: gravity={rb.useGravity}, kinematic={rb.isKinematic}");
            }

            // Disable Animator root motion to prevent Y-position pulling during animations
            if (m_EmeraldAI != null && m_EmeraldAI.AIAnimator != null)
            {
                m_EmeraldAI.AIAnimator.applyRootMotion = false;
                if (m_EmeraldAI.AIAnimator.runtimeAnimatorController == null)
                {
                    var masterController = Resources.Load<RuntimeAnimatorController>("Emerald Animator Controller");
                    if (masterController != null) m_EmeraldAI.AIAnimator.runtimeAnimatorController = masterController;
                }
            }

            // Force initial upright rotation
            transform.rotation = Quaternion.Euler(0f, transform.rotation.eulerAngles.y, 0f);
            Debug.Log($"[EmeraldAINetworkBridge] Spawned at pos={transform.position}, rot={transform.rotation.eulerAngles}, IsServer={IsServer}");

            if (IsServer)
            {
                // Snap character position firmly to floor NavMesh surface
                var navAgent = GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (navAgent != null)
                {
                    navAgent.baseOffset = -0.05f;
                    if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out UnityEngine.AI.NavMeshHit hit, 5.0f, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        navAgent.Warp(hit.position);
                    }
                }

                // Host handles AI decision making
                if (m_EmeraldAI != null)
                {
                    m_EmeraldAI.WalkSpeed = 0.8f;
                    m_EmeraldAI.AlignAIWithGroundRef = EmeraldAISystem.YesOrNo.No;
                    m_EmeraldAI.BehaviorRef = EmeraldAISystem.CurrentBehavior.Aggressive;
                    m_EmeraldAI.ConfidenceRef = EmeraldAISystem.ConfidenceType.Brave;
                    m_EmeraldAI.PlayerTag = "Player";
                    if (m_EmeraldAI.PlayerFaction == null) m_EmeraldAI.PlayerFaction = new System.Collections.Generic.List<EmeraldAISystem.PlayerFactionClass>();
                    if (m_EmeraldAI.PlayerFaction.Count == 0)
                    {
                        m_EmeraldAI.PlayerFaction.Add(new EmeraldAISystem.PlayerFactionClass("Player", 0));
                    }
                    m_EmeraldAI.DetectionLayerMask = ~0;
                    m_EmeraldAI.fieldOfViewAngle = 360;
                    m_EmeraldAI.DetectionRadius = 35;
                    m_EmeraldAI.MeleeAttackDistance = 1.8f;
                    m_EmeraldAI.AttackDistance = 1.8f;
                    m_EmeraldAI.DeathTypeRef = EmeraldAISystem.DeathType.Animation;
                    m_EmeraldAI.enabled = true;
                }
                if (m_Detection != null) m_Detection.enabled = true;
                if (m_Behaviors != null) m_Behaviors.enabled = true;

                StartCoroutine(TargetDetectionRoutine());
            }
            else
            {
                // Clients do NOT run local AI pathfinding/vision; NetworkTransform & NetworkAnimator replicate movement
                if (m_EmeraldAI != null)
                {
                    m_EmeraldAI.AlignAIWithGroundRef = EmeraldAISystem.YesOrNo.No;
                    m_EmeraldAI.enabled = false;
                }
                if (m_Detection != null) m_Detection.enabled = false;
                if (m_Behaviors != null) m_Behaviors.enabled = false;

                var navAgent = GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (navAgent != null) navAgent.enabled = false;
            }
        }

        private void Update()
        {
            if (m_EmeraldAI != null && m_EmeraldAI.AIAnimator != null)
            {
                // If enemy is dead, stop locomotion parameter updating completely
                if (m_EmeraldAI.IsDead || m_EmeraldAI.CurrentHealth <= 0)
                {
                    m_EmeraldAI.AIAnimator.SetFloat("Speed", 0f);
                    return;
                }

                float distMoved = Vector3.Distance(transform.position, m_LastPos);
                m_LastPos = transform.position;
                float currentSpeed = distMoved / Mathf.Max(Time.deltaTime, 0.001f);

                if (m_LastForward == Vector3.zero) m_LastForward = transform.forward;
                float angleDelta = Vector3.SignedAngle(m_LastForward, transform.forward, Vector3.up);
                m_LastForward = transform.forward;
                float turnRate = angleDelta / Mathf.Max(Time.deltaTime, 0.001f);

                if (currentSpeed > 0.10f)
                {
                    // Moving locomotion
                    float animSpeed = Mathf.Clamp01(currentSpeed / 2.0f);
                    m_EmeraldAI.AIAnimator.SetFloat("Speed", animSpeed, 0.05f, Time.deltaTime);

                    float playbackSpeedMultiplier = Mathf.Clamp(currentSpeed / 0.8f, 1.2f, 2.5f);
                    m_EmeraldAI.AIAnimator.speed = playbackSpeedMultiplier;

                    m_EmeraldAI.AIAnimator.SetBool("Turn Left", false);
                    m_EmeraldAI.AIAnimator.SetBool("Turn Right", false);
                }
                else
                {
                    // Stationary turning check
                    m_EmeraldAI.AIAnimator.SetFloat("Speed", 0f, 0.1f, Time.deltaTime);
                    m_EmeraldAI.AIAnimator.speed = 1.0f;

                    if (Mathf.Abs(turnRate) > 15f)
                    {
                        bool turningRight = turnRate > 0;
                        m_EmeraldAI.AIAnimator.SetBool("Turn Right", turningRight);
                        m_EmeraldAI.AIAnimator.SetBool("Turn Left", !turningRight);
                        m_EmeraldAI.AIAnimator.SetFloat("Direction", Mathf.Clamp(turnRate / 90.0f, -1.0f, 1.0f), 0.05f, Time.deltaTime);
                    }
                    else
                    {
                        m_EmeraldAI.AIAnimator.SetBool("Turn Left", false);
                        m_EmeraldAI.AIAnimator.SetBool("Turn Right", false);
                        m_EmeraldAI.AIAnimator.SetFloat("Direction", 0f, 0.1f, Time.deltaTime);
                    }
                }
            }
        }

        /// <summary>
        /// Force upright rotation AFTER Emerald AI and physics have run (only while alive).
        /// </summary>
        private void LateUpdate()
        {
            if (m_EmeraldAI != null && (m_EmeraldAI.IsDead || m_EmeraldAI.CurrentHealth <= 0))
            {
                return; // Do NOT lock rotation on death so character falls naturally onto floor
            }

            Vector3 euler = transform.rotation.eulerAngles;
            float xAngle = euler.x > 180f ? euler.x - 360f : euler.x;
            float zAngle = euler.z > 180f ? euler.z - 360f : euler.z;
            if (Mathf.Abs(xAngle) > 0.5f || Mathf.Abs(zAngle) > 0.5f)
            {
                transform.rotation = Quaternion.Euler(0f, euler.y, 0f);
            }
        }

        /// <summary>
        /// Continuously ensures Emerald AI targets active VR players.
        /// </summary>
        private IEnumerator TargetDetectionRoutine()
        {
            while (IsServer && IsSpawned)
            {
                yield return new WaitForSeconds(0.5f);

                if (m_EmeraldAI == null || m_EmeraldAI.IsDead || m_EmeraldAI.CurrentHealth <= 0) break;

                Transform targetPlayer = FindNearestVRPlayer();
                if (targetPlayer != null)
                {
                    m_EmeraldAI.CurrentTarget = targetPlayer;
                    if (m_EmeraldAI.CombatStateRef == EmeraldAISystem.CombatState.NotActive)
                    {
                        m_EmeraldAI.EmeraldBehaviorsComponent.ActivateCombatState();
                    }
                }
            }
        }

        private Transform FindNearestVRPlayer()
        {
            float closestDist = float.MaxValue;
            Transform closest = null;

            XRINetworkPlayer[] players = FindObjectsOfType<XRINetworkPlayer>();
            foreach (var p in players)
            {
                if (p == null) continue;

                // Dynamically ensure VR player has "Player" tag so Emerald AI detection accepts it
                if (!p.CompareTag("Player"))
                {
                    try { p.tag = "Player"; } catch { }
                }

                float d = Vector3.Distance(transform.position, p.transform.position);
                if (d < closestDist)
                {
                    closestDist = d;
                    closest = p.transform;
                }
            }

            if (closest == null && Camera.main != null)
            {
                closest = Camera.main.transform;
                if (!closest.CompareTag("Player"))
                {
                    try { closest.tag = "Player"; } catch { }
                }
            }

            return closest;
        }

        /// <summary>
        /// IDamageable implementation. Receives bullet damage from VR NetworkWeapon.
        /// Handles Client-to-Server damage synchronization via ServerRpc.
        /// </summary>
        public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitNormal, ulong attackerId)
        {
            Debug.Log($"[EmeraldAINetworkBridge] TakeDamage called on '{gameObject.name}'! Amount: {amount}, AttackerId: {attackerId}, IsServer: {IsServer}, IsSpawned: {IsSpawned}, IsDead: {(m_EmeraldAI != null ? m_EmeraldAI.IsDead.ToString() : "NULL")}");

            if (m_EmeraldAI == null)
            {
                Debug.LogError($"[EmeraldAINetworkBridge] TakeDamage FAILED - m_EmeraldAI is NULL on '{gameObject.name}'!");
                return;
            }

            if (m_EmeraldAI.IsDead)
            {
                Debug.Log($"[EmeraldAINetworkBridge] TakeDamage IGNORED - Enemy '{gameObject.name}' is ALREADY DEAD.");
                return;
            }

            if (IsServer)
            {
                ApplyDamageServerInternal((int)amount, attackerId);
            }
            else
            {
                Debug.Log($"[EmeraldAINetworkBridge] Client forwarding damage {amount} to Server via TakeDamageServerRpc...");
                TakeDamageServerRpc((int)amount, attackerId);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void TakeDamageServerRpc(int damageAmount, ulong attackerId)
        {
            Debug.Log($"[EmeraldAINetworkBridge] TakeDamageServerRpc received on Server for enemy '{gameObject.name}'. Amount: {damageAmount}, ShooterId: {attackerId}");
            ApplyDamageServerInternal(damageAmount, attackerId);
        }

        private void ApplyDamageServerInternal(int damageAmount, ulong attackerId)
        {
            if (m_EmeraldAI == null || m_EmeraldAI.IsDead) return;

            Transform attackerTransform = null;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var attackerNetObj))
            {
                attackerTransform = attackerNetObj.transform;
            }

            // Guard PlayerFaction so Emerald AI Damage doesn't throw IndexOutOfRangeException
            if (m_EmeraldAI.PlayerFaction == null || m_EmeraldAI.PlayerFaction.Count == 0)
            {
                m_EmeraldAI.PlayerFaction = new System.Collections.Generic.List<EmeraldAISystem.PlayerFactionClass>
                {
                    new EmeraldAISystem.PlayerFactionClass("Player", 0)
                };
            }

            int healthBefore = m_EmeraldAI.CurrentHealth;
            m_EmeraldAI.CurrentHealth -= damageAmount;
            Debug.Log($"[EmeraldAINetworkBridge] Server processing damage on '{gameObject.name}'. Health: {healthBefore} -> {m_EmeraldAI.CurrentHealth} (took {damageAmount} damage)");

            // Pass damage notification into Emerald AI system
            try
            {
                m_EmeraldAI.Damage(damageAmount, EmeraldAISystem.TargetType.Player, attackerTransform);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[EmeraldAINetworkBridge] Exception inside m_EmeraldAI.Damage: {ex.Message}\n{ex.StackTrace}");
            }

            // Check if health reached zero or below
            if (m_EmeraldAI.CurrentHealth <= 0 && !m_EmeraldAI.IsDead)
            {
                Debug.Log($"[EmeraldAINetworkBridge] *** Enemy '{gameObject.name}' DIED! Invoking OnEnemyDiedClientRpc & TriggerDeathSequence. ***");

                TriggerDeathSequence();

                // Replicate Death state and play Death Animation across all Clients
                OnEnemyDiedClientRpc();

                try
                {
                    m_EmeraldAI.EmeraldBehaviorsComponent.DeadState();
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[EmeraldAINetworkBridge] Exception inside DeadState: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        [ClientRpc]
        private void OnEnemyDiedClientRpc()
        {
            Debug.Log($"[EmeraldAINetworkBridge] OnEnemyDiedClientRpc executed on Client for '{gameObject.name}'! Playing Death Animation.");
            TriggerDeathSequence();
        }

        private void TriggerDeathSequence()
        {
            if (m_EmeraldAI == null) return;

            m_EmeraldAI.CurrentHealth = 0;
            m_EmeraldAI.IsDead = true;

            var navAgent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (navAgent != null)
            {
                if (navAgent.enabled)
                {
                    navAgent.ResetPath();
                    navAgent.isStopped = true;
                }
                navAgent.enabled = false;
            }

            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            if (m_EmeraldAI.AIAnimator != null)
            {
                m_EmeraldAI.AIAnimator.enabled = true;
                m_EmeraldAI.AIAnimator.speed = 1.0f;
                m_EmeraldAI.AIAnimator.SetFloat("Speed", 0f);

                int totalDeaths = m_EmeraldAI.TotalDeathAnimations > 0 ? m_EmeraldAI.TotalDeathAnimations : 4;
                int deathIndex = Random.Range(1, totalDeaths + 1);

                m_EmeraldAI.AIAnimator.SetInteger("Death Index", deathIndex);
                m_EmeraldAI.AIAnimator.SetTrigger("Dead");

                try
                {
                    m_EmeraldAI.AIAnimator.Play($"Death States.Death {deathIndex}", 0, 0f);
                }
                catch
                {
                    try { m_EmeraldAI.AIAnimator.Play($"Death {deathIndex}", 0, 0f); } catch { }
                }
            }
        }
    }
}
