#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Unity.Netcode;
using System.IO;

namespace XRMultiplayer
{
    public class GridSetupWizard : EditorWindow
    {
        [MenuItem("Tools/Grid Builder Wizard")]
        public static void ShowWindow()
        {
            GetWindow<GridSetupWizard>("Grid Builder Wizard");
        }

        private void OnGUI()
        {
            GUILayout.Label("Tactical Killhouse Builder Setup", EditorStyles.boldLabel);
            GUILayout.Space(10);
            GUILayout.Label("This wizard will automatically:\n" +
                            "1. Create modular prefabs (Wall, Window, Door, Watchtower, Container).\n" +
                            "2. Add NetworkObject components and register them with Netcode.\n" +
                            "3. Setup the floor, boundary walls, and Manager components in your scene.", EditorStyles.wordWrappedLabel);
            GUILayout.Space(20);

            if (GUILayout.Button("Generate & Setup Hangar Scene", GUILayout.Height(40)))
            {
                RunSetup();
            }
        }

        private static void RunSetup()
        {
            // 1. Create target prefabs directory
            string folderPath = "Assets/VRMPAssets/Prefabs/GridObjects";
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
                AssetDatabase.Refresh();
            }

            // Create basic materials
            Material wallMat = GetOrCreateMaterial("WallMaterial", Color.grey);
            Material glassMat = GetOrCreateMaterial("GlassMaterial", new Color(0.7f, 0.9f, 1f, 0.4f), true);
            Material containerMat = GetOrCreateMaterial("ContainerMaterial", Color.red);
            Material concreteMat = GetOrCreateMaterial("ConcreteMaterial", Color.white);
            Material sandbagMat = GetOrCreateMaterial("SandbagMaterial", new Color(0.8f, 0.7f, 0.5f, 1.0f));

            // Generate Prefabs
            GameObject wallPrefab = CreateWallPrefab(folderPath, wallMat);
            GameObject windowPrefab = CreateWindowPrefab(folderPath, wallMat, glassMat);
            GameObject doorPrefab = CreateDoorPrefab(folderPath, wallMat);
            GameObject watchtowerPrefab = CreateWatchtowerPrefab(folderPath, wallMat);
            GameObject containerPrefab = CreateContainerPrefab(folderPath, containerMat);
            GameObject concretePrefab = CreateConcretePrefab(folderPath, concreteMat);
            GameObject sandbagPrefab = CreateSandbagPrefab(folderPath, sandbagMat);

            // Register in Netcode NetworkManager
            NetworkManager netManager = FindFirstObjectByType<NetworkManager>();
            if (netManager != null)
            {
                AddPrefabToNetcode(netManager, wallPrefab);
                AddPrefabToNetcode(netManager, windowPrefab);
                AddPrefabToNetcode(netManager, doorPrefab);
                AddPrefabToNetcode(netManager, watchtowerPrefab);
                AddPrefabToNetcode(netManager, containerPrefab);
                AddPrefabToNetcode(netManager, concretePrefab);
                AddPrefabToNetcode(netManager, sandbagPrefab);
                EditorUtility.SetDirty(netManager);
            }
            else
            {
                Debug.LogWarning("GridSetupWizard: NetworkManager not found in scene! Please open a scene containing a NetworkManager first.");
                return;
            }

            // 2. Setup Scene Boundary GameObjects
            GameObject floor = GameObject.Find("Hangar Floor");
            if (floor == null)
            {
                floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "Hangar Floor";
                floor.transform.position = Vector3.zero;
            }

            GameObject northWall = GameObject.Find("North Wall");
            if (northWall == null)
            {
                northWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                northWall.name = "North Wall";
            }

            GameObject southWall = GameObject.Find("South Wall");
            if (southWall == null)
            {
                southWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                southWall.name = "South Wall";
            }

            GameObject eastWall = GameObject.Find("East Wall");
            if (eastWall == null)
            {
                eastWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                eastWall.name = "East Wall";
            }

            GameObject westWall = GameObject.Find("West Wall");
            if (westWall == null)
            {
                westWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                westWall.name = "West Wall";
            }

            // Setup GridMapManager
            GridMapManager mapManager = FindFirstObjectByType<GridMapManager>();
            if (mapManager == null)
            {
                GameObject mgrObj = new GameObject("GridMapManager");
                mapManager = mgrObj.AddComponent<GridMapManager>();
                mgrObj.AddComponent<NetworkObject>();
            }

            // Assign boundary references in GridMapManager using Reflection/Properties
            var serializedManager = new SerializedObject(mapManager);
            serializedManager.FindProperty("m_HangarFloor").objectReferenceValue = floor;
            serializedManager.FindProperty("m_NorthWall").objectReferenceValue = northWall;
            serializedManager.FindProperty("m_SouthWall").objectReferenceValue = southWall;
            serializedManager.FindProperty("m_EastWall").objectReferenceValue = eastWall;
            serializedManager.FindProperty("m_WestWall").objectReferenceValue = westWall;

