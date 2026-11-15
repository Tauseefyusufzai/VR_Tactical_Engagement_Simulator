using UnityEngine;
using UnityEditor;
using Unity.Netcode;
using Unity.Netcode.Components;
using EmeraldAI;
using EmeraldAI.Utility;

namespace XRMultiplayer.Editor
{
    /// <summary>
    /// Editor script to construct the complete, rig-compatible Network Emerald AI Survivalist Enemy prefab.
    /// Access via: Tools -> VRMP -> Create Emerald AI Networked Enemy Prefab
    /// </summary>
    public static class EmeraldAISurvivalistSetup
    {
        [MenuItem("Tools/VRMP/Create Emerald AI Networked Enemy Prefab")]
        public static void CreateSurvivalistEnemyPrefab()
        {
            // 1. Locate character model asset (Military SK_Military_CHR7_URP or Survivalist)
            string modelPath = "Assets/Military/Prefab/URP/SK_Military_CHR7_URP.prefab";
            GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

            if (modelPrefab == null)
            {
                modelPath = "Assets/Survivalist/Basemesh/SK_Military_Survivalist.fbx";
                modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            }

            if (modelPrefab == null)
            {
                string[] guids = AssetDatabase.FindAssets("SK_Military_CHR7_URP t:Prefab");
                if (guids.Length == 0) guids = AssetDatabase.FindAssets("SK_Military_Survivalist");
                foreach (string g in guids)
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    if (modelPrefab != null) break;
                }
            }

            if (modelPrefab == null)
            {
                Debug.LogError("[EmeraldAISurvivalistSetup] Could not find SK_Military_CHR7_URP or SK_Military_Survivalist model!");
                return;
            }

            // 2. Instantiate temporary GameObject in scene
            GameObject enemyObj = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
            enemyObj.name = "NetworkEmeraldSurvivalistEnemy";

            // 3. Ensure Emerald AI components exist
            EmeraldAISystem emeraldAI = enemyObj.GetComponent<EmeraldAISystem>();
            if (emeraldAI == null) emeraldAI = enemyObj.AddComponent<EmeraldAISystem>();

            Animator animator = enemyObj.GetComponent<Animator>();
            if (animator == null) animator = enemyObj.AddComponent<Animator>();

            RuntimeAnimatorController baseController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Emerald AI/Resources/Emerald Animator Controller.overrideController");
            if (baseController != null)
            {
                animator.runtimeAnimatorController = baseController;
            }
            emeraldAI.AIAnimator = animator;

            EmeraldAIInitializer initializer = enemyObj.GetComponent<EmeraldAIInitializer>();
            if (initializer == null) enemyObj.AddComponent<EmeraldAIInitializer>();

            EmeraldAIDetection detection = enemyObj.GetComponent<EmeraldAIDetection>();
            if (detection == null) enemyObj.AddComponent<EmeraldAIDetection>();

            EmeraldAIBehaviors behaviors = enemyObj.GetComponent<EmeraldAIBehaviors>();
            if (behaviors == null) enemyObj.AddComponent<EmeraldAIBehaviors>();

            EmeraldAIEventsManager eventsManager = enemyObj.GetComponent<EmeraldAIEventsManager>();
            if (eventsManager == null) enemyObj.AddComponent<EmeraldAIEventsManager>();

            // 4. Attach Rifle Weapon (SM_Military_Rifle2.prefab) to Right Hand (hand_r)
            Transform rightHandTransform = null;
            foreach (Transform t in enemyObj.GetComponentsInChildren<Transform>())
            {
                if (t.name.Equals("hand_r", System.StringComparison.OrdinalIgnoreCase) ||
                    t.name.Equals("RightHand", System.StringComparison.OrdinalIgnoreCase) ||
                    t.name.Equals("Right_Hand", System.StringComparison.OrdinalIgnoreCase) ||
                    t.name.Contains("RightHand") || t.name.Contains("hand_r"))
                {
                    rightHandTransform = t;
                    break;
                }
            }

