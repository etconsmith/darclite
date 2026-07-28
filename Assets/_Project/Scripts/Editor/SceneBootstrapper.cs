using UnityEditor;
using UnityEngine;

namespace Darclite.EditorTools
{
    public static class SceneBootstrapper
    {
        private const string FloorMaterialPath = "Assets/_Project/Materials/Floor.mat";

        [MenuItem("Darclite/Create Floor")]
        public static void CreateFloor()
        {
            GameObject existing = GameObject.Find("Floor");
            if (existing != null)
            {
                existing.GetComponent<Renderer>().sharedMaterial = GetOrCreateFloorMaterial();
                Selection.activeGameObject = existing;
                Debug.Log("A Floor already exists in the scene — refreshed its material and selected it instead of creating a duplicate.");
                return;
            }

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(50f, 1f, 50f);

            Renderer renderer = floor.GetComponent<Renderer>();
            renderer.sharedMaterial = GetOrCreateFloorMaterial();

            Undo.RegisterCreatedObjectUndo(floor, "Create Floor");
            Selection.activeGameObject = floor;
        }

        private static Material GetOrCreateFloorMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, FloorMaterialPath);
            }

            Shader toonShader = Shader.Find("Darclite/ToonLit");
            if (toonShader != null)
            {
                material.shader = toonShader;
            }

            material.SetColor("_BaseColor", new Color(0.35f, 0.55f, 0.3f));
            // The floor is a Cube scaled 50x on X/Z; outline extrusion is in object space,
            // so its width must be scaled down to match the character's world-space outline thickness.
            material.SetFloat("_OutlineWidth", 0.02f / 50f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        private const string WarriorModelPath = "Assets/_Project/Art/Characters/RPGCharacterPack/Models/Warrior.fbx";
        private const string PlayerControllerPath = "Assets/_Project/Animations/PlayerAnimatorController.controller";

        [MenuItem("Darclite/Setup Player Character")]
        public static void SetupPlayerCharacter()
        {
            GameObject player = GameObject.Find("Player");
            if (player == null)
            {
                Debug.LogError("No 'Player' GameObject found in the scene. Create one with the Third Person Player Controller first.");
                return;
            }

            foreach (MeshRenderer mr in player.GetComponents<MeshRenderer>()) Object.DestroyImmediate(mr);
            foreach (MeshFilter mf in player.GetComponents<MeshFilter>()) Object.DestroyImmediate(mf);
            foreach (CapsuleCollider cc in player.GetComponents<CapsuleCollider>()) Object.DestroyImmediate(cc);
            foreach (BoxCollider bc in player.GetComponents<BoxCollider>()) Object.DestroyImmediate(bc);

            Transform existingModel = player.transform.Find("Model");
            if (existingModel != null)
            {
                Object.DestroyImmediate(existingModel.gameObject);
            }

            GameObject warriorAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WarriorModelPath);
            if (warriorAsset == null)
            {
                Debug.LogError($"Could not find Warrior model at {WarriorModelPath}");
                return;
            }

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(warriorAsset, player.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            ApplyCharacterMaterial(model, "Warrior");

            Animator animator = model.GetComponent<Animator>();
            if (animator == null)
            {
                animator = model.AddComponent<Animator>();
            }
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerControllerPath);
            animator.applyRootMotion = false;

            Bounds bounds = CalculateBounds(model);
            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null && bounds.size.y > 0f)
            {
                controller.height = bounds.size.y;
                controller.center = new Vector3(0f, bounds.size.y * 0.5f, 0f);
                controller.radius = Mathf.Clamp(Mathf.Min(bounds.extents.x, bounds.extents.z), 0.2f, 0.6f);
            }

            Selection.activeGameObject = player;
            Debug.Log("Player character spawned and wired up.");
        }

        private const string CharacterTexturesFolder = "Assets/_Project/Art/Characters/RPGCharacterPack/Textures";
        private const string CharacterMaterialsFolder = "Assets/_Project/Materials/Characters";

        private static void ApplyCharacterMaterial(GameObject model, string characterName)
        {
            string texturePath = $"{CharacterTexturesFolder}/{characterName}_Texture.png";
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find texture at {texturePath}; leaving default material on '{characterName}'.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(CharacterMaterialsFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Materials", "Characters");
            }

            string materialPath = $"{CharacterMaterialsFolder}/{characterName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }

            Shader toonShader = Shader.Find("Darclite/ToonLit");
            if (toonShader != null)
            {
                material.shader = toonShader;
            }

            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();

            // The outline shader extrudes in object space, but this rig bakes a large uniform
            // scale (e.g. 100x) into the hierarchy above the mesh to reach real-world size.
            // Compensate so the outline reads as a consistent real-world thickness.
            const float desiredWorldOutlineThickness = 0.015f;
            float lossyScale = renderers.Length > 0 ? renderers[0].transform.lossyScale.x : 1f;
            float objectSpaceOutlineWidth = lossyScale > 0f ? desiredWorldOutlineThickness / lossyScale : desiredWorldOutlineThickness;
            material.SetFloat("_OutlineWidth", objectSpaceOutlineWidth);

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();

            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.zero);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }
    }
}