            // Populate placeable prefabs
            SerializedProperty prefabsProp = serializedManager.FindProperty("m_PlaceablePrefabs");
            prefabsProp.ClearArray();
            AddToArray(prefabsProp, wallPrefab);
            AddToArray(prefabsProp, windowPrefab);
            AddToArray(prefabsProp, doorPrefab);
            AddToArray(prefabsProp, watchtowerPrefab);
            AddToArray(prefabsProp, containerPrefab);
            AddToArray(prefabsProp, concretePrefab);
            AddToArray(prefabsProp, sandbagPrefab);
            serializedManager.ApplyModifiedProperties();

            // Setup ExConGridEditor on Camera
            Camera editorCam = Camera.main;
            if (editorCam != null)
            {
                ExConGridEditor gridEditor = editorCam.GetComponent<ExConGridEditor>();
                if (gridEditor == null)
                {
                    gridEditor = editorCam.gameObject.AddComponent<ExConGridEditor>();
                }

                var serializedEditor = new SerializedObject(gridEditor);
                serializedEditor.FindProperty("m_EditorCamera").objectReferenceValue = editorCam;

                // Set Ghost Prefabs (we can reuse the same prefabs, but we disable colliders at runtime anyway)
                SerializedProperty ghostsProp = serializedEditor.FindProperty("m_GhostPrefabs");
                ghostsProp.ClearArray();
                AddToArray(ghostsProp, wallPrefab);
                AddToArray(ghostsProp, windowPrefab);
                AddToArray(ghostsProp, doorPrefab);
                AddToArray(ghostsProp, watchtowerPrefab);
                AddToArray(ghostsProp, containerPrefab);
                AddToArray(ghostsProp, concretePrefab);
                AddToArray(ghostsProp, sandbagPrefab);
                serializedEditor.ApplyModifiedProperties();
            }

            AssetDatabase.SaveAssets();
            Debug.Log("GridSetupWizard: Setup complete! All modular prefabs and boundaries generated and configured.");
        }

