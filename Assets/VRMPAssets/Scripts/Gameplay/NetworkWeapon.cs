using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace XRMultiplayer
{
    /// <summary>
    /// Networked weapon component handling shooting mechanics, raycasting,
    /// bullet tracers, child muzzle flash activation, audio, and multiplayer synchronization.
    /// </summary>
    public class NetworkWeapon : NetworkBehaviour
    {
        [Header("Weapon Configuration")]
        [SerializeField, Tooltip("Transform representing the weapon barrel tip (e.g. 'Muzzle' child object).")]
        public Transform barrelTransform;

        [SerializeField, Tooltip("Time between consecutive shots in seconds.")]
        public float fireRate = 0.15f;

        [SerializeField, Tooltip("Maximum shooting range in meters.")]
        public float maxDistance = 100f;

        [SerializeField, Tooltip("Damage dealt per shot.")]
        public float damagePerShot = 35f;

        [SerializeField, Tooltip("Layer mask for shooting collision detection.")]
        public LayerMask shootableLayers = ~0;

        [Header("Muzzle Flash VFX")]
        [SerializeField, Tooltip("Child GameObject placed inside weapon prefab representing the MuzzleFlash. Activated momentarily on fire.")]
        public GameObject childMuzzleFlash;

        [SerializeField, Tooltip("Optional custom Muzzle Flash VFX prefab to instantiate at barrel.")]
        public GameObject customMuzzleFlashPrefab;

        [SerializeField, Tooltip("Particle system component if attached directly to barrel.")]
        public ParticleSystem muzzleFlashParticles;

        [SerializeField, Tooltip("Color of the bullet tracer line.")]
        public Color tracerColor = new Color(1f, 0.8f, 0.2f, 1f);

        [SerializeField, Tooltip("Duration of tracer line in seconds.")]
        public float tracerDuration = 0.08f;

        [Header("Audio Effects")]
        [SerializeField, Tooltip("AudioSource component for weapon sounds.")]
        public AudioSource audioSource;

        [SerializeField, Tooltip("Sound clip played when firing.")]
        public AudioClip fireSound;

        private float m_NextFireTime = 0f;
        private Coroutine m_MuzzleFlashCoroutine;

        private void Awake()
        {
            // 1. Locate 'Muzzle' or 'Barrel' child transform
            if (barrelTransform == null)
            {
                foreach (Transform child in GetComponentsInChildren<Transform>(true))
                {
                    if (child.name.Equals("Muzzle", System.StringComparison.OrdinalIgnoreCase) ||
                        child.name.Equals("Barrel", System.StringComparison.OrdinalIgnoreCase))
                    {
                        barrelTransform = child;
                        break;
                    }
                }

                if (barrelTransform == null)
                {
                    GameObject defaultBarrel = new GameObject("Muzzle");
                    defaultBarrel.transform.SetParent(transform, false);
                    defaultBarrel.transform.localPosition = new Vector3(0f, 0.05f, 0.25f);
                    defaultBarrel.transform.localRotation = Quaternion.identity;
                    barrelTransform = defaultBarrel.transform;
                }
            }

            // 2. Locate child MuzzleFlash GameObject inside weapon
            if (childMuzzleFlash == null)
            {
                foreach (Transform child in GetComponentsInChildren<Transform>(true))
                {
                    if (child != transform && (child.name.Equals("MuzzleFlash", System.StringComparison.OrdinalIgnoreCase) ||
                                               child.name.Equals("Muzzle Flash", System.StringComparison.OrdinalIgnoreCase) ||
                                               child.name.Contains("MuzzleFlash")))
                    {
                        childMuzzleFlash = child.gameObject;
                        break;
                    }
                }
            }

            // Disable child MuzzleFlash by default so it only appears when shooting
            if (childMuzzleFlash != null)
            {
                childMuzzleFlash.SetActive(false);
            }

            // 3. AudioSource setup
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                    audioSource.spatialBlend = 1.0f; // 3D Audio
                    audioSource.maxDistance = 30f;
                    audioSource.playOnAwake = false;
                }
            }

            // 4. Audio Clip auto-load
            if (fireSound == null)
            {
                fireSound = Resources.Load<AudioClip>("Weapons/eaglaxle-gun-shot-1-530788")
                         ?? Resources.Load<AudioClip>("eaglaxle-gun-shot-1-530788");
            }

            Debug.Log($"[NetworkWeapon] Initialized on '{gameObject.name}'. Barrel: '{barrelTransform.name}'. ChildMuzzleFlash: '{(childMuzzleFlash != null ? childMuzzleFlash.name : "None")}'.");
        }

        /// <summary>
        /// Attempts to fire the weapon. Checks rate of fire cooldown.
        /// </summary>
        public bool TryShoot()
        {
            if (Time.time < m_NextFireTime) return false;

            m_NextFireTime = Time.time + fireRate;

            Vector3 origin = barrelTransform.position;
            Vector3 direction = barrelTransform.forward;

            ulong targetNetId = 0;
            Vector3 hitPoint = origin + direction * maxDistance;
            Vector3 hitNormal = -direction;

            if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, shootableLayers, QueryTriggerInteraction.Collide))
            {
                hitPoint = hit.point;
                hitNormal = hit.normal;
                var netObj = hit.collider.GetComponentInParent<NetworkObject>() ?? hit.collider.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    targetNetId = netObj.NetworkObjectId;
                    Debug.Log($"[NetworkWeapon] Raycast HIT object '{hit.collider.gameObject.name}' (Root NetObj '{netObj.gameObject.name}', ID #{targetNetId}) at {hitPoint}");
                }
                else
                {
                    Debug.Log($"[NetworkWeapon] Raycast HIT non-networked object '{hit.collider.gameObject.name}' at {hitPoint}");
                }
            }
            else
            {
                Debug.Log($"[NetworkWeapon] Raycast MISSED all objects along line {origin} -> {direction}");
            }

            // Trigger networked shooting RPC
            if (IsSpawned)
            {
                Debug.Log($"[NetworkWeapon] Sending ShootServerRpc... TargetNetId: {targetNetId}");
                ShootServerRpc(origin, direction, targetNetId, hitPoint, hitNormal);
            }
            else
            {
                // Standalone fallback if not spawned on network yet
                ExecuteShootVisualsAndEffects(origin, direction);

                if (Physics.Raycast(origin, direction, out RaycastHit localHit, maxDistance, shootableLayers, QueryTriggerInteraction.Collide))
                {
                    var damageable = localHit.collider.GetComponentInParent<IDamageable>() ?? localHit.collider.GetComponent<IDamageable>() ?? localHit.collider.GetComponentInChildren<IDamageable>();
                    damageable?.TakeDamage(damagePerShot, localHit.point, localHit.normal, 0);
                }
            }

            return true;
        }

        [ServerRpc(RequireOwnership = false)]
        private void ShootServerRpc(Vector3 origin, Vector3 direction, ulong targetNetId, Vector3 hitPoint, Vector3 hitNormal, ServerRpcParams rpcParams = default)
        {
            ShootClientRpc(origin, direction);

            ulong shooterId = rpcParams.Receive.SenderClientId;
            Debug.Log($"[NetworkWeapon] ShootServerRpc received on Server from shooter #{shooterId}. TargetNetId: {targetNetId}");

            // 1. Direct hit application via Client-verified Target ID
            if (targetNetId != 0 && NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetId, out var targetNetObj))
            {
                var damageable = targetNetObj.GetComponentInParent<IDamageable>() ?? targetNetObj.GetComponent<IDamageable>() ?? targetNetObj.GetComponentInChildren<IDamageable>();
                if (damageable != null)
                {
                    Debug.Log($"[NetworkWeapon] Direct hit on TargetNetObj '{targetNetObj.gameObject.name}'! Invoking TakeDamage({damagePerShot})...");
                    damageable.TakeDamage(damagePerShot, hitPoint, hitNormal, shooterId);
                    return;
                }
                else
                {
                    Debug.LogWarning($"[NetworkWeapon] TargetNetObj '{targetNetObj.gameObject.name}' found, but has NO IDamageable component!");
                }
            }

            // 2. Server-side Raycast Fallback
            if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, shootableLayers, QueryTriggerInteraction.Collide))
            {
                var damageable = hit.collider.GetComponentInParent<IDamageable>() ?? hit.collider.GetComponent<IDamageable>() ?? hit.collider.GetComponentInChildren<IDamageable>();
                if (damageable != null)
                {
                    Debug.Log($"[NetworkWeapon] Server fallback raycast HIT '{hit.collider.gameObject.name}'! Invoking TakeDamage({damagePerShot})...");
                    damageable.TakeDamage(damagePerShot, hit.point, hit.normal, shooterId);
                }
                else
                {
                    Debug.Log($"[NetworkWeapon] Server fallback raycast hit '{hit.collider.gameObject.name}', but no IDamageable found.");
                }
            }
            else
            {
                Debug.Log($"[NetworkWeapon] Server fallback raycast missed all targets.");
            }
        }

        [ClientRpc]
        private void ShootClientRpc(Vector3 origin, Vector3 direction)
        {
            ExecuteShootVisualsAndEffects(origin, direction);
        }

        /// <summary>
        /// Executes local visual effects, tracers, raycasting, and audio on all clients.
        /// </summary>
        private void ExecuteShootVisualsAndEffects(Vector3 origin, Vector3 direction)
        {
            // 1. Raycast for hit detection & end point calculation
            Vector3 endPoint = origin + direction * maxDistance;
            if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, shootableLayers, QueryTriggerInteraction.Ignore))
            {
                endPoint = hit.point;

                // Spawn impact visual (spark / decal)
                SpawnImpactEffect(hit.point, hit.normal);
            }

            // 2. Play Muzzle Flash VFX
            PlayMuzzleFlashVFX(origin);

            // 3. Render Bullet Tracer
            StartCoroutine(RenderTracerLine(origin, endPoint));

            // 4. Play Audio
            PlayFireAudio();
        }

        /// <summary>
        /// Activates child MuzzleFlash inside weapon, or instantiates custom prefab / procedural spark.
        /// </summary>
        private void PlayMuzzleFlashVFX(Vector3 origin)
        {
            // Option 1: Child MuzzleFlash GameObject inside weapon prefab (User's placed MuzzleFlash)
            if (childMuzzleFlash != null)
            {
                if (m_MuzzleFlashCoroutine != null) StopCoroutine(m_MuzzleFlashCoroutine);
                m_MuzzleFlashCoroutine = StartCoroutine(ActivateChildMuzzleFlashRoutine());
                return;
            }

            // Option 2: Custom Muzzle Flash Prefab to instantiate
            if (customMuzzleFlashPrefab != null && barrelTransform != null)
            {
                GameObject flashInstance = Instantiate(customMuzzleFlashPrefab, barrelTransform.position, barrelTransform.rotation, barrelTransform);
                flashInstance.transform.localPosition = Vector3.zero;
                flashInstance.transform.localRotation = Quaternion.identity;

                ParticleSystem[] pss = flashInstance.GetComponentsInChildren<ParticleSystem>(true);
                float maxLife = 0.3f;
                foreach (var ps in pss)
                {
                    ps.gameObject.SetActive(true);
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play(true);
                    float psTime = ps.main.duration + ps.main.startLifetime.constantMax;
                    if (psTime > maxLife) maxLife = psTime;
                }

                Renderer[] renderers = flashInstance.GetComponentsInChildren<Renderer>(true);
                foreach (var rend in renderers)
                {
                    rend.enabled = true;
                    rend.gameObject.layer = LayerMask.NameToLayer("Default");
                }

                Destroy(flashInstance, Mathf.Clamp(maxLife, 0.15f, 1.5f));
                return;
            }

            // Option 3: Particle System on Barrel
            if (muzzleFlashParticles != null)
            {
                muzzleFlashParticles.Play();
                return;
            }

            // Option 4: Fallback Procedural Muzzle Flash Spark
            CreateProceduralMuzzleFlash(origin);
        }

        /// <summary>
        /// Momentarily activates the child MuzzleFlash GameObject inside the weapon when firing.
        /// </summary>
        private IEnumerator ActivateChildMuzzleFlashRoutine()
        {
            if (childMuzzleFlash == null) yield break;

            childMuzzleFlash.SetActive(true);

            // Trigger all particle systems inside child MuzzleFlash
            ParticleSystem[] pss = childMuzzleFlash.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in pss)
            {
                ps.gameObject.SetActive(true);
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
            }

            // Ensure renderers are enabled
            Renderer[] renderers = childMuzzleFlash.GetComponentsInChildren<Renderer>(true);
            foreach (var rend in renderers)
            {
                rend.enabled = true;
                rend.gameObject.layer = LayerMask.NameToLayer("Default");
            }

            // Keep flash visible for short burst duration (0.06s)
            yield return new WaitForSeconds(0.06f);

            if (childMuzzleFlash != null)
            {
                childMuzzleFlash.SetActive(false);
            }
        }

        private void SpawnImpactEffect(Vector3 hitPoint, Vector3 hitNormal)
        {
            GameObject spark = new GameObject("HitImpactFX");
            spark.transform.position = hitPoint;
            spark.transform.rotation = Quaternion.LookRotation(hitNormal);

            Light light = spark.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.7f, 0.3f);
            light.range = 2f;
            light.intensity = 3f;

            Destroy(spark, 0.1f);
        }

        private void CreateProceduralMuzzleFlash(Vector3 position)
        {
            GameObject flashObj = new GameObject("MuzzleFlash_Burst");
            flashObj.transform.position = position;
            flashObj.transform.rotation = barrelTransform != null ? barrelTransform.rotation : transform.rotation;

            Light light = flashObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.85f, 0.35f);
            light.range = 2.0f;
            light.intensity = 6f;

            GameObject meshObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            meshObj.transform.SetParent(flashObj.transform, false);
            meshObj.transform.localScale = Vector3.one * 0.06f;
            meshObj.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            meshObj.transform.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));

            Renderer r = meshObj.GetComponent<Renderer>();
            if (r != null)
            {
                Material m = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit"));
                m.color = new Color(1f, 0.9f, 0.3f, 1f);
                r.material = m;
            }
            Destroy(meshObj.GetComponent<Collider>());

            Destroy(flashObj, 0.05f);
        }

        private IEnumerator RenderTracerLine(Vector3 start, Vector3 end)
        {
            GameObject tracerObj = new GameObject("BulletTracer");
            LineRenderer lr = tracerObj.AddComponent<LineRenderer>();

            lr.startWidth = 0.02f;
            lr.endWidth = 0.005f;
            lr.positionCount = 2;
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);

            Material mat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit"));
            mat.color = tracerColor;
            lr.material = mat;

            float elapsed = 0f;
            while (elapsed < tracerDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / tracerDuration;
                Color current = tracerColor;
                current.a = Mathf.Lerp(1f, 0f, t);
                mat.color = current;
                yield return null;
            }

            Destroy(tracerObj);
            Destroy(mat);
        }

        private void PlayFireAudio()
        {
            if (audioSource != null)
            {
                if (fireSound != null)
                {
                    audioSource.PlayOneShot(fireSound);
                }
                else
                {
                    audioSource.Play();
                }
            }
        }
    }
}