            GameObject riflePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Military/Prefab/SM_Military_Rifle2.prefab");
            if (riflePrefab != null && rightHandTransform != null)
            {
                for (int i = rightHandTransform.childCount - 1; i >= 0; i--)
                {
                    Transform child = rightHandTransform.GetChild(i);
                    if (child.name.Contains("Rifle") || child.name.Contains("Weapon"))
                    {
                        Object.DestroyImmediate(child.gameObject);
                    }
                }

                GameObject rifleInstance = (GameObject)PrefabUtility.InstantiatePrefab(riflePrefab, rightHandTransform);
                rifleInstance.name = "RightHand_Weapon_Rifle";
                rifleInstance.transform.localPosition = new Vector3(0.05f, 0.05f, 0.02f);
                rifleInstance.transform.localRotation = Quaternion.Euler(0f, 90f, 90f);
                rifleInstance.transform.localScale = Vector3.one;

                Transform muzzleTransform = rifleInstance.transform.Find("Muzzle");
                if (muzzleTransform == null)
                {
                    GameObject muzzleObj = new GameObject("Muzzle");
                    muzzleObj.transform.SetParent(rifleInstance.transform);
                    muzzleObj.transform.localPosition = new Vector3(0f, 0.15f, 0.6f);
                    muzzleObj.transform.localRotation = Quaternion.identity;
                    muzzleTransform = muzzleObj.transform;
                }

                emeraldAI.RangedAttackTransform = muzzleTransform;
            }

            // 5. Configure CapsuleCollider & Rigidbody
            CapsuleCollider collider = enemyObj.GetComponent<CapsuleCollider>();
            if (collider == null) collider = enemyObj.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 0.9f, 0f);
            collider.height = 1.8f;
            collider.radius = 0.45f;
            collider.isTrigger = false;

            BoxCollider boxCollider = enemyObj.GetComponent<BoxCollider>();
            if (boxCollider != null) Object.DestroyImmediate(boxCollider);

            Rigidbody rb = enemyObj.GetComponent<Rigidbody>();
            if (rb == null) rb = enemyObj.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            // 6. Configure NavMeshAgent & AudioSource
            UnityEngine.AI.NavMeshAgent navAgent = enemyObj.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (navAgent == null) navAgent = enemyObj.AddComponent<UnityEngine.AI.NavMeshAgent>();
            navAgent.baseOffset = -0.05f;
            navAgent.speed = 0.8f;
            navAgent.stoppingDistance = 1.2f;
            navAgent.radius = 0.4f;
            navAgent.height = 1.8f;

            AudioSource audioSource = enemyObj.GetComponent<AudioSource>();
            if (audioSource == null) audioSource = enemyObj.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1.0f;

            // 7. Configure Emerald AI System settings
            emeraldAI.WalkSpeed = 0.8f;
            emeraldAI.RunSpeed = 2.0f;
            emeraldAI.WalkAnimationSpeed = 2.0f;
            emeraldAI.RunAnimationSpeed = 1.5f;
            emeraldAI.AlignAIWithGroundRef = EmeraldAISystem.YesOrNo.No;
            emeraldAI.BehaviorRef = EmeraldAISystem.CurrentBehavior.Aggressive;
            emeraldAI.ConfidenceRef = EmeraldAISystem.ConfidenceType.Brave;
            emeraldAI.PlayerTag = "Player";

            if (emeraldAI.PlayerFaction == null) emeraldAI.PlayerFaction = new System.Collections.Generic.List<EmeraldAISystem.PlayerFactionClass>();
            emeraldAI.PlayerFaction.Clear();
            emeraldAI.PlayerFaction.Add(new EmeraldAISystem.PlayerFactionClass("Player", 0));

            if (emeraldAI.FactionRelationsList == null) emeraldAI.FactionRelationsList = new System.Collections.Generic.List<EmeraldAISystem.FactionsList>();
            emeraldAI.FactionRelationsList.Clear();
            emeraldAI.FactionRelationsList.Add(new EmeraldAISystem.FactionsList(0, 0));

