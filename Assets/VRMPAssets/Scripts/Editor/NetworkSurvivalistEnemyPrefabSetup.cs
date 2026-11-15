#if UNITY_EDITOR
using UnityEditor;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;
using XRMultiplayer;

namespace XRMultiplayer.EditorTools
{
    public static class NetworkSurvivalistEnemyPrefabSetup
    {
        private const string FbxPath = "Assets/Survivalist/Basemesh/SK_Military_Survivalist.fbx";
        private const string ControllerPath = "Assets/Survivalist/StarterAssets/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
        private const string TargetPrefabPath = "Assets/Survivalist/Prefab/NetworkSurvivalistEnemy.prefab";

        [MenuItem("Tools/VRMP/Create Networked Survivalist AI Enemy Prefab")]
        public static void CreateEnemyPrefab()
        {
            // 1. Load FBX asset
            GameObject fbxModel = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbxModel == null)
            {
                Debug.LogError($"[EnemyPrefabSetup] Could not find FBX at '{FbxPath}'");
                return;
            }

            // 2. Instantiate temporary GameObject in scene
            GameObject enemyObj = (GameObject)PrefabUtility.InstantiatePrefab(fbxModel);
            enemyObj.name = "NetworkSurvivalistEnemy";

            // 3. Configure Animator
            Animator animator = enemyObj.GetComponent<Animator>();
            if (animator == null)
            {
                animator = enemyObj.AddComponent<Animator>();
            }
            animator.applyRootMotion = false; // Disable root motion so NavMeshAgent handles movement cleanly

            RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
            }

            // 4. Add CapsuleCollider
            CapsuleCollider collider = enemyObj.GetComponent<CapsuleCollider>();
            if (collider == null)
            {
                collider = enemyObj.AddComponent<CapsuleCollider>();
            }
            collider.center = new Vector3(0f, 0.9f, 0f);
            collider.height = 1.8f;
            collider.radius = 0.35f;

            // Add Rigidbody (Must be isKinematic = true so physics doesn't conflict with NavMeshAgent)
            Rigidbody rb = enemyObj.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = enemyObj.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
            rb.useGravity = false;

            // 5. Add NavMeshAgent
            NavMeshAgent navAgent = enemyObj.GetComponent<NavMeshAgent>();
            if (navAgent == null)
            {
                navAgent = enemyObj.AddComponent<NavMeshAgent>();
            }
            navAgent.speed = 3.5f;
            navAgent.angularSpeed = 120f;
            navAgent.acceleration = 8f;
            navAgent.stoppingDistance = 1.5f;
            navAgent.radius = 0.35f;
            navAgent.height = 1.8f;
            navAgent.baseOffset = -0.05f; // Ground level offset

            // 6. Add NetworkObject
            NetworkObject netObj = enemyObj.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                netObj = enemyObj.AddComponent<NetworkObject>();
            }

            // 7. Add NetworkTransform (Sync position & rotation across network)
            NetworkTransform netTransform = enemyObj.GetComponent<NetworkTransform>();
            if (netTransform == null)
            {
                netTransform = enemyObj.AddComponent<NetworkTransform>();
            }

            // 8. Add NetworkAnimator
            NetworkAnimator netAnimator = enemyObj.GetComponent<NetworkAnimator>();
            if (netAnimator == null)
            {
                netAnimator = enemyObj.AddComponent<NetworkAnimator>();
            }
            netAnimator.Animator = animator;

            // 8. Add NetworkEnemyHealth
            NetworkEnemyHealth health = enemyObj.GetComponent<NetworkEnemyHealth>();
            if (health == null)
            {
                health = enemyObj.AddComponent<NetworkEnemyHealth>();
            }

            // 9. Add NetworkEnemyAI
            NetworkEnemyAI ai = enemyObj.GetComponent<NetworkEnemyAI>();
            if (ai == null)
            {
                ai = enemyObj.AddComponent<NetworkEnemyAI>();
            }

            // 10. Save as Prefab
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(enemyObj, TargetPrefabPath);
            Object.DestroyImmediate(enemyObj);

            if (savedPrefab != null)
            {
                Debug.Log($"[EnemyPrefabSetup] Successfully created networked AI enemy prefab at '{TargetPrefabPath}'!");
                Selection.activeObject = savedPrefab;

                // 11. Register in NetworkPrefabsList asset
                string listPath = "Assets/VRMPAssets/Prefabs/NetworkedPrefabs/Kinematic/_KinematicNetworkPrefabsList.asset";
                NetworkPrefabsList prefabsList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(listPath);
                if (prefabsList != null)
                {
                    if (!prefabsList.Contains(savedPrefab))
                    {
                        prefabsList.Add(new NetworkPrefab { Prefab = savedPrefab });
                        EditorUtility.SetDirty(prefabsList);
                        AssetDatabase.SaveAssets();
                        Debug.Log($"[EnemyPrefabSetup] Successfully registered '{savedPrefab.name}' in NetworkPrefabsList at '{listPath}'!");
                    }
                }
            }
            else
            {
                Debug.LogError($"[EnemyPrefabSetup] Failed to save prefab at '{TargetPrefabPath}'");
            }
        }
    }
}
#endif
