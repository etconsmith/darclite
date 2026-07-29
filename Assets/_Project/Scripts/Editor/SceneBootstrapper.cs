using Darclite.Combat;
using Darclite.Enemies;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

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

            Combatant combatant = player.GetComponent<Combatant>();
            if (combatant == null)
            {
                combatant = player.AddComponent<Combatant>();
            }
            ApplyCombatantTiming(combatant);

            PlayerCombat playerCombat = player.GetComponent<PlayerCombat>();
            if (playerCombat == null)
            {
                playerCombat = player.AddComponent<PlayerCombat>();
            }

            PopulateAttackDurations(player.GetComponent<AttackCombo>());
            SetupPlayerHealthUI(combatant);

            Selection.activeGameObject = player;
            Debug.Log("Player character spawned and wired up.");
        }

        private const string FightAnimationsFolder = "Assets/_Project/Animations/FightAnimations";

        private static readonly string[] LightAttackClipNames =
        {
            "BodyPunchLeft", "BodyPunchRight", "HeadPunchLeft", "HeadPunchRight",
            "BodyPunchLeft2", "BodyPunchRight2", "HeadPunchLeft2", "HeadPunchRight2"
        };

        private static readonly string[] HeavyAttackClipNames = { "HeadHeavyLeft", "HeadHeavyRight" };

        // Contact frame scrubbed by hand in Unity's clip preview for each punch (raw clip frame,
        // at the clip's own 30fps), in the same order as the *ClipNames arrays above.
        private const float ImpactClipFrameRate = 30f;
        private static readonly float[] LightImpactFrames = { 8f, 10f, 6f, 11f, 9f, 8f, 11f, 6f };
        private static readonly float[] HeavyImpactFrames = { 14f, 14f };

        // Doubled per follow-up request. Force-pushed here (rather than just changing Combatant's
        // script default) because Setup reuses an already-existing Combatant, which keeps whatever
        // value was serialized the first time it was added.
        private const float HitCooldownStunDuration = 1f;

        private static void ApplyCombatantTiming(Combatant combatant)
        {
            SerializedObject so = new SerializedObject(combatant);
            SerializedProperty stunProp = so.FindProperty("stunDuration");
            if (stunProp != null)
            {
                stunProp.floatValue = HitCooldownStunDuration;
            }
            so.ApplyModifiedProperties();
        }

        private static void PopulateAttackDurations(AttackCombo attackCombo)
        {
            if (attackCombo == null)
            {
                return;
            }

            SerializedObject so = new SerializedObject(attackCombo);

            SerializedProperty lightProp = so.FindProperty("lightAttackDurations");
            for (int i = 0; i < LightAttackClipNames.Length && i < lightProp.arraySize; i++)
            {
                lightProp.GetArrayElementAtIndex(i).floatValue = GetFightClipLength(LightAttackClipNames[i]) / AnimatorControllerBuilder.AttackSpeedMultiplier;
            }

            SerializedProperty heavyProp = so.FindProperty("heavyAttackDurations");
            for (int i = 0; i < HeavyAttackClipNames.Length && i < heavyProp.arraySize; i++)
            {
                heavyProp.GetArrayElementAtIndex(i).floatValue = GetFightClipLength(HeavyAttackClipNames[i]) / AnimatorControllerBuilder.AttackSpeedMultiplier;
            }

            SerializedProperty lightImpactProp = so.FindProperty("lightImpactDelays");
            for (int i = 0; i < LightImpactFrames.Length && i < lightImpactProp.arraySize; i++)
            {
                float rawImpactTime = LightImpactFrames[i] / ImpactClipFrameRate;
                lightImpactProp.GetArrayElementAtIndex(i).floatValue = rawImpactTime / AnimatorControllerBuilder.AttackSpeedMultiplier;
            }

            SerializedProperty heavyImpactProp = so.FindProperty("heavyImpactDelays");
            for (int i = 0; i < HeavyImpactFrames.Length && i < heavyImpactProp.arraySize; i++)
            {
                float rawImpactTime = HeavyImpactFrames[i] / ImpactClipFrameRate;
                heavyImpactProp.GetArrayElementAtIndex(i).floatValue = rawImpactTime / AnimatorControllerBuilder.AttackSpeedMultiplier;
            }

            so.ApplyModifiedProperties();
        }

        private static float GetFightClipLength(string clipName)
        {
            string path = $"{FightAnimationsFolder}/{clipName}.fbx";
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is AnimationClip clip && !clip.name.Contains("__preview__"))
                {
                    return clip.length;
                }
            }

            Debug.LogWarning($"[SceneBootstrapper] Could not find fight animation clip at {path}");
            return 0f;
        }

        private static void SetupPlayerHealthUI(Combatant combatant)
        {
            GameObject existingCanvas = GameObject.Find("PlayerHUD");
            if (existingCanvas != null)
            {
                Object.DestroyImmediate(existingCanvas);
            }

            GameObject canvasObject = new GameObject("PlayerHUD", typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            GameObject textObject = new GameObject("HealthText", typeof(Text));
            textObject.transform.SetParent(canvasObject.transform, false);

            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 36;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.text = combatant.CurrentHealth.ToString();

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(20f, -20f);
            rect.sizeDelta = new Vector2(200f, 50f);

            PlayerHealthUI healthUI = canvasObject.AddComponent<PlayerHealthUI>();
            SerializedObject so = new SerializedObject(healthUI);
            so.FindProperty("combatant").objectReferenceValue = combatant;
            so.FindProperty("healthText").objectReferenceValue = text;
            so.ApplyModifiedProperties();
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

        [MenuItem("Darclite/Bake NavMesh")]
        public static void BakeNavMesh()
        {
            GameObject floor = GameObject.Find("Floor");
            if (floor == null)
            {
                Debug.LogError("No 'Floor' GameObject found in the scene. Create one with Darclite/Create Floor first.");
                return;
            }

            NavMeshSurface surface = floor.GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                surface = floor.AddComponent<NavMeshSurface>();
            }

            surface.BuildNavMesh();
            Debug.Log("NavMesh baked from Floor.");
        }

        private const string EnemyModelPath = "Assets/_Project/Art/Characters/RPGCharacterPack/Models/Rogue.fbx";
        private const string EnemyModelCharacterName = "Rogue";
        private const string EnemyWeaponPath = "Assets/_Project/Art/Characters/RPGCharacterPack/Weapons/Cleric_Staff.fbx";
        private const string EnemyWeaponMaterialName = "Cleric_Staff";
        private const string WeaponSocketBoneName = "Weapon.R";

        [MenuItem("Darclite/Setup Enemy Character")]
        public static void SetupEnemyCharacter()
        {
            GameObject enemy = GameObject.Find("Enemy");
            if (enemy == null)
            {
                enemy = new GameObject("Enemy");
                enemy.transform.position = new Vector3(4f, 0f, 4f);
                Undo.RegisterCreatedObjectUndo(enemy, "Create Enemy");
            }

            Transform existingModel = enemy.transform.Find("Model");
            if (existingModel != null)
            {
                Object.DestroyImmediate(existingModel.gameObject);
            }

            GameObject enemyAsset = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyModelPath);
            if (enemyAsset == null)
            {
                Debug.LogError($"Could not find enemy model at {EnemyModelPath}");
                return;
            }

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(enemyAsset, enemy.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            ApplyCharacterMaterial(model, EnemyModelCharacterName);

            Animator animator = model.GetComponent<Animator>();
            if (animator == null)
            {
                animator = model.AddComponent<Animator>();
            }
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerControllerPath);
            animator.applyRootMotion = false;

            AttachWeapon(model, EnemyWeaponPath, EnemyWeaponMaterialName);

            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            if (agent == null)
            {
                agent = enemy.AddComponent<NavMeshAgent>();
            }

            Bounds bounds = CalculateBounds(model);
            if (bounds.size.y > 0f)
            {
                agent.radius = Mathf.Clamp(Mathf.Min(bounds.extents.x, bounds.extents.z), 0.2f, 0.6f);
                agent.height = bounds.size.y;
            }
            agent.speed = 3.5f;
            agent.acceleration = 12f;
            agent.stoppingDistance = 0f;

            if (enemy.GetComponent<EnemyController>() == null)
            {
                enemy.AddComponent<EnemyController>();
            }

            Combatant combatant = enemy.GetComponent<Combatant>();
            if (combatant == null)
            {
                combatant = enemy.AddComponent<Combatant>();
            }
            ApplyCombatantTiming(combatant);

            PopulateAttackDurations(enemy.GetComponent<AttackCombo>());
            SetupEnemyHealthUI(enemy, combatant, bounds);

            Selection.activeGameObject = enemy;
            Debug.Log("Enemy character spawned and wired up.");
        }

        private static void SetupEnemyHealthUI(GameObject enemy, Combatant combatant, Bounds modelBounds)
        {
            Transform existing = enemy.transform.Find("HealthCanvas");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            float heightAboveHead = (modelBounds.max.y - enemy.transform.position.y) + 0.3f;

            GameObject canvasObject = new GameObject("HealthCanvas", typeof(Canvas));
            canvasObject.transform.SetParent(enemy.transform, false);
            canvasObject.transform.localPosition = new Vector3(0f, heightAboveHead, 0f);
            canvasObject.transform.localScale = Vector3.one * 0.02f;

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(200f, 60f);

            GameObject textObject = new GameObject("HealthText", typeof(Text));
            textObject.transform.SetParent(canvasObject.transform, false);

            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 40;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = combatant.CurrentHealth.ToString();

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            EnemyHealthUI healthUI = canvasObject.AddComponent<EnemyHealthUI>();
            SerializedObject so = new SerializedObject(healthUI);
            so.FindProperty("combatant").objectReferenceValue = combatant;
            so.FindProperty("healthText").objectReferenceValue = text;
            so.ApplyModifiedProperties();
        }

        private static void AttachWeapon(GameObject model, string weaponModelPath, string weaponMaterialName)
        {
            Transform socket = FindDescendant(model.transform, WeaponSocketBoneName);
            if (socket == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find weapon socket '{WeaponSocketBoneName}' on '{model.name}'.");
                return;
            }

            GameObject weaponAsset = AssetDatabase.LoadAssetAtPath<GameObject>(weaponModelPath);
            if (weaponAsset == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find weapon at {weaponModelPath}.");
                return;
            }

            GameObject weapon = (GameObject)PrefabUtility.InstantiatePrefab(weaponAsset, socket);
            weapon.name = "Weapon";
            weapon.transform.localPosition = Vector3.zero;
            weapon.transform.localRotation = Quaternion.identity;

            // The socket bone lives inside the character rig's ~100x-scaled hierarchy, but the
            // weapon prop mesh is authored at normal (1x) scale. Counteract the parent's scale
            // so the weapon renders at its correct real-world size instead of being blown up.
            Vector3 parentScale = socket.lossyScale;
            weapon.transform.localScale = new Vector3(
                parentScale.x != 0f ? 1f / parentScale.x : 1f,
                parentScale.y != 0f ? 1f / parentScale.y : 1f,
                parentScale.z != 0f ? 1f / parentScale.z : 1f);

            ApplyCharacterMaterial(weapon, weaponMaterialName);
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            foreach (Transform child in root)
            {
                Transform result = FindDescendant(child, name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
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