            if (emeraldAI.FactionRelations == null) emeraldAI.FactionRelations = new System.Collections.Generic.List<int>();
            emeraldAI.FactionRelations.Clear();
            emeraldAI.FactionRelations.Add(0);

            if (emeraldAI.AIFactionsList == null) emeraldAI.AIFactionsList = new System.Collections.Generic.List<int>();
            emeraldAI.AIFactionsList.Clear();
            emeraldAI.AIFactionsList.Add(0);

            emeraldAI.DetectionLayerMask = ~0;
            emeraldAI.fieldOfViewAngle = 360;
            emeraldAI.DetectionRadius = 35;
            emeraldAI.MeleeAttackDistance = 1.8f;
            emeraldAI.AttackDistance = 1.8f;
            emeraldAI.StartingHealth = 100;
            emeraldAI.CurrentHealth = 100;

            if (emeraldAI.MeleeAttacks == null) emeraldAI.MeleeAttacks = new System.Collections.Generic.List<EmeraldAISystem.MeleeAttackClass>();
            if (emeraldAI.MeleeAttacks.Count == 0)
            {
                emeraldAI.MeleeAttacks.Add(new EmeraldAISystem.MeleeAttackClass { AttackAnimaition = 1, MinDamage = 15, MaxDamage = 25, AttackOdds = 100 });
            }

            // Assign Rig-Compatible Animation Clips from Military Set
            AnimationClip idleClip    = LoadAnimationClip("Assets/Military/Animations/Idle.fbx")
                                     ?? LoadAnimationClip("Assets/Survivalist/StarterAssets/ThirdPersonController/Character/Animations/Stand--Idle.anim.fbx");
            AnimationClip walkClip    = LoadAnimationClip("Assets/Military/Animations/Walk_F.FBX")
                                     ?? LoadAnimationClip("Assets/Survivalist/StarterAssets/ThirdPersonController/Character/Animations/Locomotion--Walk_N.anim.fbx");
            AnimationClip runClip     = LoadAnimationClip("Assets/Military/Animations/Run.FBX")
                                     ?? LoadAnimationClip("Assets/Survivalist/StarterAssets/ThirdPersonController/Character/Animations/Locomotion--Run_N.anim.fbx");
            AnimationClip strafeLClip = LoadAnimationClip("Assets/Military/Animations/Strafe_L.FBX") ?? walkClip;
            AnimationClip strafeRClip = LoadAnimationClip("Assets/Military/Animations/Strafe_R.FBX") ?? walkClip;
            AnimationClip recoilClip  = LoadAnimationClip("Assets/Military/Animations/Recoil_Rifle.fbx") ?? walkClip;
            AnimationClip punchClip   = LoadAnimationClip("Assets/Military/Animations/Punch.fbx") ?? walkClip;

            AnimationClip death1Clip  = LoadAnimationClip("Assets/Military/Animations/Death.fbx")
                                     ?? LoadAnimationClip("Assets/PREFAB AND MODELS/animmatyuiodewk/Death From The Front.fbx");
            AnimationClip death2Clip  = LoadAnimationClip("Assets/PREFAB AND MODELS/animmatyuiodewk/Standing Death Backward 01.fbx");
            AnimationClip death3Clip  = LoadAnimationClip("Assets/PREFAB AND MODELS/animmatyuiodewk/Death From The Back.fbx");
            AnimationClip death4Clip  = LoadAnimationClip("Assets/PREFAB AND MODELS/animmatyuiodewk/Death From Right.fbx");

            // Assign Idles
            if (idleClip != null)
            {
                emeraldAI.NonCombatIdleAnimation = idleClip;
                emeraldAI.CombatIdleAnimation = idleClip;
                emeraldAI.Idle1Animation = idleClip;
                if (emeraldAI.IdleAnimationList == null) emeraldAI.IdleAnimationList = new System.Collections.Generic.List<EmeraldAISystem.AnimationClass>();
                emeraldAI.IdleAnimationList.Clear();
                emeraldAI.IdleAnimationList.Add(new EmeraldAISystem.AnimationClass(1.0f, idleClip));
            }