        private static Material GetOrCreateMaterial(string name, Color color, bool isTransparent = false)
        {
            string path = $"Assets/VRMPAssets/Prefabs/GridObjects/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = (mat == null);
            if (isNew)
            {
                Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
                if (urpShader != null)
                {
                    mat = new Material(urpShader);
                }
                else
                {
                    mat = new Material(Shader.Find("Standard"));
                }
            }

            // Always upgrade the shader if it's currently broken/pink
            if (mat.shader.name == "Standard" || mat.shader.name == "Hidden/InternalErrorShader" || mat.shader.name.Contains("Legacy"))
            {
                Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
                if (urpShader != null)
                {
                    mat.shader = urpShader;
                }
            }

            mat.color = color;

            if (isTransparent && mat.shader.name.Contains("Universal Render Pipeline/Lit"))
            {
                mat.SetFloat("_Surface", 1.0f); // Transparent
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            if (isNew)
            {
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                EditorUtility.SetDirty(mat);
            }

            return mat;
        }

        private static GameObject CreateWallPrefab(string folder, Material mat)
        {
            string path = $"{folder}/SolidWallPrefab.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            temp.transform.localScale = new Vector3(3.0f, 3.0f, 0.2f);
            temp.GetComponent<Renderer>().material = mat;
            temp.AddComponent<NetworkObject>();
            temp.AddComponent<GridObjectState>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
            DestroyImmediate(temp);
            return prefab;
        }

        private static GameObject CreateWindowPrefab(string folder, Material wallMat, Material glassMat)
        {
            string path = $"{folder}/WindowPrefab.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            GameObject root = new GameObject("WindowPrefab");

            // Left post
            GameObject left = GameObject.CreatePrimitive(PrimitiveType.Cube);
            left.transform.parent = root.transform;
            left.transform.localPosition = new Vector3(-1.25f, 1.5f, 0f);
            left.transform.localScale = new Vector3(0.5f, 3f, 0.2f);
            left.GetComponent<Renderer>().material = wallMat;

            // Right post
            GameObject right = GameObject.CreatePrimitive(PrimitiveType.Cube);
            right.transform.parent = root.transform;
            right.transform.localPosition = new Vector3(1.25f, 1.5f, 0f);
            right.transform.localScale = new Vector3(0.5f, 3f, 0.2f);
            right.GetComponent<Renderer>().material = wallMat;

            // Top header
            GameObject top = GameObject.CreatePrimitive(PrimitiveType.Cube);
            top.transform.parent = root.transform;
            top.transform.localPosition = new Vector3(0f, 2.75f, 0f);
            top.transform.localScale = new Vector3(2f, 0.5f, 0.2f);
            top.GetComponent<Renderer>().material = wallMat;

            // Bottom footer
            GameObject bottom = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bottom.transform.parent = root.transform;
            bottom.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            bottom.transform.localScale = new Vector3(2f, 1f, 0.2f);
            bottom.GetComponent<Renderer>().material = wallMat;

            // Glass pane
            GameObject glass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            glass.transform.parent = root.transform;
            glass.transform.localPosition = new Vector3(0f, 1.75f, 0f);
            glass.transform.localScale = new Vector3(2f, 1.5f, 0.05f);
            glass.GetComponent<Renderer>().material = glassMat;

            root.AddComponent<NetworkObject>();
            root.AddComponent<GridObjectState>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            DestroyImmediate(root);
            DestroyImmediate(left);
            DestroyImmediate(right);
            DestroyImmediate(top);
            DestroyImmediate(bottom);
            DestroyImmediate(glass);
            return prefab;
        }

        private static GameObject CreateDoorPrefab(string folder, Material mat)
        {
            string path = $"{folder}/DoorPrefab.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            GameObject root = new GameObject("DoorPrefab");

            // Left jamb
            GameObject left = GameObject.CreatePrimitive(PrimitiveType.Cube);
            left.transform.parent = root.transform;
            left.transform.localPosition = new Vector3(-1.0f, 1.5f, 0f);
            left.transform.localScale = new Vector3(0.5f, 3f, 0.2f);
            left.GetComponent<Renderer>().material = mat;

            // Right jamb
            GameObject right = GameObject.CreatePrimitive(PrimitiveType.Cube);
            right.transform.parent = root.transform;
            right.transform.localPosition = new Vector3(1.0f, 1.5f, 0f);
            right.transform.localScale = new Vector3(0.5f, 3f, 0.2f);
            right.GetComponent<Renderer>().material = mat;

            // Top lintel
            GameObject top = GameObject.CreatePrimitive(PrimitiveType.Cube);
            top.transform.parent = root.transform;
            top.transform.localPosition = new Vector3(0f, 2.75f, 0f);
            top.transform.localScale = new Vector3(1.5f, 0.5f, 0.2f);
            top.GetComponent<Renderer>().material = mat;

            root.AddComponent<NetworkObject>();
            root.AddComponent<GridObjectState>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            DestroyImmediate(root);
            DestroyImmediate(left);
            DestroyImmediate(right);
            DestroyImmediate(top);
            return prefab;
        }

        private static GameObject CreateWatchtowerPrefab(string folder, Material mat)
        {
            string path = $"{folder}/WatchtowerPrefab.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            GameObject root = new GameObject("WatchtowerPrefab");

            float[] xs = { -1.4f, 1.4f };
            float[] zs = { -1.4f, 1.4f };
            foreach (float x in xs)
            {
                foreach (float z in zs)
                {
                    GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    post.transform.parent = root.transform;
                    post.transform.localPosition = new Vector3(x, 2.5f, z);
                    post.transform.localScale = new Vector3(0.15f, 5.0f, 0.15f);
                    post.GetComponent<Renderer>().material = mat;
                }
            }

            GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.transform.parent = root.transform;
            platform.transform.localPosition = new Vector3(0f, 5.0f, 0f);
            platform.transform.localScale = new Vector3(3.0f, 0.2f, 3.0f);
            platform.GetComponent<Renderer>().material = mat;

            root.AddComponent<NetworkObject>();
            root.AddComponent<GridObjectState>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            DestroyImmediate(root);
            DestroyImmediate(platform);
            return prefab;
        }

        private static GameObject CreateContainerPrefab(string folder, Material mat)
        {
            string path = $"{folder}/ContainerPrefab.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            temp.transform.localScale = new Vector3(3.0f, 3.0f, 6.0f);
            temp.GetComponent<Renderer>().material = mat;
            temp.AddComponent<NetworkObject>();
            temp.AddComponent<GridObjectState>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
            DestroyImmediate(temp);
            return prefab;
        }

        private static GameObject CreateConcretePrefab(string folder, Material mat)
        {
            string path = $"{folder}/ConcreteBarrierPrefab.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            temp.transform.localScale = new Vector3(3.0f, 1.0f, 0.6f);
            temp.GetComponent<Renderer>().material = mat;
            temp.AddComponent<NetworkObject>();
            temp.AddComponent<GridObjectState>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
            DestroyImmediate(temp);
            return prefab;
        }

        private static GameObject CreateSandbagPrefab(string folder, Material mat)
        {
            string path = $"{folder}/SandbagsPrefab.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            temp.transform.localScale = new Vector3(3.0f, 1.2f, 0.8f);
            temp.GetComponent<Renderer>().material = mat;
            temp.AddComponent<NetworkObject>();
            temp.AddComponent<GridObjectState>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
            DestroyImmediate(temp);
            return prefab;
        }

        private static void AddPrefabToNetcode(NetworkManager netManager, GameObject prefab)
        {
            var prefabs = netManager.NetworkConfig.Prefabs;
            if (!prefabs.Contains(prefab))
            {
                prefabs.Add(new NetworkPrefab { Prefab = prefab });
            }
        }

        private static void AddToArray(SerializedProperty listProp, GameObject obj)
        {
            int index = listProp.arraySize;
            listProp.InsertArrayElementAtIndex(index);
            listProp.GetArrayElementAtIndex(index).objectReferenceValue = obj;
        }
    }
}
#endif