            // Assign Locomotion & Turns
            if (walkClip != null)
            {
                emeraldAI.WalkStraightAnimation = walkClip;
                emeraldAI.CombatWalkStraightAnimation = walkClip;
                emeraldAI.WalkLeftAnimation = strafeLClip;
                emeraldAI.WalkRightAnimation = strafeRClip;
                emeraldAI.CombatWalkLeftAnimation = strafeLClip;
                emeraldAI.CombatWalkRightAnimation = strafeRClip;
                emeraldAI.NonCombatTurnLeftAnimation = strafeLClip;
                emeraldAI.NonCombatTurnRightAnimation = strafeRClip;
                emeraldAI.CombatTurnLeftAnimation = strafeLClip;
                emeraldAI.CombatTurnRightAnimation = strafeRClip;
            }

            if (runClip != null)
            {
                emeraldAI.RunStraightAnimation = runClip;
                emeraldAI.CombatRunStraightAnimation = runClip;
                emeraldAI.RunLeftAnimation = runClip;
                emeraldAI.RunRightAnimation = runClip;
                emeraldAI.CombatRunLeftAnimation = runClip;
                emeraldAI.CombatRunRightAnimation = runClip;
            }

            // Assign Attacks
            if (emeraldAI.AttackAnimationList == null) emeraldAI.AttackAnimationList = new System.Collections.Generic.List<EmeraldAISystem.AnimationClass>();
            emeraldAI.AttackAnimationList.Clear();
            if (recoilClip != null) emeraldAI.AttackAnimationList.Add(new EmeraldAISystem.AnimationClass(1.0f, recoilClip));
            if (punchClip != null) emeraldAI.AttackAnimationList.Add(new EmeraldAISystem.AnimationClass(1.0f, punchClip));

            // Assign Hits
            if (emeraldAI.HitAnimationList == null) emeraldAI.HitAnimationList = new System.Collections.Generic.List<EmeraldAISystem.AnimationClass>();
            emeraldAI.HitAnimationList.Clear();
            if (recoilClip != null) emeraldAI.HitAnimationList.Add(new EmeraldAISystem.AnimationClass(1.0f, recoilClip));

            if (emeraldAI.CombatHitAnimationList == null) emeraldAI.CombatHitAnimationList = new System.Collections.Generic.List<EmeraldAISystem.AnimationClass>();
            emeraldAI.CombatHitAnimationList.Clear();
            if (recoilClip != null) emeraldAI.CombatHitAnimationList.Add(new EmeraldAISystem.AnimationClass(1.0f, recoilClip));

            // Assign Deaths
            if (emeraldAI.DeathAnimationList == null) emeraldAI.DeathAnimationList = new System.Collections.Generic.List<EmeraldAISystem.AnimationClass>();
            emeraldAI.DeathAnimationList.Clear();

            if (death1Clip != null) { emeraldAI.Death1Animation = death1Clip; emeraldAI.DeathAnimationList.Add(new EmeraldAISystem.AnimationClass(1.0f, death1Clip)); }
            if (death2Clip != null) { emeraldAI.Death2Animation = death2Clip; emeraldAI.DeathAnimationList.Add(new EmeraldAISystem.AnimationClass(1.0f, death2Clip)); }
            if (death3Clip != null) { emeraldAI.Death3Animation = death3Clip; emeraldAI.DeathAnimationList.Add(new EmeraldAISystem.AnimationClass(1.0f, death3Clip)); }
            if (death4Clip != null) { emeraldAI.Death4Animation = death4Clip; emeraldAI.DeathAnimationList.Add(new EmeraldAISystem.AnimationClass(1.0f, death4Clip)); }

            emeraldAI.TotalDeathAnimations = emeraldAI.DeathAnimationList.Count;
            emeraldAI.DeathTypeRef = EmeraldAISystem.DeathType.Animation;

            // Generate Emerald AI blend trees and controller
            EmeraldAIAnimatorGenerator.GenerateAnimatorController(emeraldAI);

            foreach (Transform child in enemyObj.GetComponentsInChildren<Transform>())
            {
                if (child.name.Equals("Head", System.StringComparison.OrdinalIgnoreCase) || child.name.Contains("Head"))
                {
                    emeraldAI.HeadTransform = child;
                    break;
                }
            }

            // 7. Add Netcode Components
            NetworkObject netObj = enemyObj.GetComponent<NetworkObject>();
            if (netObj == null) netObj = enemyObj.AddComponent<NetworkObject>();

            NetworkTransform netTransform = enemyObj.GetComponent<NetworkTransform>();
            if (netTransform == null) netTransform = enemyObj.AddComponent<NetworkTransform>();

            NetworkAnimator netAnimator = enemyObj.GetComponent<NetworkAnimator>();
            if (netAnimator == null) netAnimator = enemyObj.AddComponent<NetworkAnimator>();
            if (emeraldAI.AIAnimator != null)
            {
                netAnimator.Animator = emeraldAI.AIAnimator;
            }

            EmeraldAINetworkBridge networkBridge = enemyObj.GetComponent<EmeraldAINetworkBridge>();
            if (networkBridge == null) enemyObj.AddComponent<EmeraldAINetworkBridge>();

            // 8. Save Prefab to Assets/VRMPAssets/Prefabs/ and Assets/VRMPAssets/Resources/
            string prefabFolder = "Assets/VRMPAssets/Prefabs";
            string resourcesFolder = "Assets/VRMPAssets/Resources";
            if (!AssetDatabase.IsValidFolder(prefabFolder)) System.IO.Directory.CreateDirectory(prefabFolder);
            if (!AssetDatabase.IsValidFolder(resourcesFolder)) System.IO.Directory.CreateDirectory(resourcesFolder);
            AssetDatabase.Refresh();

            string savePath = $"{prefabFolder}/NetworkEmeraldSurvivalistEnemy.prefab";
            string resSavePath = $"{resourcesFolder}/NetworkEmeraldSurvivalistEnemy.prefab";

            bool success;
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(enemyObj, savePath, out success);
            if (success)
            {
                PrefabUtility.SaveAsPrefabAsset(enemyObj, resSavePath);
            }
            Object.DestroyImmediate(enemyObj);

            if (success)
            {
                // Register in NetworkManager instances in active scene
                var networkManagers = Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None);
                foreach (var nm in networkManagers)
                {
                    if (nm != null && nm.NetworkConfig != null && nm.NetworkConfig.Prefabs != null)
                    {
                        if (savedPrefab != null && !nm.NetworkConfig.Prefabs.Contains(savedPrefab))
                        {
                            nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = savedPrefab });
                            EditorUtility.SetDirty(nm);
                            Debug.Log($"[EmeraldAISurvivalistSetup] Registered '{savedPrefab.name}' in NetworkManager on '{nm.gameObject.name}'.");
                        }
                    }
                }

                Debug.Log($"[EmeraldAISurvivalistSetup] Successfully created networked enemy prefab at: '{savePath}' and '{resSavePath}'");
                EditorUtility.DisplayDialog("Emerald AI Setup", $"Successfully created Networked Emerald AI Enemy Prefab at:\n{savePath}\nand registered in NetworkManager!", "OK");
            }
            else
            {
                Debug.LogError($"[EmeraldAISurvivalistSetup] Failed to save prefab at: '{savePath}'");
            }
        }

        private static AnimationClip LoadAnimationClip(string path)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (assets != null)
            {
                foreach (Object asset in assets)
                {
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    {
                        return clip;
                    }
                }
            }
            return null;
        }
    }
}
