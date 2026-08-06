using System.Collections.Generic;
using System.IO;
using Darclite.Combat;
using Darclite.Core;
using Darclite.Dialogue;
using Darclite.Enemies;
using Darclite.Player;
using LLMUnity;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.VFX;

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

            Shader toonShader = Shader.Find("Darclite/AshenLit");
            if (toonShader != null)
            {
                material.shader = toonShader;
            }

            material.SetColor("_BaseColor", new Color(0.35f, 0.55f, 0.3f));
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        private const string TerrainDataFolder = "Assets/_Project/Terrain";
        private const string TerrainDataPath = "Assets/_Project/Terrain/WorldTerrainData.asset";
        private const string GrassTerrainTexturePath = "Assets/_Project/Terrain/GrassTerrainTexture.png";
        private const string GrassTerrainLayerPath = "Assets/_Project/Terrain/GrassTerrainLayer.terrainlayer";

        // Sized for "a village's worth" of land — a walkable clearing plus enough surrounding
        // countryside for hills/a mountain, not an open-world scale. Bump terrainWidth up later if
        // the village ends up needing more breathing room.
        private const float TerrainWidth = 300f;
        private const float TerrainMaxHeight = 60f;

        // Coarser than Unity's usual 513 default — a chunkier heightfield reads closer to the
        // low-poly look even before any custom shading pass, and it's plenty of detail for
        // stylized sculpting rather than realistic terrain.
        private const int TerrainHeightmapResolution = 129;

        [MenuItem("Darclite/Create Terrain")]
        public static void CreateTerrain()
        {
            GameObject existing = GameObject.Find("Terrain");
            if (existing != null)
            {
                Selection.activeGameObject = existing;
                Debug.Log("A Terrain already exists in the scene — selected it instead of creating a duplicate.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(TerrainDataFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "Terrain");
            }

            TerrainData terrainData = new TerrainData
            {
                heightmapResolution = TerrainHeightmapResolution,
                size = new Vector3(TerrainWidth, TerrainMaxHeight, TerrainWidth),
                terrainLayers = new[] { GetOrCreateGrassTerrainLayer() }
            };
            AssetDatabase.CreateAsset(terrainData, TerrainDataPath);

            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            terrainObject.name = "Terrain";
            // The heightmap starts perfectly flat at this object's own Y position — centering the
            // terrain on the world origin means it starts flat right under the existing Player/
            // Enemy/QuestNPC spawn points (all near (0,0,0)), matching what they already assume.
            // Keep the middle flat while sculpting; push hills/mountains out toward the edges.
            terrainObject.transform.position = new Vector3(-TerrainWidth / 2f, 0f, -TerrainWidth / 2f);

            Undo.RegisterCreatedObjectUndo(terrainObject, "Create Terrain");
            Selection.activeGameObject = terrainObject;

            Debug.Log($"Terrain created ({TerrainWidth}x{TerrainWidth}, starting flat at y=0). The old 'Floor' is now redundant — disable or delete it once you're happy with the terrain, then re-run 'Darclite/Bake NavMesh'.");
        }

        private static TerrainLayer GetOrCreateGrassTerrainLayer()
        {
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(GrassTerrainLayerPath);
            if (layer != null)
            {
                return layer;
            }

            const int size = 4;
            Color grassColor = new Color(0.35f, 0.55f, 0.3f);
            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = grassColor;
            }

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(GrassTerrainTexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(GrassTerrainTexturePath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(GrassTerrainTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }

            layer = new TerrainLayer
            {
                diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTerrainTexturePath),
                tileSize = new Vector2(8f, 8f)
            };
            AssetDatabase.CreateAsset(layer, GrassTerrainLayerPath);
            AssetDatabase.SaveAssets();

            return layer;
        }

        internal const string WarriorModelPath = "Assets/_Project/Art/Characters/RPGCharacterPack/Models/Warrior.fbx";
        internal const string PlayerControllerPath = "Assets/_Project/Animations/PlayerAnimatorController.controller";

        // Darclite/Create Player Animator Controller deletes and recreates the controller asset at
        // PlayerControllerPath every time it runs — even though the path stays the same, it's a
        // brand-new asset identity, so every Animator already in the scene/prefabs that was
        // pointing at the old one goes back to "not playing an AnimatorController" until it's
        // reassigned. Rather than remembering to re-run Setup Player/Enemy/Quest NPC Character and
        // Build Bandit Prefab every single time (each of which also redoes a bunch of unrelated
        // setup work), this just reattaches the current controller wherever it's gone missing.
        [MenuItem("Darclite/Reassign Animator Controllers")]
        public static void ReassignAnimatorControllers()
        {
            RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerControllerPath);
            if (controller == null)
            {
                Debug.LogError($"No Animator Controller found at {PlayerControllerPath} — run 'Create Player Animator Controller' first.");
                return;
            }

            int fixedCount = 0;
            foreach (string rootName in new[] { "Player", "Enemy", "QuestNPC" })
            {
                GameObject root = GameObject.Find(rootName);
                Animator animator = root != null ? root.GetComponentInChildren<Animator>() : null;
                if (animator == null)
                {
                    continue;
                }

                if (animator.runtimeAnimatorController == null)
                {
                    animator.runtimeAnimatorController = controller;
                    EditorUtility.SetDirty(animator);
                    fixedCount++;
                }
            }

            GameObject banditPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BanditPrefabPath);
            Animator banditAnimator = banditPrefab != null ? banditPrefab.GetComponentInChildren<Animator>() : null;
            if (banditAnimator != null && banditAnimator.runtimeAnimatorController == null)
            {
                banditAnimator.runtimeAnimatorController = controller;
                EditorUtility.SetDirty(banditPrefab);
                fixedCount++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[SceneBootstrapper] Reassigned the Animator Controller on {fixedCount} character(s)/prefab(s) that had lost it.");
        }

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
            ApplyCombatantTiming(combatant, PlayerMaxHealth);
            SetupHitEffect(player, combatant);

            PlayerCombat playerCombat = player.GetComponent<PlayerCombat>();
            if (playerCombat == null)
            {
                playerCombat = player.AddComponent<PlayerCombat>();
            }

            PopulateAttackDurations(player.GetComponent<AttackCombo>());
            SetupPlayerHealthUI(combatant);

            CharacterAudio playerAudio = player.GetComponent<CharacterAudio>();
            if (playerAudio == null)
            {
                playerAudio = player.AddComponent<CharacterAudio>();
            }
            PopulateCharacterAudio(playerAudio);

            BlockDodge blockDodge = player.GetComponent<BlockDodge>();
            if (blockDodge == null)
            {
                blockDodge = player.AddComponent<BlockDodge>();
            }
            ApplyBlockDodgeTiming(blockDodge, respondToKeyboardInput: true);

            SetupGameplayPostProcessing();
            SetupLiteConcentrationAura(player, animator);
            SetupLiteRecoveryAbility(player, animator, combatant);
            SetupLiteBracingAbility(player);
            SetupLiteSparkAbility(player);
            SetupLiteFlickerAbility(player, animator);
            SetupLiteReleaseAbility(player, animator);
            SetupForcefulStrikeAbility(player, animator);
            SetupAttackSensingAbility(player);
            SetupLiteBurstAbility(player, animator);
            SetupSteadyFocusAbility(player);
            SetupLiteSkinAbility(player);
            SetupSteadyStanceAbility(player);
            SetupBraceReflexAbility(player);
            SetupLiteTrickleAbility(player, combatant);
            SetupLiteSipAbility(player, combatant);
            SetupSecondWindAbility(player, combatant);

            Selection.activeGameObject = player;
            Debug.Log("Player character spawned and wired up.");
        }

        // ==================== Gameplay Post-Processing ====================

        private const string GameplayVolumeProfilePath = "Assets/_Project/Settings/GameplayPostProcessingProfile.asset";

        // Always-on global volume (unlike StatMenuBlurVolume, whose weight sits at 0 outside the
        // menu) — a subtle baseline Bloom so any bright/additive VFX (starting with the Lite
        // Concentration aura) actually reads as glowing instead of a flat white shape.
        private static void SetupGameplayPostProcessing()
        {
            GameObject volumeObject = GameObject.Find("GameplayPostProcessingVolume");
            if (volumeObject == null)
            {
                volumeObject = new GameObject("GameplayPostProcessingVolume");
            }

            Volume volume = volumeObject.GetComponent<Volume>();
            if (volume == null)
            {
                volume = volumeObject.AddComponent<Volume>();
            }

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GameplayVolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, GameplayVolumeProfilePath);
            }

            if (!profile.TryGet(out Bloom bloom))
            {
                bloom = profile.Add<Bloom>(true);
                AssetDatabase.AddObjectToAsset(bloom, profile);
            }

            // Threshold sits above 1.0 (standard LDR colors top out at 1.0) so ordinary alpha-
            // blended effects — e.g. the dash ghost trail's translucent blue silhouettes — don't
            // bloom into a blown-out blob. Only genuinely bright/additive things (stacked additive
            // particles, real Light components) are meant to push past it.
            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.3f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.25f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.4f;

            if (!profile.TryGet(out DepthOfField depthOfField))
            {
                depthOfField = profile.Add<DepthOfField>(true);
                AssetDatabase.AddObjectToAsset(depthOfField, profile);
            }

            // Starts fully off — LiteReleaseAbility (and anything else that wants an impact blur)
            // drives gaussianMaxRadius up and back down for a brief pulse, only flipping `active`
            // on while a pulse is actually running so it costs nothing the rest of the time. Kept
            // on this always-on shared volume (not StatMenuBlurVolume) so a gameplay impact pulse
            // can never race the Stat Menu's own open/close blur toggle.
            depthOfField.active = false;
            depthOfField.mode.overrideState = true;
            depthOfField.mode.value = DepthOfFieldMode.Gaussian;
            depthOfField.gaussianStart.overrideState = true;
            depthOfField.gaussianStart.value = 0.1f;
            depthOfField.gaussianEnd.overrideState = true;
            depthOfField.gaussianEnd.value = 3f;
            depthOfField.gaussianMaxRadius.overrideState = true;
            depthOfField.gaussianMaxRadius.value = 0f;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            volume.isGlobal = true;
            volume.sharedProfile = profile;
            volume.priority = 0f;
            volume.weight = 1f;

            StatMenuBootstrapper.EnsureCameraSupportsPostProcessing();
        }

        // ==================== Hit Effect ====================

        private const string HitEffectAssetPath = "Assets/_Project/VFX/Hit Effect.vfx";
        private const string LiteHitEffectAssetPath = "Assets/_Project/VFX/Lite Hit.vfx";

        // Shared by player, enemy, and quest NPC setup — any Combatant that can take a hit gets
        // both of these, positioned fresh at the Chest or Head bone (based on hitIndex) each time
        // Combatant.TakeHit/TakeKnockback lands, so it reads as coming from the actual point of
        // contact rather than a fixed spot on the body. Combatant resolves the bone itself off its
        // own existing animator reference, so nothing extra needs wiring here. Which one actually
        // plays (normal vs. the bigger Lite Hit) is decided at runtime in AttackCombo/Combatant
        // based on whether the attacker had Lite Concentration active.
        //
        // Note: scaling these objects' transforms does NOT change the rendered particle size for
        // either graph (confirmed by testing) — both graphs must be sized by hand inside the graph
        // itself (Initialize Particle's Set Size block), not from here.
        private static void SetupHitEffect(GameObject character, Combatant combatant)
        {
            if (combatant == null)
            {
                return;
            }

            VisualEffect hitEffect = BuildHitVfxChild(character, "HitEffect", HitEffectAssetPath);
            VisualEffect liteHitEffect = BuildHitVfxChild(character, "LiteHitEffect", LiteHitEffectAssetPath);

            SerializedObject so = new SerializedObject(combatant);
            so.FindProperty("hitEffect").objectReferenceValue = hitEffect;
            so.FindProperty("liteHitEffect").objectReferenceValue = liteHitEffect;
            so.ApplyModifiedProperties();
        }

        private static VisualEffect BuildHitVfxChild(GameObject character, string name, string assetPath)
        {
            VisualEffectAsset asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(assetPath);
            if (asset == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find VFX asset at {assetPath}");
                return null;
            }

            Transform existing = FindDescendant(character.transform, name);
            GameObject vfxObject = existing != null ? existing.gameObject : new GameObject(name, typeof(VisualEffect));
            vfxObject.transform.SetParent(character.transform, false);
            // SetParent(..., false) leaves local position/rotation/scale untouched — reusing an
            // existing object (Player/Enemy, whose setup tools never destroy the character root)
            // silently carries forward whatever scale it happened to end up with historically,
            // while a freshly-created one (Quest NPC, which is fully destroyed/recreated every run)
            // always starts at the correct default. Force it explicitly so both paths match.
            vfxObject.transform.localScale = Vector3.one;

            VisualEffect visualEffect = vfxObject.GetComponent<VisualEffect>();
            visualEffect.visualEffectAsset = asset;

            return visualEffect;
        }

        // ==================== Lite Concentration Aura ====================

        private const string AbilityAuraShaderName = "Darclite/AdditiveGlowParticle";
        private const string AbilityAuraRimShaderName = "Darclite/RimGlowShell";
        private const string AbilityAuraGlowSpritePath = "Assets/_Project/Art/UI/SoftWhiteGlow.png";
        private const string AbilityAuraRingSpritePath = "Assets/_Project/Art/UI/SoftGlowRing.png";
        private const string AbilityAuraMoteMaterialPath = "Assets/_Project/Materials/AbilityAuraMote.mat";
        private const string AbilityAuraRingBurstMaterialPath = "Assets/_Project/Materials/AbilityAuraRingBurst.mat";
        private const string AbilityAuraRimMaterialPath = "Assets/_Project/Materials/AbilityAuraRimGlow.mat";

        // A pale warm-white reads as "magical light" far better than clinical pure white, across
        // every layer of the effect (particles, lights, rim glow, ring).
        private static readonly Color AbilityAuraWarmWhite = new Color(1f, 0.95f, 0.85f);

        private static readonly string[] LiteConcentrationAuraObjectNames =
        {
            // First two are the previous version's mote object names, kept here purely so a
            // rebuild cleans up the now-orphaned old objects instead of leaving duplicates behind.
            "LiteConcentrationAura_LeftArm", "LiteConcentrationAura_RightArm",
            "LiteConcentrationMotes_LeftArm", "LiteConcentrationMotes_RightArm",
            "LiteConcentrationWisps_LeftArm", "LiteConcentrationWisps_RightArm",
            "LiteConcentrationShell_LeftArm", "LiteConcentrationShell_RightArm",
            // Kept so a rebuild cleans up the now-removed persistent palm ring if it's still
            // present in the scene from before.
            "LiteConcentrationRing_LeftHand", "LiteConcentrationRing_RightHand",
            "LiteConcentrationFlash_LeftHand", "LiteConcentrationFlash_RightHand",
            "LiteConcentrationRingBurst_LeftHand", "LiteConcentrationRingBurst_RightHand",
            "LiteConcentrationLight_LeftArm", "LiteConcentrationLight_RightArm",
            "LiteConcentrationAudioSource",
            "LiteConcentrationVFX_LeftHand", "LiteConcentrationVFX_RightHand",
        };

        // Lite Concentration (tier 1) and Lite Concentration II (tier 2) hand VFX — the single
        // LiteConcentrationAura component swaps between these at runtime based on which tier
        // actually activates, since only one can ever be equipped at a time.
        private const string LiteConcentrationTierOneVfxAssetPath = "Assets/_Project/VFX/Lite Concentration.vfx";
        private const string LiteConcentrationTierTwoVfxAssetPath = "Assets/_Project/VFX/Lite Concentration 2.vfx";

        // Positions everything at the actual midpoint (or exact position) between/at real bone
        // transforms rather than a guessed local axis/offset, and uses shapes (Sphere, camera-
        // facing Billboards) that look correct regardless of the rig's own bone-roll convention —
        // so nothing here can end up rotated the wrong way relative to the arm.
        private static void SetupLiteConcentrationAura(GameObject player, Animator animator)
        {
            foreach (string name in LiteConcentrationAuraObjectNames)
            {
                Transform existing = FindDescendant(player.transform, name);
                if (existing != null)
                {
                    Object.DestroyImmediate(existing.gameObject);
                }
            }

            if (animator == null)
            {
                Debug.LogWarning("[SceneBootstrapper] No Animator on player model; skipping Lite Concentration aura setup.");
                return;
            }

            Transform[] lowerArms = { animator.GetBoneTransform(HumanBodyBones.LeftLowerArm), animator.GetBoneTransform(HumanBodyBones.RightLowerArm) };
            Transform[] hands = { animator.GetBoneTransform(HumanBodyBones.LeftHand), animator.GetBoneTransform(HumanBodyBones.RightHand) };
            string[] armSideNames = { "LeftArm", "RightArm" };
            string[] handSideNames = { "LeftHand", "RightHand" };

            if (lowerArms[0] == null || lowerArms[1] == null || hands[0] == null || hands[1] == null)
            {
                Debug.LogWarning("[SceneBootstrapper] Could not resolve arm bones on player model; skipping Lite Concentration aura setup.");
                return;
            }

            Sprite softGlowSprite = CreateSoftWhiteGlowSprite();
            Sprite ringSprite = CreateGlowRingSprite();
            if (softGlowSprite == null || ringSprite == null)
            {
                return;
            }

            Material moteMaterial = CreateOrLoadAuraMaterial(AbilityAuraShaderName, softGlowSprite.texture, AbilityAuraMoteMaterialPath);
            Material ringBurstMaterial = CreateOrLoadAuraMaterial(AbilityAuraShaderName, ringSprite.texture, AbilityAuraRingBurstMaterialPath);
            Material rimGlowMaterial = CreateOrLoadRimGlowMaterial();

            if (moteMaterial == null || ringBurstMaterial == null || rimGlowMaterial == null)
            {
                return;
            }

            ParticleSystem[] motes = new ParticleSystem[2];
            ParticleSystem[] wisps = new ParticleSystem[2];
            ParticleSystem[] flashes = new ParticleSystem[2];
            ParticleSystem[] ringBursts = new ParticleSystem[2];
            Light[] lights = new Light[2];
            VisualEffect[] handVfx = new VisualEffect[2];

            VisualEffectAsset tierOneVfxAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(LiteConcentrationTierOneVfxAssetPath);
            if (tierOneVfxAsset == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find Lite Concentration VFX asset at {LiteConcentrationTierOneVfxAssetPath}");
            }

            VisualEffectAsset tierTwoVfxAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(LiteConcentrationTierTwoVfxAssetPath);
            if (tierTwoVfxAsset == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find Lite Concentration II VFX asset at {LiteConcentrationTierTwoVfxAssetPath}");
            }

            for (int i = 0; i < 2; i++)
            {
                Transform lowerArm = lowerArms[i];
                Transform hand = hands[i];

                motes[i] = BuildArmAuraParticles($"LiteConcentrationMotes_{armSideNames[i]}", lowerArm, hand, moteMaterial);
                wisps[i] = BuildArmWispParticles($"LiteConcentrationWisps_{armSideNames[i]}", lowerArm, hand, moteMaterial);
                BuildArmGlowShell($"LiteConcentrationShell_{armSideNames[i]}", lowerArm, hand, rimGlowMaterial);
                flashes[i] = BuildCastFlash($"LiteConcentrationFlash_{handSideNames[i]}", hand, moteMaterial);
                ringBursts[i] = BuildCastRingBurst($"LiteConcentrationRingBurst_{handSideNames[i]}", hand, ringBurstMaterial);
                lights[i] = BuildArmAuraLight($"LiteConcentrationLight_{armSideNames[i]}", lowerArm, hand);

                // Built with tier 1's asset as a sane default — LiteConcentrationAura swaps this
                // to whichever tier actually activates before every cast.
                VisualEffectAsset defaultAsset = tierOneVfxAsset != null ? tierOneVfxAsset : tierTwoVfxAsset;
                if (defaultAsset != null)
                {
                    handVfx[i] = BuildHandVfx($"LiteConcentrationVFX_{handSideNames[i]}", hand, defaultAsset);
                }
            }

            AudioSource loopAudioSource = BuildLoopAudioSource("LiteConcentrationAudioSource", player.transform);
            AudioClip loopClip = LoadAudioClip(FightAudioFolder, "liteconcentration");

            Volume gameplayVolume = null;
            GameObject volumeObject = GameObject.Find("GameplayPostProcessingVolume");
            if (volumeObject != null)
            {
                gameplayVolume = volumeObject.GetComponent<Volume>();
            }

            LiteConcentrationAura auraController = player.GetComponent<LiteConcentrationAura>();
            if (auraController == null)
            {
                auraController = player.AddComponent<LiteConcentrationAura>();
            }

            SerializedObject auraSo = new SerializedObject(auraController);
            AssignObjectArray(auraSo, "moteParticles", motes);
            AssignObjectArray(auraSo, "wispParticles", wisps);
            AssignObjectArray(auraSo, "castFlashParticles", flashes);
            AssignObjectArray(auraSo, "castRingBurstParticles", ringBursts);
            AssignObjectArray(auraSo, "armLights", lights);
            AssignObjectArray(auraSo, "handVfx", handVfx);
            auraSo.FindProperty("tierOneHandVfxAsset").objectReferenceValue = tierOneVfxAsset;
            auraSo.FindProperty("tierTwoHandVfxAsset").objectReferenceValue = tierTwoVfxAsset;
            auraSo.FindProperty("rimGlowMaterial").objectReferenceValue = rimGlowMaterial;
            auraSo.FindProperty("gameplayVolume").objectReferenceValue = gameplayVolume;
            auraSo.FindProperty("loopAudioSource").objectReferenceValue = loopAudioSource;
            auraSo.FindProperty("loopClip").objectReferenceValue = loopClip;
            auraSo.ApplyModifiedProperties();
        }

        // ==================== Lite Recovery Ability ====================

        private const string LiteRecoveryVfxAssetPath = "Assets/_Project/VFX/Lite Healing.vfx";

        private static void SetupLiteRecoveryAbility(GameObject player, Animator animator, Combatant combatant)
        {
            Transform existing = FindDescendant(player.transform, "LiteRecoveryVFX");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            if (animator == null)
            {
                Debug.LogWarning("[SceneBootstrapper] No Animator on player model; skipping Lite Recovery ability setup.");
                return;
            }

            Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null)
            {
                Debug.LogWarning("[SceneBootstrapper] Could not resolve chest bone on player model; skipping Lite Recovery ability setup.");
                return;
            }

            VisualEffectAsset liteRecoveryVfxAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(LiteRecoveryVfxAssetPath);
            if (liteRecoveryVfxAsset == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find Lite Recovery VFX asset at {LiteRecoveryVfxAssetPath}");
            }

            VisualEffect healVfx = liteRecoveryVfxAsset != null
                ? BuildHandVfx("LiteRecoveryVFX", chest, liteRecoveryVfxAsset)
                : null;

            LiteRecoveryAbility abilityController = player.GetComponent<LiteRecoveryAbility>();
            if (abilityController == null)
            {
                abilityController = player.AddComponent<LiteRecoveryAbility>();
            }

            SerializedObject abilitySo = new SerializedObject(abilityController);
            abilitySo.FindProperty("combatant").objectReferenceValue = combatant;
            abilitySo.FindProperty("healVfx").objectReferenceValue = healVfx;
            abilitySo.ApplyModifiedProperties();
        }

        // ==================== Lite Bracing Ability ====================

        private const string LiteAuraVfxAssetPath = "Assets/_Project/VFX/Lite Aura.vfx";

        // Cleans up the old per-arm motes/wisps objects from the previous version of this ability,
        // in favor of a single hand-authored VFX Graph effect at the player's feet.
        private static readonly string[] LiteBracingCleanupObjectNames =
        {
            "LiteBracingMotes_LeftArm", "LiteBracingMotes_RightArm",
            "LiteBracingWisps_LeftArm", "LiteBracingWisps_RightArm",
        };

        // A pure toggle read directly off BlockDodge — no VFX/audio/animation of its own, so unlike
        // every other ability here there's nothing to wire beyond just making sure it exists.
        private static void SetupAttackSensingAbility(GameObject player)
        {
            if (player.GetComponent<AttackSensingAbility>() == null)
            {
                player.AddComponent<AttackSensingAbility>();
            }
        }

        // A pure toggle read directly off AttackCombo — no VFX/audio/animation of its own, so
        // unlike every other ability here there's nothing to wire beyond just making sure it exists.
        private static void SetupLiteSparkAbility(GameObject player)
        {
            if (player.GetComponent<LiteSparkAbility>() == null)
            {
                player.AddComponent<LiteSparkAbility>();
            }
        }

        // A pure toggle read directly off ThirdPersonOrbitCamera.Shake — no VFX/audio/animation of
        // its own, so unlike every other ability here there's nothing to wire beyond just making
        // sure it exists.
        private static void SetupSteadyFocusAbility(GameObject player)
        {
            if (player.GetComponent<SteadyFocusAbility>() == null)
            {
                player.AddComponent<SteadyFocusAbility>();
            }
        }

        // A pure toggle read directly off Combatant.ApplyDamage — no VFX/audio/animation of its
        // own, so unlike every other ability here there's nothing to wire beyond just making sure
        // it exists.
        private static void SetupLiteSkinAbility(GameObject player)
        {
            if (player.GetComponent<LiteSkinAbility>() == null)
            {
                player.AddComponent<LiteSkinAbility>();
            }
        }

        // A pure toggle read directly off Combatant.KnockbackSlide — no VFX/audio/animation of its
        // own, so unlike every other ability here there's nothing to wire beyond just making sure
        // it exists.
        private static void SetupSteadyStanceAbility(GameObject player)
        {
            if (player.GetComponent<SteadyStanceAbility>() == null)
            {
                player.AddComponent<SteadyStanceAbility>();
            }
        }

        // ==================== Brace Reflex Ability ====================

        private const string BraceReflexVfxAssetPath = "Assets/_Project/VFX/Brace Reflex.vfx";

        // Parented directly to the player root at local zero, same as Lite Bracing's aura — no
        // animation means the visual has to read as "on the player" regardless of what pose
        // they're currently in when it fires.
        private static void SetupBraceReflexAbility(GameObject player)
        {
            VisualEffectAsset braceVfxAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(BraceReflexVfxAssetPath);
            if (braceVfxAsset == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find Brace Reflex VFX asset at {BraceReflexVfxAssetPath}");
            }

            Transform existingVfx = FindDescendant(player.transform, "BraceReflexVFX");
            GameObject vfxObject = existingVfx != null ? existingVfx.gameObject : new GameObject("BraceReflexVFX", typeof(VisualEffect));
            vfxObject.transform.SetParent(player.transform, false);
            vfxObject.transform.localPosition = Vector3.zero;

            VisualEffect braceVfx = null;
            if (braceVfxAsset != null)
            {
                braceVfx = vfxObject.GetComponent<VisualEffect>();
                braceVfx.visualEffectAsset = braceVfxAsset;
            }

            Transform existingAudio = FindDescendant(player.transform, "BraceReflexAudioSource");
            GameObject audioObject = existingAudio != null ? existingAudio.gameObject : new GameObject("BraceReflexAudioSource", typeof(AudioSource));
            audioObject.transform.SetParent(player.transform, false);
            AudioSource audioSource = audioObject.GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 1f;

            AudioClip braceClip = LoadAudioClip(LiteAudioFolder, "Brace Reflex");

            BraceReflexAbility abilityController = player.GetComponent<BraceReflexAbility>();
            if (abilityController == null)
            {
                abilityController = player.AddComponent<BraceReflexAbility>();
            }

            SerializedObject abilitySo = new SerializedObject(abilityController);
            abilitySo.FindProperty("braceVfx").objectReferenceValue = braceVfx;
            abilitySo.FindProperty("audioSource").objectReferenceValue = audioSource;
            abilitySo.FindProperty("braceClip").objectReferenceValue = braceClip;
            abilitySo.ApplyModifiedProperties();
        }

        // ==================== Lite Trickle Ability ====================

        // A pure passive read against Combatant/PlayerController every frame — no VFX/audio/
        // animation of its own.
        private static void SetupLiteTrickleAbility(GameObject player, Combatant combatant)
        {
            PlayerController playerController = player.GetComponent<PlayerController>();

            LiteTrickleAbility abilityController = player.GetComponent<LiteTrickleAbility>();
            if (abilityController == null)
            {
                abilityController = player.AddComponent<LiteTrickleAbility>();
            }

            SerializedObject abilitySo = new SerializedObject(abilityController);
            abilitySo.FindProperty("combatant").objectReferenceValue = combatant;
            abilitySo.FindProperty("playerController").objectReferenceValue = playerController;
            abilitySo.ApplyModifiedProperties();
        }

        // ==================== Lite Sip Ability ====================

        private const string LiteSipVfxAssetPath = "Assets/_Project/VFX/Lite Sip.vfx";

        // Parented directly to the player root at local zero — no animation, so this puts the
        // heal VFX at their feet regardless of whatever pose they're currently in.
        private static void SetupLiteSipAbility(GameObject player, Combatant combatant)
        {
            VisualEffectAsset sipVfxAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(LiteSipVfxAssetPath);
            if (sipVfxAsset == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find Lite Sip VFX asset at {LiteSipVfxAssetPath}");
            }

            Transform existingVfx = FindDescendant(player.transform, "LiteSipVFX");
            GameObject vfxObject = existingVfx != null ? existingVfx.gameObject : new GameObject("LiteSipVFX", typeof(VisualEffect));
            vfxObject.transform.SetParent(player.transform, false);
            vfxObject.transform.localPosition = Vector3.zero;

            VisualEffect sipVfx = null;
            if (sipVfxAsset != null)
            {
                sipVfx = vfxObject.GetComponent<VisualEffect>();
                sipVfx.visualEffectAsset = sipVfxAsset;
            }

            Transform existingAudio = FindDescendant(player.transform, "LiteSipAudioSource");
            GameObject audioObject = existingAudio != null ? existingAudio.gameObject : new GameObject("LiteSipAudioSource", typeof(AudioSource));
            audioObject.transform.SetParent(player.transform, false);
            AudioSource audioSource = audioObject.GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 1f;

            AudioClip sipClip = LoadAudioClip(LiteAudioFolder, "Lite Sip");

            LiteSipAbility abilityController = player.GetComponent<LiteSipAbility>();
            if (abilityController == null)
            {
                abilityController = player.AddComponent<LiteSipAbility>();
            }

            SerializedObject abilitySo = new SerializedObject(abilityController);
            abilitySo.FindProperty("combatant").objectReferenceValue = combatant;
            abilitySo.FindProperty("sipVfx").objectReferenceValue = sipVfx;
            abilitySo.FindProperty("audioSource").objectReferenceValue = audioSource;
            abilitySo.FindProperty("sipClip").objectReferenceValue = sipClip;
            abilitySo.ApplyModifiedProperties();
        }

        // ==================== Second Wind Ability ====================

        private const string SecondWindVfxAssetPath = "Assets/_Project/VFX/Second Wind.vfx";

        // Parented directly to the player root at local zero, same as every other feet-VFX ability
        // here — the emitter tracks the player automatically as a child transform. Whether the
        // spawned particles themselves also follow (rather than hanging in place once emitted)
        // depends on the graph's own Simulation Space setting (Local vs World), which lives inside
        // the VFX Graph asset and can't be changed from this script.
        private static void SetupSecondWindAbility(GameObject player, Combatant combatant)
        {
            VisualEffectAsset windVfxAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(SecondWindVfxAssetPath);
            if (windVfxAsset == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find Second Wind VFX asset at {SecondWindVfxAssetPath}");
            }

            Transform existingVfx = FindDescendant(player.transform, "SecondWindVFX");
            GameObject vfxObject = existingVfx != null ? existingVfx.gameObject : new GameObject("SecondWindVFX", typeof(VisualEffect));
            vfxObject.transform.SetParent(player.transform, false);
            vfxObject.transform.localPosition = Vector3.zero;

            VisualEffect windVfx = null;
            if (windVfxAsset != null)
            {
                windVfx = vfxObject.GetComponent<VisualEffect>();
                windVfx.visualEffectAsset = windVfxAsset;
            }

            Transform existingAudio = FindDescendant(player.transform, "SecondWindAudioSource");
            GameObject audioObject = existingAudio != null ? existingAudio.gameObject : new GameObject("SecondWindAudioSource", typeof(AudioSource));
            audioObject.transform.SetParent(player.transform, false);
            AudioSource audioSource = audioObject.GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 1f;

            AudioClip windClip = LoadAudioClip(LiteAudioFolder, "Second Wind");

            SecondWindAbility abilityController = player.GetComponent<SecondWindAbility>();
            if (abilityController == null)
            {
                abilityController = player.AddComponent<SecondWindAbility>();
            }

            SerializedObject abilitySo = new SerializedObject(abilityController);
            abilitySo.FindProperty("combatant").objectReferenceValue = combatant;
            abilitySo.FindProperty("windVfx").objectReferenceValue = windVfx;
            abilitySo.FindProperty("audioSource").objectReferenceValue = audioSource;
            abilitySo.FindProperty("windClip").objectReferenceValue = windClip;
            abilitySo.ApplyModifiedProperties();
        }

        // Parented directly to the player root (not a bone) at local zero — since the player's
        // root transform sits at ground level, this puts the effect at their feet and keeps it
        // riding along with them automatically as they move, without any per-frame position code.
        private static void SetupLiteBracingAbility(GameObject player)
        {
            foreach (string name in LiteBracingCleanupObjectNames)
            {
                Transform existing = FindDescendant(player.transform, name);
                if (existing != null)
                {
                    Object.DestroyImmediate(existing.gameObject);
                }
            }

            VisualEffectAsset liteAuraAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(LiteAuraVfxAssetPath);
            if (liteAuraAsset == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find Lite Aura VFX asset at {LiteAuraVfxAssetPath}");
            }

            Transform existingVfx = FindDescendant(player.transform, "LiteBracingVFX");
            GameObject vfxObject = existingVfx != null ? existingVfx.gameObject : new GameObject("LiteBracingVFX", typeof(VisualEffect));
            vfxObject.transform.SetParent(player.transform, false);
            vfxObject.transform.localPosition = Vector3.zero;

            VisualEffect auraVfx = null;
            if (liteAuraAsset != null)
            {
                auraVfx = vfxObject.GetComponent<VisualEffect>();
                auraVfx.visualEffectAsset = liteAuraAsset;
            }

            LiteBracingAbility abilityController = player.GetComponent<LiteBracingAbility>();
            if (abilityController == null)
            {
                abilityController = player.AddComponent<LiteBracingAbility>();
            }

            SerializedObject abilitySo = new SerializedObject(abilityController);
            abilitySo.FindProperty("auraVfx").objectReferenceValue = auraVfx;
            abilitySo.ApplyModifiedProperties();
        }

        // ==================== Lite Release Ability ====================

        private const string LiteReleaseVfxAssetPath = "Assets/_Project/VFX/Lite Release.vfx";
        private const string LiteAnimationsFolder = "Assets/_Project/Animations/Lite Animations";
        private const string LiteAudioFolder = "Assets/_Project/Audio/LiteAudio";

        // Parented directly to the player root at local zero, same as Lite Bracing's aura — this
        // is a burst centered on the player, not a per-bone effect, so it just needs to ride along
        // with them rather than track any specific bone.
        private static void SetupLiteReleaseAbility(GameObject player, Animator animator)
        {
            // Force a fresh import so CharacterModelPostprocessor's Lite Animations handling
            // actually applies even if this clip was already imported once before that folder was
            // recognized (e.g. it was added to the project before this postprocessor branch was).
            AssetDatabase.ImportAsset($"{LiteAnimationsFolder}/Lite Release.fbx", ImportAssetOptions.ForceUpdate);

            VisualEffectAsset releaseVfxAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(LiteReleaseVfxAssetPath);
            if (releaseVfxAsset == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find Lite Release VFX asset at {LiteReleaseVfxAssetPath}");
            }

            Transform existingVfx = FindDescendant(player.transform, "LiteReleaseVFX");
            GameObject vfxObject = existingVfx != null ? existingVfx.gameObject : new GameObject("LiteReleaseVFX", typeof(VisualEffect));
            vfxObject.transform.SetParent(player.transform, false);
            vfxObject.transform.localPosition = Vector3.zero;

            VisualEffect releaseVfx = null;
            if (releaseVfxAsset != null)
            {
                releaseVfx = vfxObject.GetComponent<VisualEffect>();
                releaseVfx.visualEffectAsset = releaseVfxAsset;
            }

            Transform existingAudio = FindDescendant(player.transform, "LiteReleaseAudioSource");
            GameObject audioObject = existingAudio != null ? existingAudio.gameObject : new GameObject("LiteReleaseAudioSource", typeof(AudioSource));
            audioObject.transform.SetParent(player.transform, false);
            AudioSource audioSource = audioObject.GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 1f;

            AudioClip backgroundClip = LoadAudioClip(LiteAudioFolder, "Lite Release Background");
            AudioClip explosionClip = LoadAudioClip(LiteAudioFolder, "Lite Release Explosion");

            float castDuration = GetLiteAnimationClipLength("Lite Release");

            Volume gameplayVolume = null;
            GameObject gameplayVolumeObject = GameObject.Find("GameplayPostProcessingVolume");
            if (gameplayVolumeObject != null)
            {
                gameplayVolume = gameplayVolumeObject.GetComponent<Volume>();
            }

            LiteReleaseAbility abilityController = player.GetComponent<LiteReleaseAbility>();
            if (abilityController == null)
            {
                abilityController = player.AddComponent<LiteReleaseAbility>();
            }

            SerializedObject abilitySo = new SerializedObject(abilityController);
            abilitySo.FindProperty("animator").objectReferenceValue = animator;
            abilitySo.FindProperty("releaseVfx").objectReferenceValue = releaseVfx;
            abilitySo.FindProperty("audioSource").objectReferenceValue = audioSource;
            abilitySo.FindProperty("backgroundClip").objectReferenceValue = backgroundClip;
            abilitySo.FindProperty("explosionClip").objectReferenceValue = explosionClip;
            abilitySo.FindProperty("gameplayVolume").objectReferenceValue = gameplayVolume;
            if (castDuration > 0f)
            {
                abilitySo.FindProperty("castDuration").floatValue = castDuration;
            }
            abilitySo.ApplyModifiedProperties();
        }

        private static float GetLiteAnimationClipLength(string clipName)
        {
            string path = $"{LiteAnimationsFolder}/{clipName}.fbx";
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is AnimationClip clip && !clip.name.Contains("__preview__"))
                {
                    return clip.length;
                }
            }

            Debug.LogWarning($"[SceneBootstrapper] Could not find Lite animation clip at {path}");
            return 0f;
        }

        // ==================== Lite Burst Ability ====================

        private const string LiteBurstVfxAssetPath = "Assets/_Project/VFX/Lite Burst.vfx";

        // Parented directly to the player root at local zero — position and rotation are both set
        // fresh on every cast (LiteBurstAbility orients it along the caster's current forward), so
        // its resting transform here doesn't matter beyond keeping it out from underfoot.
        private static void SetupLiteBurstAbility(GameObject player, Animator animator)
        {
            // Force a fresh import so CharacterModelPostprocessor's Lite Animations handling
            // actually applies even if this clip was already imported once before that folder was
            // recognized.
            AssetDatabase.ImportAsset($"{LiteAnimationsFolder}/Lite Burst.fbx", ImportAssetOptions.ForceUpdate);

            VisualEffectAsset burstVfxAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(LiteBurstVfxAssetPath);
            if (burstVfxAsset == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find Lite Burst VFX asset at {LiteBurstVfxAssetPath}");
            }

            Transform existingVfx = FindDescendant(player.transform, "LiteBurstVFX");
            GameObject vfxObject = existingVfx != null ? existingVfx.gameObject : new GameObject("LiteBurstVFX", typeof(VisualEffect));
            vfxObject.transform.SetParent(player.transform, false);
            vfxObject.transform.localPosition = Vector3.zero;
            vfxObject.transform.localScale = Vector3.one;

            VisualEffect burstVfx = null;
            if (burstVfxAsset != null)
            {
                burstVfx = vfxObject.GetComponent<VisualEffect>();
                burstVfx.visualEffectAsset = burstVfxAsset;
            }

            Transform existingAudio = FindDescendant(player.transform, "LiteBurstAudioSource");
            GameObject audioObject = existingAudio != null ? existingAudio.gameObject : new GameObject("LiteBurstAudioSource", typeof(AudioSource));
            audioObject.transform.SetParent(player.transform, false);
            AudioSource audioSource = audioObject.GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 1f;

            AudioClip burstClip = LoadAudioClip(LiteAudioFolder, "Lite Burst");

            float castDuration = GetLiteAnimationClipLength("Lite Burst");

            // Burst originates from the hand rather than the feet — falls back to the player root
            // via LiteBurstAbility itself if this bone can't be resolved (e.g. non-humanoid rig).
            Transform castAnchor = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;

            LiteBurstAbility abilityController = player.GetComponent<LiteBurstAbility>();
            if (abilityController == null)
            {
                abilityController = player.AddComponent<LiteBurstAbility>();
            }

            SerializedObject abilitySo = new SerializedObject(abilityController);
            abilitySo.FindProperty("animator").objectReferenceValue = animator;
            abilitySo.FindProperty("burstVfx").objectReferenceValue = burstVfx;
            abilitySo.FindProperty("audioSource").objectReferenceValue = audioSource;
            abilitySo.FindProperty("burstClip").objectReferenceValue = burstClip;
            abilitySo.FindProperty("castAnchor").objectReferenceValue = castAnchor;
            abilitySo.FindProperty("burstRange").floatValue = 13.5f;
            abilitySo.FindProperty("burstHalfAngle").floatValue = 25f;
            if (castDuration > 0f)
            {
                abilitySo.FindProperty("castDuration").floatValue = castDuration;
            }
            abilitySo.ApplyModifiedProperties();
        }

        // ==================== Lite Flicker Ability ====================

        private const string LiteFlickerVfxAssetPath = "Assets/_Project/VFX/Lite Flicker.vfx";

        // Parented directly to the player root at local zero — position and rotation are both set
        // fresh on every cast (LiteFlickerAbility orients it along the caster's current forward),
        // so its resting transform here doesn't matter beyond keeping it out from underfoot.
        private static void SetupLiteFlickerAbility(GameObject player, Animator animator)
        {
            // Force a fresh import so CharacterModelPostprocessor's Lite Animations handling
            // actually applies even if this clip was already imported once before that folder was
            // recognized.
            AssetDatabase.ImportAsset($"{LiteAnimationsFolder}/Lite Flicker.fbx", ImportAssetOptions.ForceUpdate);

            VisualEffectAsset flickerVfxAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(LiteFlickerVfxAssetPath);
            if (flickerVfxAsset == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find Lite Flicker VFX asset at {LiteFlickerVfxAssetPath}");
            }

            Transform existingVfx = FindDescendant(player.transform, "LiteFlickerVFX");
            GameObject vfxObject = existingVfx != null ? existingVfx.gameObject : new GameObject("LiteFlickerVFX", typeof(VisualEffect));
            vfxObject.transform.SetParent(player.transform, false);
            vfxObject.transform.localPosition = Vector3.zero;
            vfxObject.transform.localScale = Vector3.one;

            VisualEffect flickerVfx = null;
            if (flickerVfxAsset != null)
            {
                flickerVfx = vfxObject.GetComponent<VisualEffect>();
                flickerVfx.visualEffectAsset = flickerVfxAsset;
            }

            Transform existingAudio = FindDescendant(player.transform, "LiteFlickerAudioSource");
            GameObject audioObject = existingAudio != null ? existingAudio.gameObject : new GameObject("LiteFlickerAudioSource", typeof(AudioSource));
            audioObject.transform.SetParent(player.transform, false);
            AudioSource audioSource = audioObject.GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 1f;

            AudioClip flickerClip = LoadAudioClip(LiteAudioFolder, "Lite Flicker");

            float castDuration = GetLiteAnimationClipLength("Lite Flicker");

            // Flicker originates from the hand rather than the feet — falls back to the player
            // root via LiteFlickerAbility itself if this bone can't be resolved (e.g. non-humanoid
            // rig).
            Transform castAnchor = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;

            LiteFlickerAbility abilityController = player.GetComponent<LiteFlickerAbility>();
            if (abilityController == null)
            {
                abilityController = player.AddComponent<LiteFlickerAbility>();
            }

            SerializedObject abilitySo = new SerializedObject(abilityController);
            abilitySo.FindProperty("animator").objectReferenceValue = animator;
            abilitySo.FindProperty("flickerVfx").objectReferenceValue = flickerVfx;
            abilitySo.FindProperty("audioSource").objectReferenceValue = audioSource;
            abilitySo.FindProperty("flickerClip").objectReferenceValue = flickerClip;
            abilitySo.FindProperty("castAnchor").objectReferenceValue = castAnchor;
            abilitySo.FindProperty("flickerRange").floatValue = 4.2f;
            abilitySo.FindProperty("flickerHalfAngle").floatValue = 20f;
            if (castDuration > 0f)
            {
                abilitySo.FindProperty("castDuration").floatValue = castDuration;
            }
            abilitySo.ApplyModifiedProperties();
        }

        // ==================== Forceful Strike Ability ====================

        private const string ForcefulStrikeVfxAssetPath = "Assets/_Project/VFX/Forceful Strike.vfx";
        private const string ForcefulStrikeImpactVfxAssetPath = "Assets/_Project/VFX/Forceful Strike Impact.vfx";

        // No cast animation of its own (unlike Lite Release) — this is a passive-until-triggered
        // buff on your next ordinary punch, so it only needs VFX/audio wiring, never touching the
        // Animator Controller at all.
        private static void SetupForcefulStrikeAbility(GameObject player, Animator animator)
        {
            foreach (string name in new[] { "ForcefulStrikeVFX_LeftHand", "ForcefulStrikeVFX_RightHand", "ForcefulStrikeImpactVFX" })
            {
                Transform existing = FindDescendant(player.transform, name);
                if (existing != null)
                {
                    Object.DestroyImmediate(existing.gameObject);
                }
            }

            if (animator == null)
            {
                Debug.LogWarning("[SceneBootstrapper] No Animator on player model; skipping Forceful Strike ability setup.");
                return;
            }

            Transform[] hands = { animator.GetBoneTransform(HumanBodyBones.LeftHand), animator.GetBoneTransform(HumanBodyBones.RightHand) };
            if (hands[0] == null || hands[1] == null)
            {
                Debug.LogWarning("[SceneBootstrapper] Could not resolve hand bones on player model; skipping Forceful Strike ability setup.");
                return;
            }

            VisualEffectAsset handVfxAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(ForcefulStrikeVfxAssetPath);
            if (handVfxAsset == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find Forceful Strike VFX asset at {ForcefulStrikeVfxAssetPath}");
            }

            VisualEffectAsset impactVfxAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(ForcefulStrikeImpactVfxAssetPath);
            if (impactVfxAsset == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find Forceful Strike Impact VFX asset at {ForcefulStrikeImpactVfxAssetPath}");
            }

            string[] handSideNames = { "LeftHand", "RightHand" };
            VisualEffect[] handVfx = new VisualEffect[2];
            for (int i = 0; i < 2; i++)
            {
                if (handVfxAsset != null)
                {
                    handVfx[i] = BuildHandVfx($"ForcefulStrikeVFX_{handSideNames[i]}", hands[i], handVfxAsset);
                }
            }

            VisualEffect impactVfx = null;
            if (impactVfxAsset != null)
            {
                GameObject impactObject = new GameObject("ForcefulStrikeImpactVFX", typeof(VisualEffect));
                impactObject.transform.SetParent(player.transform, false);
                impactVfx = impactObject.GetComponent<VisualEffect>();
                impactVfx.visualEffectAsset = impactVfxAsset;
            }

            AudioSource loopAudioSource = BuildLoopAudioSource("ForcefulStrikeAudioSource", player.transform);
            AudioClip chargeLoopClip = LoadAudioClip(LiteAudioFolder, "Lite Background 2");

            Transform existingImpactAudio = FindDescendant(player.transform, "ForcefulStrikeImpactAudioSource");
            GameObject impactAudioObject = existingImpactAudio != null ? existingImpactAudio.gameObject : new GameObject("ForcefulStrikeImpactAudioSource", typeof(AudioSource));
            impactAudioObject.transform.SetParent(player.transform, false);
            AudioSource impactAudioSource = impactAudioObject.GetComponent<AudioSource>();
            impactAudioSource.playOnAwake = false;
            impactAudioSource.loop = false;
            impactAudioSource.spatialBlend = 1f;

            AudioClip impactClip = LoadAudioClip(LiteAudioFolder, "Forceful Impact");

            ForcefulStrikeAbility abilityController = player.GetComponent<ForcefulStrikeAbility>();
            if (abilityController == null)
            {
                abilityController = player.AddComponent<ForcefulStrikeAbility>();
            }

            SerializedObject abilitySo = new SerializedObject(abilityController);
            AssignObjectArray(abilitySo, "handVfx", handVfx);
            abilitySo.FindProperty("impactVfx").objectReferenceValue = impactVfx;
            abilitySo.FindProperty("loopAudioSource").objectReferenceValue = loopAudioSource;
            abilitySo.FindProperty("chargeLoopClip").objectReferenceValue = chargeLoopClip;
            abilitySo.FindProperty("impactAudioSource").objectReferenceValue = impactAudioSource;
            abilitySo.FindProperty("impactClip").objectReferenceValue = impactClip;
            abilitySo.ApplyModifiedProperties();
        }

        // Ambient drifting motes — the original diffuse cloud, now warm-tinted and slightly
        // brighter at its core so it reads as a real glow instead of a haze.
        private static ParticleSystem BuildArmAuraParticles(string name, Transform boneParent, Transform towardBone, Material material)
        {
            GameObject psObject = new GameObject(name, typeof(ParticleSystem));
            psObject.transform.SetParent(boneParent, false);
            psObject.transform.position = Vector3.Lerp(boneParent.position, towardBone.position, 0.5f);

            float radius = Mathf.Max(0.05f, Vector3.Distance(boneParent.position, towardBone.position) * 0.45f);

            ParticleSystem system = psObject.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.5f, radius * 0.9f);
            main.startColor = AbilityAuraWarmWhite;
            main.maxParticles = 40;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 18f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
            shape.radiusThickness = 0.3f;

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.15f;
            noise.frequency = 0.4f;
            noise.scrollSpeed = 0.2f;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.3f),
                new Keyframe(0.3f, 1f),
                new Keyframe(1f, 0f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.65f, 0.2f),
                    new GradientAlphaKey(0.45f, 0.7f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = gradient;

            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = material;
            renderer.sortingFudge = 0f;

            return system;
        }

        // Rising wisps: fewer, longer-lived, stretched along their own velocity so they read as
        // faint curling tendrils of light drifting upward off the forearm, rather than more dust.
        private static ParticleSystem BuildArmWispParticles(string name, Transform boneParent, Transform towardBone, Material material)
        {
            GameObject psObject = new GameObject(name, typeof(ParticleSystem));
            psObject.transform.SetParent(boneParent, false);
            psObject.transform.position = Vector3.Lerp(boneParent.position, towardBone.position, 0.5f);

            float radius = Mathf.Max(0.04f, Vector3.Distance(boneParent.position, towardBone.position) * 0.3f);

            ParticleSystem system = psObject.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.6f, radius * 1.1f);
            main.startColor = AbilityAuraWarmWhite;
            main.maxParticles = 20;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 6f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
            shape.radiusThickness = 0.4f;

            ParticleSystem.VelocityOverLifetimeModule velocityOverLifetime = system.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            velocityOverLifetime.space = ParticleSystemSimulationSpace.Local;
            // x/y/z must all use the same MinMaxCurve mode (TwoConstants here) — leaving x/z at
            // their float-literal default would silently put them in a different mode and Unity
            // rejects the mismatch at runtime.
            velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(0.15f, 0.3f);
            velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.1f;
            noise.frequency = 0.3f;
            noise.scrollSpeed = 0.15f;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.5f, 0.25f),
                    new GradientAlphaKey(0.3f, 0.7f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = gradient;

            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 3.5f;
            renderer.material = material;

            return system;
        }

        // A thin capsule proxy wrapping the forearm, rendered with a Fresnel rim shader that's
        // invisible face-on and only blooms at grazing angles — this is what makes the *character*
        // look like it's glowing rather than just having sparkles floating nearby. World-space
        // position/rotation are derived from the real bone positions, so it can't come out rotated
        // wrong regardless of this rig's own bone-roll convention, and being a rigid child of the
        // forearm bone it tracks any arm animation perfectly (the bone's rotation IS the forearm's).
        private static void BuildArmGlowShell(string name, Transform elbow, Transform hand, Material material)
        {
            GameObject shellObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            shellObject.name = name;
            Collider shellCollider = shellObject.GetComponent<Collider>();
            if (shellCollider != null)
            {
                Object.DestroyImmediate(shellCollider);
            }

            shellObject.transform.SetParent(elbow, false);

            Vector3 midWorld = Vector3.Lerp(elbow.position, hand.position, 0.5f);
            float length = Vector3.Distance(elbow.position, hand.position);
            Vector3 directionWorld = (hand.position - elbow.position).normalized;

            shellObject.transform.position = midWorld;
            shellObject.transform.rotation = Quaternion.FromToRotation(Vector3.up, directionWorld);

            float radius = Mathf.Max(0.05f, length * 0.22f);
            // Default capsule primitive is 2 units tall / 0.5 radius at scale 1 — convert desired
            // world-space dimensions into that local scale, with a touch of overshoot past the
            // elbow/wrist so it fully wraps the joint rather than stopping short.
            shellObject.transform.localScale = new Vector3(radius * 2f, length * 0.525f, radius * 2f);

            MeshRenderer renderer = shellObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Otherwise DashGhostSpawner's "clone every renderer under the model" sweep picks this
            // up as if it were a real body part and spawns a ghost duplicate of it on every dash.
            shellObject.AddComponent<ExcludeFromDashGhost>();
        }

        // Graph's particle sizes/velocities are authored at a scale meant for a much bigger space
        // than "sitting on a hand" — shrinking the transform is the simplest way to bring the whole
        // effect down to hand-sized without editing the graph itself.
        private const float HandVfxScale = 0.06f;

        // Parented directly to the hand bone (rather than the player root, like the old removed
        // palm ring) — this is a hand-authored VFX Graph effect rather than a rigid fixed shape, so
        // it reads fine tracking the arm's swing the same way the motes/wisps already do. Stopped
        // immediately since it should only play while the ability is actually active.
        private static VisualEffect BuildHandVfx(string name, Transform hand, VisualEffectAsset asset)
        {
            GameObject vfxObject = new GameObject(name, typeof(VisualEffect));
            vfxObject.transform.SetParent(hand, false);
            vfxObject.transform.localPosition = Vector3.zero;
            vfxObject.transform.localScale = Vector3.one * HandVfxScale;

            VisualEffect visualEffect = vfxObject.GetComponent<VisualEffect>();
            visualEffect.visualEffectAsset = asset;
            visualEffect.Stop();

            return visualEffect;
        }

        // Dedicated AudioSource (separate from CharacterAudio's one-shot combat SFX source) so
        // LiteConcentrationAura can freely drive its volume for the loop's fade-out without
        // affecting punch/footstep sounds playing on the same character.
        private static AudioSource BuildLoopAudioSource(string name, Transform parent)
        {
            GameObject audioObject = new GameObject(name, typeof(AudioSource));
            audioObject.transform.SetParent(parent, false);

            AudioSource source = audioObject.GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;

            return source;
        }

        // One-shot bright pop at the palm the instant the ability is cast — gives casting a felt
        // "thump" of light, distinct from the calmer sustained glow that follows it.
        private static ParticleSystem BuildCastFlash(string name, Transform hand, Material material)
        {
            GameObject psObject = new GameObject(name, typeof(ParticleSystem));
            psObject.transform.SetParent(hand, false);
            psObject.transform.localPosition = Vector3.zero;

            ParticleSystem system = psObject.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 0.22f;
            main.startSpeed = 0f;
            main.startSize = 0.05f;
            main.startColor = AbilityAuraWarmWhite;
            main.maxParticles = 1;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.3f),
                new Keyframe(0.25f, 1f),
                new Keyframe(1f, 1.4f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.6f, 0.3f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = gradient;

            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = material;

            return system;
        }

        // One-shot expanding ring shockwave at the palm, timed with the cast flash above.
        private static ParticleSystem BuildCastRingBurst(string name, Transform hand, Material material)
        {
            GameObject psObject = new GameObject(name, typeof(ParticleSystem));
            psObject.transform.SetParent(hand, false);
            psObject.transform.localPosition = Vector3.zero;

            ParticleSystem system = psObject.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 0.3f;
            main.startSpeed = 0f;
            main.startSize = 0.05f;
            main.startColor = AbilityAuraWarmWhite;
            main.maxParticles = 1;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.2f),
                new Keyframe(1f, 3.2f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0.9f, 0f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = gradient;

            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = material;

            return system;
        }

        private static Light BuildArmAuraLight(string name, Transform boneParent, Transform towardBone)
        {
            GameObject lightObject = new GameObject(name, typeof(Light));
            lightObject.transform.SetParent(boneParent, false);
            lightObject.transform.position = Vector3.Lerp(boneParent.position, towardBone.position, 0.5f);

            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = AbilityAuraWarmWhite;
            light.range = 1.2f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;

            return light;
        }

        private static Material CreateOrLoadAuraMaterial(string shaderName, Texture texture, string materialPath)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogError($"[SceneBootstrapper] Could not find shader '{shaderName}' — has it finished importing/compiling yet?");
                    return null;
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            material.SetTexture("_MainTex", texture);
            material.SetColor("_Color", AbilityAuraWarmWhite);
            EditorUtility.SetDirty(material);

            return material;
        }

        private static Material CreateOrLoadRimGlowMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(AbilityAuraRimMaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find(AbilityAuraRimShaderName);
                if (shader == null)
                {
                    Debug.LogError($"[SceneBootstrapper] Could not find shader '{AbilityAuraRimShaderName}' — has it finished importing/compiling yet?");
                    return null;
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, AbilityAuraRimMaterialPath);
            }

            material.SetColor("_Color", AbilityAuraWarmWhite);
            material.SetFloat("_RimPower", 2.5f);
            material.SetFloat("_Intensity", 0f);
            EditorUtility.SetDirty(material);

            return material;
        }

        // A soft ring band sitting near the outer edge of the texture, rather than a crisp thin
        // border like the UI's CreateHollowRoundedRectSprite — this one needs to read as a glowing
        // magic circle from a distance, not a clean 9-sliced HUD outline.
        private static Sprite CreateGlowRingSprite()
        {
            const int size = 128;
            const float ringCenterFraction = 0.82f;
            const float ringThicknessFraction = 0.16f;

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float outerRadius = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float t = dist / outerRadius;

                    float distFromRingCenter = Mathf.Abs(t - ringCenterFraction);
                    float alpha = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(distFromRingCenter / ringThicknessFraction));

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(AbilityAuraRingSpritePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(AbilityAuraRingSpritePath, ImportAssetOptions.ForceUpdate);
            TextureImporter ringImporter = AssetImporter.GetAtPath(AbilityAuraRingSpritePath) as TextureImporter;
            if (ringImporter != null)
            {
                ringImporter.textureType = TextureImporterType.Sprite;
                ringImporter.spriteImportMode = SpriteImportMode.Single;
                ringImporter.alphaIsTransparency = true;
                ringImporter.mipmapEnabled = false;
                ringImporter.filterMode = FilterMode.Bilinear;
                ringImporter.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(AbilityAuraRingSpritePath);
        }

        // Pure white RGB with a bright core fading smoothly to fully transparent — tintable via
        // the material's _Color rather than a fixed hue baked into the texture like the UI's gold
        // CreateGlowCircleSprite, since this needs to stay white.
        private static Sprite CreateSoftWhiteGlowSprite()
        {
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float radius = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float t = dist / radius;

                    float alpha;
                    if (t <= 0.25f)
                    {
                        alpha = 1f;
                    }
                    else if (t <= 1f)
                    {
                        alpha = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.25f, 1f, t));
                    }
                    else
                    {
                        alpha = 0f;
                    }

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(AbilityAuraGlowSpritePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(AbilityAuraGlowSpritePath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(AbilityAuraGlowSpritePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(AbilityAuraGlowSpritePath);
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

        // Force-pushed here (rather than just changing Combatant's script defaults) because Setup
        // reuses an already-existing Combatant, which keeps whatever value was serialized the
        // first time it was added.
        private const float HitCooldownStunDuration = 1f;
        private const float KnockbackSlideDuration = AnimatorControllerBuilder.KnockbackDuration;
        private const float KnockbackAccelerationFraction = 0.15f;
        // Slide to a stop gradually across most of the slide instead of a sharp cutoff.
        private const float KnockbackStopFraction = 0.65f;
        private const float SlideAudioDelay = 0.65f;

        // Reset to neutral pending recalibration against the replacement Knockback clip — the
        // previous values (and correction curve below) were tuned to the old animation.
        private const float KnockbackGroundedFraction = 0.5f;

        // Player starts with 3x the base health so they can survive a lot more punishment than
        // the enemy — a plain design tweak, not something tied to enemy balance.
        private const int PlayerMaxHealth = 300;

        private static void ApplyCombatantTiming(Combatant combatant, int? maxHealthOverride = null)
        {
            SerializedObject so = new SerializedObject(combatant);

            if (maxHealthOverride.HasValue)
            {
                SerializedProperty maxHealthProp = so.FindProperty("maxHealth");
                if (maxHealthProp != null)
                {
                    maxHealthProp.intValue = maxHealthOverride.Value;
                }
            }

            SerializedProperty stunProp = so.FindProperty("stunDuration");
            if (stunProp != null)
            {
                stunProp.floatValue = HitCooldownStunDuration;
            }

            SerializedProperty knockbackDurationProp = so.FindProperty("knockbackDuration");
            if (knockbackDurationProp != null)
            {
                knockbackDurationProp.floatValue = KnockbackSlideDuration;
            }

            SerializedProperty knockbackAccelerationFractionProp = so.FindProperty("knockbackAccelerationFraction");
            if (knockbackAccelerationFractionProp != null)
            {
                knockbackAccelerationFractionProp.floatValue = KnockbackAccelerationFraction;
            }

            SerializedProperty knockbackStopFractionProp = so.FindProperty("knockbackStopFraction");
            if (knockbackStopFractionProp != null)
            {
                knockbackStopFractionProp.floatValue = KnockbackStopFraction;
            }

            SerializedProperty slideAudioDelayProp = so.FindProperty("slideAudioDelay");
            if (slideAudioDelayProp != null)
            {
                slideAudioDelayProp.floatValue = SlideAudioDelay;
            }

            SerializedProperty knockbackGroundedFractionProp = so.FindProperty("knockbackGroundedFraction");
            if (knockbackGroundedFractionProp != null)
            {
                knockbackGroundedFractionProp.floatValue = KnockbackGroundedFraction;
            }

            SerializedProperty groundedCorrectionCurveProp = so.FindProperty("knockbackGroundedCorrectionCurve");
            if (groundedCorrectionCurveProp != null)
            {
                groundedCorrectionCurveProp.animationCurveValue = KnockbackGroundedCorrectionCurve;
            }
            so.ApplyModifiedProperties();
        }

        // Reset to empty (no correction) pending recalibration against the replacement clip.
        // Force-pushed here since Setup reuses an already-existing Combatant, which keeps
        // whatever curve was serialized the first time it was added.
        private static readonly AnimationCurve KnockbackGroundedCorrectionCurve = new AnimationCurve();

        // Scaled down to a 0.4s total re-trigger time per follow-up request, keeping the same
        // guard:vulnerable ratio as before (was 0.4/0.3).
        private const float GuardDuration = 0.23f;
        private const float GuardVulnerableDuration = 0.17f;

        private static void ApplyBlockDodgeTiming(BlockDodge blockDodge, bool respondToKeyboardInput)
        {
            SerializedObject so = new SerializedObject(blockDodge);

            SerializedProperty guardDurationProp = so.FindProperty("guardDuration");
            if (guardDurationProp != null)
            {
                guardDurationProp.floatValue = GuardDuration;
            }

            SerializedProperty vulnerableDurationProp = so.FindProperty("vulnerableDuration");
            if (vulnerableDurationProp != null)
            {
                vulnerableDurationProp.floatValue = GuardVulnerableDuration;
            }

            // The AI drives its own guard via EnemyController — it must not also react to the
            // player's F key, which is the only reason this flag exists.
            SerializedProperty respondToInputProp = so.FindProperty("respondToKeyboardInput");
            if (respondToInputProp != null)
            {
                respondToInputProp.boolValue = respondToKeyboardInput;
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

        private const string FightAudioFolder = "Assets/_Project/Audio/FightAudio";
        private const string MovementAudioFolder = "Assets/_Project/Audio/MovementAudio";

        private static void PopulateCharacterAudio(CharacterAudio characterAudio)
        {
            if (characterAudio == null)
            {
                return;
            }

            SerializedObject so = new SerializedObject(characterAudio);

            SetClipArray(so, "walkClips", LoadClipsWithPrefix(MovementAudioFolder, "walk"));
            SetClipArray(so, "runClips", LoadClipsWithPrefix(MovementAudioFolder, "run"));
            SetClipArray(so, "punchImpactClips", LoadClipsWithPrefix(FightAudioFolder, "punch"));

            SetClip(so, "dashClip", LoadAudioClip(MovementAudioFolder, "dash"));
            SetClip(so, "slideClip", LoadAudioClip(MovementAudioFolder, "slide"));
            SetClip(so, "jumpTakeoffClip", LoadAudioClip(MovementAudioFolder, "jumpstart"));
            SetClip(so, "jumpLandClip", LoadAudioClip(MovementAudioFolder, "jumpland"));
            SetClip(so, "heavyPunchImpactClip", LoadAudioClip(FightAudioFolder, "hardpunch"));

            SetClip(so, "guardBlockHitClip", LoadAudioClip(FightAudioFolder, "blockhit"));
            SetClip(so, "guardDodgeHitClip", LoadAudioClip(FightAudioFolder, "dodge"));
            SetClip(so, "blockBreakClip", LoadAudioClip(FightAudioFolder, "blockbreak"));

            so.ApplyModifiedProperties();
        }

        private static void SetClip(SerializedObject so, string propertyName, AudioClip clip)
        {
            SerializedProperty prop = so.FindProperty(propertyName);
            if (prop != null)
            {
                prop.objectReferenceValue = clip;
            }
        }

        private static void SetClipArray(SerializedObject so, string propertyName, AudioClip[] clips)
        {
            SerializedProperty prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                return;
            }

            prop.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            }
        }

        private static AudioClip LoadAudioClip(string folder, string name)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{folder}/{name}.mp3");
            if (clip == null)
            {
                Debug.LogWarning($"[SceneBootstrapper] Could not find audio clip at {folder}/{name}.mp3");
            }

            return clip;
        }

        private static AudioClip[] LoadClipsWithPrefix(string folder, string prefix)
        {
            List<AudioClip> clips = new List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (!fileName.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null)
                {
                    clips.Add(clip);
                }
            }

            if (clips.Count == 0)
            {
                Debug.LogWarning($"[SceneBootstrapper] No audio clips found in {folder} starting with '{prefix}'.");
            }

            return clips.ToArray();
        }

        private const string HealthbarShapePath = "Assets/_Project/Art/UI/HealthbarShape.png";
        private const string HealthbarFillShapePath = "Assets/_Project/Art/UI/HealthbarFillShape.png";
        private const string AbilityHotbarHudRingPath = "Assets/_Project/Art/UI/AbilityHotbarHudRing.png";
        private const string GameFontPath = "Assets/_Project/Art/Fonts/Cinzel.ttf";

        // Falls back to Unity's built-in font if Cinzel hasn't been imported yet, so a missing
        // font asset doesn't break every UI-building script that calls this.
        internal static Font GetGameFont()
        {
            return AssetDatabase.LoadAssetAtPath<Font>(GameFontPath) ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        private const int HealthBarChunkCount = 10;

        // GameObject.Find only searches active objects, so a PlayerHUD that was ever disabled
        // (e.g. an old build that hid it via SetActive(false)) becomes permanently invisible to
        // it — later Setup runs would then create a second, active PlayerHUD instead of replacing
        // the orphaned one, leaving stale duplicates behind forever. This sweeps every root object
        // with the given name, active or not, so re-running Setup always ends up with exactly one.
        private static void DestroyAllRootObjectsNamed(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == name)
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        private static void SetupPlayerHealthUI(Combatant combatant)
        {
            DestroyAllRootObjectsNamed("PlayerHUD");

            GameObject canvasObject = new GameObject("PlayerHUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            GameObject barObject = new GameObject("Healthbar", typeof(RectTransform));
            barObject.transform.SetParent(canvasObject.transform, false);

            RectTransform barRect = barObject.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0.5f, 0f);
            barRect.anchorMax = new Vector2(0.5f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = new Vector2(0f, 30f);
            barRect.sizeDelta = new Vector2(300f, 36f);

            const float borderThickness = 3f;
            // The fill/trail bars are plain flat-cornered rects (Image.Type.Filled ignores 9-slice
            // border data, so a sprite-based rounded corner would stretch/distort on them). Insetting
            // them further than Track keeps their square corners safely inside Track's curve instead
            // of poking out past it.
            const float fillInset = 8f;
            Sprite shapeSprite = CreateRoundedRectSprite();
            Sprite solidSprite = CreateSolidSprite();

            // Thin dark outline behind everything — a simple flat-color rounded rect.
            GameObject borderObject = new GameObject("Border", typeof(Image));
            borderObject.transform.SetParent(barObject.transform, false);
            StretchRect(borderObject.GetComponent<RectTransform>());
            Image borderImage = borderObject.GetComponent<Image>();
            borderImage.sprite = shapeSprite;
            borderImage.type = Image.Type.Sliced;
            borderImage.color = new Color(0.06f, 0.06f, 0.07f);
            borderImage.raycastTarget = false;

            // Dark "track" showing through wherever health has actually been lost.
            GameObject trackObject = new GameObject("Track", typeof(Image));
            trackObject.transform.SetParent(barObject.transform, false);
            InsetRect(trackObject.GetComponent<RectTransform>(), borderThickness);
            Image trackImage = trackObject.GetComponent<Image>();
            trackImage.sprite = shapeSprite;
            trackImage.type = Image.Type.Sliced;
            trackImage.color = new Color(0.15f, 0.05f, 0.05f);
            trackImage.raycastTarget = false;

            GameObject trailObject = new GameObject("DamageTrail", typeof(Image));
            trailObject.transform.SetParent(barObject.transform, false);
            InsetRect(trailObject.GetComponent<RectTransform>(), fillInset);
            Image damageTrailImage = trailObject.GetComponent<Image>();
            damageTrailImage.sprite = solidSprite;
            damageTrailImage.type = Image.Type.Filled;
            damageTrailImage.fillMethod = Image.FillMethod.Horizontal;
            damageTrailImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            damageTrailImage.fillAmount = 1f;
            damageTrailImage.color = new Color(0.95f, 0.75f, 0.15f);
            damageTrailImage.raycastTarget = false;

            // The real health value — always green, updates instantly, drawn over the trail.
            GameObject fillObject = new GameObject("HealthFill", typeof(Image));
            fillObject.transform.SetParent(barObject.transform, false);
            InsetRect(fillObject.GetComponent<RectTransform>(), fillInset);
            Image healthFillImage = fillObject.GetComponent<Image>();
            healthFillImage.sprite = solidSprite;
            healthFillImage.type = Image.Type.Filled;
            healthFillImage.fillMethod = Image.FillMethod.Horizontal;
            healthFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            healthFillImage.fillAmount = 1f;
            healthFillImage.color = new Color(0.25f, 0.85f, 0.25f);
            healthFillImage.raycastTarget = false;

            // Decorative segment dividers — purely visual "chunks" look, independent of
            // fillAmount, so chunks appear to deplete one at a time as the fill/trail pass them.
            GameObject dividersContainer = new GameObject("ChunkDividers", typeof(RectTransform));
            dividersContainer.transform.SetParent(barObject.transform, false);
            InsetRect(dividersContainer.GetComponent<RectTransform>(), fillInset);

            for (int i = 1; i < HealthBarChunkCount; i++)
            {
                float x = i / (float)HealthBarChunkCount;
                GameObject dividerObject = new GameObject($"Divider{i}", typeof(Image));
                dividerObject.transform.SetParent(dividersContainer.transform, false);

                RectTransform dividerRect = dividerObject.GetComponent<RectTransform>();
                dividerRect.anchorMin = new Vector2(x, 0f);
                dividerRect.anchorMax = new Vector2(x, 1f);
                dividerRect.pivot = new Vector2(0.5f, 0.5f);
                dividerRect.sizeDelta = new Vector2(2f, 0f);
                dividerRect.anchoredPosition = Vector2.zero;

                Image dividerImage = dividerObject.GetComponent<Image>();
                dividerImage.color = new Color(0f, 0f, 0f, 0.3f);
                dividerImage.raycastTarget = false;
            }

            PlayerHealthUI healthUI = canvasObject.AddComponent<PlayerHealthUI>();
            SerializedObject so = new SerializedObject(healthUI);
            so.FindProperty("combatant").objectReferenceValue = combatant;
            so.FindProperty("punchTarget").objectReferenceValue = barRect;
            so.FindProperty("healthFillImage").objectReferenceValue = healthFillImage;
            so.FindProperty("damageTrailImage").objectReferenceValue = damageTrailImage;
            so.ApplyModifiedProperties();

            SetupNPCChatUI(canvasObject.transform, barRect, shapeSprite, solidSprite);
            SetupQuestTrackerUI(canvasObject.transform, shapeSprite, solidSprite);
            SetupXpBarUI(canvasObject.transform, combatant.gameObject, shapeSprite, solidSprite);
            SetupAbilityHotbarHudUI(canvasObject.transform, shapeSprite, solidSprite);

            if (canvasObject.GetComponent<QuestLog>() == null)
            {
                canvasObject.AddComponent<QuestLog>();
            }
        }

        internal static void EnsureEventSystem()
        {
            // Legacy StandaloneInputModule only reads the old Input Manager, which this project
            // doesn't use — UI clicks/typing need the New Input System's own UI module instead.
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.GetComponent<EventSystem>() != null)
                {
                    return;
                }
            }

            GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(eventSystemObject, "Create EventSystem");

            // AddComponent doesn't trigger the Reset() callback that normally wires up default
            // point/click/navigate bindings when you add this via the Inspector — do it explicitly
            // so clicking/typing on UI actually works.
            eventSystemObject.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        // Chat panel sits directly above the health bar, sharing the same PlayerHUD canvas.
        private static void SetupNPCChatUI(Transform hudRoot, RectTransform healthBarRect, Sprite shapeSprite, Sprite solidSprite)
        {
            EnsureEventSystem();

            Transform existing = hudRoot.Find("NPCChatPanel");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            const float panelWidth = 420f;
            const float panelHeight = 240f;
            const float gapAboveHealthBar = 14f;
            const float panelBorderThickness = 3f;
            const float panelPadding = 14f;

            float panelBottomY = healthBarRect.anchoredPosition.y + healthBarRect.sizeDelta.y + gapAboveHealthBar;

            GameObject panelObject = new GameObject("NPCChatPanel", typeof(RectTransform));
            panelObject.transform.SetParent(hudRoot, false);
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, panelBottomY);
            panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

            GameObject borderObject = new GameObject("Border", typeof(Image));
            borderObject.transform.SetParent(panelObject.transform, false);
            StretchRect(borderObject.GetComponent<RectTransform>());
            Image borderImage = borderObject.GetComponent<Image>();
            borderImage.sprite = shapeSprite;
            borderImage.type = Image.Type.Sliced;
            borderImage.color = new Color(0.08f, 0.08f, 0.09f, 0.9f);
            borderImage.raycastTarget = true;

            GameObject backgroundObject = new GameObject("Background", typeof(Image));
            backgroundObject.transform.SetParent(panelObject.transform, false);
            InsetRect(backgroundObject.GetComponent<RectTransform>(), panelBorderThickness);
            Image backgroundImage = backgroundObject.GetComponent<Image>();
            backgroundImage.sprite = shapeSprite;
            backgroundImage.type = Image.Type.Sliced;
            backgroundImage.color = new Color(0.28f, 0.28f, 0.3f, 0.7f);
            backgroundImage.raycastTarget = true;

            GameObject nameObject = new GameObject("NameText", typeof(Text));
            nameObject.transform.SetParent(panelObject.transform, false);
            RectTransform nameRect = nameObject.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.anchoredPosition = new Vector2(0f, -panelPadding);
            nameRect.sizeDelta = new Vector2(-panelPadding * 2f, 28f);
            Text nameText = nameObject.GetComponent<Text>();
            nameText.font = GetGameFont();
            nameText.fontSize = 20;
            nameText.fontStyle = FontStyle.Bold;
            nameText.color = new Color(1f, 1f, 1f, 0.95f);
            nameText.alignment = TextAnchor.MiddleLeft;
            nameText.text = "Quest Giver";
            nameText.raycastTarget = false;

            GameObject separatorObject = new GameObject("Separator", typeof(Image));
            separatorObject.transform.SetParent(panelObject.transform, false);
            RectTransform separatorRect = separatorObject.GetComponent<RectTransform>();
            separatorRect.anchorMin = new Vector2(0f, 1f);
            separatorRect.anchorMax = new Vector2(1f, 1f);
            separatorRect.pivot = new Vector2(0.5f, 1f);
            separatorRect.anchoredPosition = new Vector2(0f, -(panelPadding + 30f));
            separatorRect.sizeDelta = new Vector2(-panelPadding * 2f, 2f);
            Image separatorImage = separatorObject.GetComponent<Image>();
            separatorImage.color = new Color(1f, 1f, 1f, 0.25f);
            separatorImage.raycastTarget = false;

            GameObject dialogueObject = new GameObject("DialogueText", typeof(Text));
            dialogueObject.transform.SetParent(panelObject.transform, false);
            RectTransform dialogueRect = dialogueObject.GetComponent<RectTransform>();
            dialogueRect.anchorMin = new Vector2(0f, 0f);
            dialogueRect.anchorMax = new Vector2(1f, 1f);
            // Bottom inset leaves room for the AcceptQuestButton strip below (it occupies no
            // visual space when hidden, but the layout reserves its spot either way).
            dialogueRect.offsetMin = new Vector2(panelPadding, 88f);
            dialogueRect.offsetMax = new Vector2(-panelPadding, -(panelPadding + 40f));
            Text dialogueText = dialogueObject.GetComponent<Text>();
            dialogueText.font = GetGameFont();
            dialogueText.fontSize = 16;
            dialogueText.color = new Color(1f, 1f, 1f, 0.9f);
            dialogueText.alignment = TextAnchor.UpperLeft;
            dialogueText.horizontalOverflow = HorizontalWrapMode.Wrap;
            dialogueText.verticalOverflow = VerticalWrapMode.Truncate;
            dialogueText.text = "...";
            dialogueText.raycastTarget = false;

            GameObject acceptQuestButtonObject = new GameObject("AcceptQuestButton", typeof(Image), typeof(Button));
            acceptQuestButtonObject.transform.SetParent(panelObject.transform, false);
            RectTransform acceptQuestButtonRect = acceptQuestButtonObject.GetComponent<RectTransform>();
            acceptQuestButtonRect.anchorMin = new Vector2(0f, 0f);
            acceptQuestButtonRect.anchorMax = new Vector2(1f, 0f);
            acceptQuestButtonRect.pivot = new Vector2(0.5f, 0f);
            acceptQuestButtonRect.anchoredPosition = new Vector2(0f, 50f);
            acceptQuestButtonRect.sizeDelta = new Vector2(-panelPadding * 2f, 30f);
            Image acceptQuestButtonImage = acceptQuestButtonObject.GetComponent<Image>();
            acceptQuestButtonImage.sprite = shapeSprite;
            acceptQuestButtonImage.type = Image.Type.Sliced;
            acceptQuestButtonImage.color = new Color(0.55f, 0.45f, 0.12f, 0.9f);

            GameObject acceptQuestButtonTextObject = new GameObject("Text", typeof(Text));
            acceptQuestButtonTextObject.transform.SetParent(acceptQuestButtonObject.transform, false);
            StretchRect(acceptQuestButtonTextObject.GetComponent<RectTransform>());
            Text acceptQuestButtonText = acceptQuestButtonTextObject.GetComponent<Text>();
            acceptQuestButtonText.font = GetGameFont();
            acceptQuestButtonText.fontSize = 15;
            acceptQuestButtonText.fontStyle = FontStyle.Bold;
            acceptQuestButtonText.color = Color.white;
            acceptQuestButtonText.alignment = TextAnchor.MiddleCenter;
            acceptQuestButtonText.text = "Accept Quest";
            acceptQuestButtonText.raycastTarget = false;

            Button acceptQuestButton = acceptQuestButtonObject.GetComponent<Button>();
            acceptQuestButton.targetGraphic = acceptQuestButtonImage;

            GameObject inputFieldObject = new GameObject("MessageInput", typeof(Image), typeof(InputField));
            inputFieldObject.transform.SetParent(panelObject.transform, false);
            RectTransform inputRect = inputFieldObject.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(1f, 0f);
            inputRect.pivot = new Vector2(0.5f, 0f);
            inputRect.anchoredPosition = new Vector2(0f, panelPadding);
            inputRect.sizeDelta = new Vector2(-panelPadding * 2f, 32f);
            Image inputBackground = inputFieldObject.GetComponent<Image>();
            inputBackground.sprite = solidSprite;
            inputBackground.type = Image.Type.Simple;
            inputBackground.color = new Color(0.12f, 0.12f, 0.13f, 0.85f);

            GameObject inputTextObject = new GameObject("Text", typeof(Text));
            inputTextObject.transform.SetParent(inputFieldObject.transform, false);
            RectTransform inputTextRect = inputTextObject.GetComponent<RectTransform>();
            inputTextRect.anchorMin = Vector2.zero;
            inputTextRect.anchorMax = Vector2.one;
            inputTextRect.offsetMin = new Vector2(8f, 4f);
            inputTextRect.offsetMax = new Vector2(-8f, -4f);
            Text inputText = inputTextObject.GetComponent<Text>();
            inputText.font = GetGameFont();
            inputText.fontSize = 15;
            inputText.color = Color.white;
            inputText.alignment = TextAnchor.MiddleLeft;
            inputText.supportRichText = false;

            GameObject placeholderObject = new GameObject("Placeholder", typeof(Text));
            placeholderObject.transform.SetParent(inputFieldObject.transform, false);
            RectTransform placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(8f, 4f);
            placeholderRect.offsetMax = new Vector2(-8f, -4f);
            Text placeholderText = placeholderObject.GetComponent<Text>();
            placeholderText.font = GetGameFont();
            placeholderText.fontSize = 15;
            placeholderText.fontStyle = FontStyle.Italic;
            placeholderText.color = new Color(1f, 1f, 1f, 0.4f);
            placeholderText.alignment = TextAnchor.MiddleLeft;
            placeholderText.text = "Type a message...";

            InputField inputField = inputFieldObject.GetComponent<InputField>();
            inputField.textComponent = inputText;
            inputField.placeholder = placeholderText;
            inputField.lineType = InputField.LineType.SingleLine;

            panelObject.SetActive(false);

            NPCChatUI chatUI = hudRoot.gameObject.GetComponent<NPCChatUI>();
            if (chatUI == null)
            {
                chatUI = hudRoot.gameObject.AddComponent<NPCChatUI>();
            }

            SerializedObject chatSo = new SerializedObject(chatUI);
            chatSo.FindProperty("panelRoot").objectReferenceValue = panelObject;
            chatSo.FindProperty("panelRect").objectReferenceValue = panelRect;
            chatSo.FindProperty("nameText").objectReferenceValue = nameText;
            chatSo.FindProperty("dialogueText").objectReferenceValue = dialogueText;
            chatSo.FindProperty("messageInputField").objectReferenceValue = inputField;
            chatSo.FindProperty("acceptQuestButton").objectReferenceValue = acceptQuestButton;
            chatSo.FindProperty("acceptQuestButtonText").objectReferenceValue = acceptQuestButtonText;
            chatSo.ApplyModifiedProperties();
        }

        // Top-left on-screen tracker showing the currently active quest — title, objective, and a
        // left-to-right progress bar. Hidden until a quest is accepted (QuestTrackerUI itself
        // reacts to QuestLog's events at runtime); this just builds the structure.
        private static void SetupQuestTrackerUI(Transform hudRoot, Sprite shapeSprite, Sprite solidSprite)
        {
            Transform existing = hudRoot.Find("QuestTrackerPanel");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            const float panelWidth = 300f;
            const float panelHeight = 140f;
            const float panelBorderThickness = 3f;
            const float panelPadding = 12f;

            GameObject panelObject = new GameObject("QuestTrackerPanel", typeof(RectTransform));
            panelObject.transform.SetParent(hudRoot, false);
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(20f, -20f);
            panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);

            GameObject borderObject = new GameObject("Border", typeof(Image));
            borderObject.transform.SetParent(panelObject.transform, false);
            StretchRect(borderObject.GetComponent<RectTransform>());
            Image borderImage = borderObject.GetComponent<Image>();
            borderImage.sprite = shapeSprite;
            borderImage.type = Image.Type.Sliced;
            borderImage.color = new Color(0.08f, 0.08f, 0.09f, 0.9f);
            borderImage.raycastTarget = false;

            GameObject backgroundObject = new GameObject("Background", typeof(Image));
            backgroundObject.transform.SetParent(panelObject.transform, false);
            InsetRect(backgroundObject.GetComponent<RectTransform>(), panelBorderThickness);
            Image backgroundImage = backgroundObject.GetComponent<Image>();
            backgroundImage.sprite = shapeSprite;
            backgroundImage.type = Image.Type.Sliced;
            backgroundImage.color = new Color(0.28f, 0.28f, 0.3f, 0.7f);
            backgroundImage.raycastTarget = false;

            GameObject titleObject = new GameObject("TitleText", typeof(Text));
            titleObject.transform.SetParent(panelObject.transform, false);
            RectTransform titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -panelPadding);
            titleRect.sizeDelta = new Vector2(-panelPadding * 2f, 24f);
            Text titleText = titleObject.GetComponent<Text>();
            titleText.font = GetGameFont();
            titleText.fontSize = 18;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.text = "Quest Title";
            titleText.raycastTarget = false;

            GameObject objectiveObject = new GameObject("ObjectiveText", typeof(Text));
            objectiveObject.transform.SetParent(panelObject.transform, false);
            RectTransform objectiveRect = objectiveObject.GetComponent<RectTransform>();
            objectiveRect.anchorMin = new Vector2(0f, 1f);
            objectiveRect.anchorMax = new Vector2(1f, 1f);
            objectiveRect.pivot = new Vector2(0.5f, 1f);
            objectiveRect.anchoredPosition = new Vector2(0f, -(panelPadding + 28f));
            objectiveRect.sizeDelta = new Vector2(-panelPadding * 2f, 34f);
            Text objectiveText = objectiveObject.GetComponent<Text>();
            objectiveText.font = GetGameFont();
            objectiveText.fontSize = 13;
            objectiveText.color = new Color(1f, 1f, 1f, 0.85f);
            objectiveText.alignment = TextAnchor.UpperLeft;
            objectiveText.horizontalOverflow = HorizontalWrapMode.Wrap;
            objectiveText.verticalOverflow = VerticalWrapMode.Truncate;
            objectiveText.text = "Objective";
            objectiveText.raycastTarget = false;

            const float barBorderThickness = 2f;
            const float barFillInset = 5f;

            GameObject barContainer = new GameObject("ProgressBar", typeof(RectTransform));
            barContainer.transform.SetParent(panelObject.transform, false);
            RectTransform barRect = barContainer.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.anchoredPosition = new Vector2(0f, -(panelPadding + 28f + 40f));
            barRect.sizeDelta = new Vector2(-panelPadding * 2f, 20f);

            GameObject barBorderObject = new GameObject("Border", typeof(Image));
            barBorderObject.transform.SetParent(barContainer.transform, false);
            StretchRect(barBorderObject.GetComponent<RectTransform>());
            Image barBorderImage = barBorderObject.GetComponent<Image>();
            barBorderImage.sprite = shapeSprite;
            barBorderImage.type = Image.Type.Sliced;
            barBorderImage.color = new Color(0.06f, 0.06f, 0.07f);
            barBorderImage.raycastTarget = false;

            GameObject barTrackObject = new GameObject("Track", typeof(Image));
            barTrackObject.transform.SetParent(barContainer.transform, false);
            InsetRect(barTrackObject.GetComponent<RectTransform>(), barBorderThickness);
            Image barTrackImage = barTrackObject.GetComponent<Image>();
            barTrackImage.sprite = shapeSprite;
            barTrackImage.type = Image.Type.Sliced;
            barTrackImage.color = new Color(0.15f, 0.13f, 0.08f);
            barTrackImage.raycastTarget = false;

            // Flat-cornered like the health bar's fill — Image.Type.Filled ignores 9-slice border
            // data, and this needs a real (non-null) sprite or fillAmount silently renders as if
            // always full regardless of its actual value.
            GameObject barFillObject = new GameObject("Fill", typeof(Image));
            barFillObject.transform.SetParent(barContainer.transform, false);
            InsetRect(barFillObject.GetComponent<RectTransform>(), barFillInset);
            Image barFillImage = barFillObject.GetComponent<Image>();
            barFillImage.sprite = solidSprite;
            barFillImage.type = Image.Type.Filled;
            barFillImage.fillMethod = Image.FillMethod.Horizontal;
            barFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            barFillImage.fillAmount = 0f;
            barFillImage.color = new Color(0.85f, 0.65f, 0.2f);
            barFillImage.raycastTarget = false;

            GameObject progressTextObject = new GameObject("ProgressText", typeof(Text));
            progressTextObject.transform.SetParent(panelObject.transform, false);
            RectTransform progressTextRect = progressTextObject.GetComponent<RectTransform>();
            progressTextRect.anchorMin = new Vector2(0f, 1f);
            progressTextRect.anchorMax = new Vector2(1f, 1f);
            progressTextRect.pivot = new Vector2(0.5f, 1f);
            progressTextRect.anchoredPosition = new Vector2(0f, -(panelPadding + 28f + 40f + 24f));
            progressTextRect.sizeDelta = new Vector2(-panelPadding * 2f, 18f);
            Text progressText = progressTextObject.GetComponent<Text>();
            progressText.font = GetGameFont();
            progressText.fontSize = 13;
            progressText.color = new Color(1f, 1f, 1f, 0.85f);
            progressText.alignment = TextAnchor.MiddleCenter;
            progressText.text = "0/0";
            progressText.raycastTarget = false;

            panelObject.SetActive(false);

            QuestTrackerUI trackerUI = hudRoot.gameObject.GetComponent<QuestTrackerUI>();
            if (trackerUI == null)
            {
                trackerUI = hudRoot.gameObject.AddComponent<QuestTrackerUI>();
            }

            SerializedObject trackerSo = new SerializedObject(trackerUI);
            trackerSo.FindProperty("panelRoot").objectReferenceValue = panelObject;
            trackerSo.FindProperty("titleText").objectReferenceValue = titleText;
            trackerSo.FindProperty("objectiveText").objectReferenceValue = objectiveText;
            trackerSo.FindProperty("progressText").objectReferenceValue = progressText;
            trackerSo.FindProperty("progressFillImage").objectReferenceValue = barFillImage;
            trackerSo.ApplyModifiedProperties();
        }

        // Hidden until the player gains XP, then fades in across most of the screen width: a
        // level badge on the left (matching the Stat Menu's level circle), a long bar next to it,
        // and a fraction readout underneath. See XpBarUI for the fill/chunk/fade animation.
        private static void SetupXpBarUI(Transform hudRoot, GameObject player, Sprite shapeSprite, Sprite solidSprite)
        {
            Transform existing = hudRoot.Find("XpHudBar");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            PlayerStats playerStats = player.GetComponent<PlayerStats>();
            if (playerStats == null)
            {
                playerStats = player.AddComponent<PlayerStats>();
            }

            const float rowHeight = 28f;
            const float badgeSize = 28f;
            const float gapAfterBadge = 8f;
            const float barHeight = 14f;
            const float barBorderThickness = 3f;
            const float barFillInset = 4f;
            const float fractionGap = 4f;
            const float fractionHeight = 16f;
            const float rootHeight = rowHeight + fractionGap + fractionHeight;

            GameObject rootObject = new GameObject("XpHudBar", typeof(RectTransform), typeof(CanvasGroup));
            rootObject.transform.SetParent(hudRoot, false);
            RectTransform rootRect = rootObject.GetComponent<RectTransform>();
            // Proportional anchors (rather than a fixed-pixel margin off full width) so this is
            // always exactly the middle third of the screen, regardless of resolution.
            rootRect.anchorMin = new Vector2(1f / 3f, 1f);
            rootRect.anchorMax = new Vector2(2f / 3f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = new Vector2(0f, -32f);
            rootRect.sizeDelta = new Vector2(0f, rootHeight);

            GameObject barRow = new GameObject("BarRow", typeof(RectTransform));
            barRow.transform.SetParent(rootObject.transform, false);
            RectTransform barRowRect = barRow.GetComponent<RectTransform>();
            barRowRect.anchorMin = new Vector2(0f, 1f);
            barRowRect.anchorMax = new Vector2(1f, 1f);
            barRowRect.pivot = new Vector2(0.5f, 1f);
            barRowRect.anchoredPosition = Vector2.zero;
            barRowRect.sizeDelta = new Vector2(0f, rowHeight);

            GameObject badgeObject = new GameObject("LevelBadge", typeof(Image));
            badgeObject.transform.SetParent(barRow.transform, false);
            RectTransform badgeRect = badgeObject.GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0f, 0.5f);
            badgeRect.anchorMax = new Vector2(0f, 0.5f);
            badgeRect.pivot = new Vector2(0f, 0.5f);
            badgeRect.anchoredPosition = Vector2.zero;
            badgeRect.sizeDelta = new Vector2(badgeSize, badgeSize);
            Image badgeImage = badgeObject.GetComponent<Image>();
            badgeImage.sprite = shapeSprite;
            badgeImage.type = Image.Type.Sliced;
            badgeImage.color = new Color(0.08f, 0.08f, 0.09f, 0.95f);
            badgeImage.raycastTarget = false;

            GameObject levelTextObject = new GameObject("Text", typeof(Text));
            levelTextObject.transform.SetParent(badgeObject.transform, false);
            StretchRect(levelTextObject.GetComponent<RectTransform>());
            Text levelText = levelTextObject.GetComponent<Text>();
            levelText.font = GetGameFont();
            levelText.fontSize = 13;
            levelText.fontStyle = FontStyle.Bold;
            levelText.color = Color.white;
            levelText.alignment = TextAnchor.MiddleCenter;
            levelText.text = "1";
            levelText.raycastTarget = false;

            GameObject barContainer = new GameObject("BarContainer", typeof(RectTransform));
            barContainer.transform.SetParent(barRow.transform, false);
            RectTransform barContainerRect = barContainer.GetComponent<RectTransform>();
            barContainerRect.anchorMin = Vector2.zero;
            barContainerRect.anchorMax = Vector2.one;
            barContainerRect.offsetMin = new Vector2(badgeSize + gapAfterBadge, (rowHeight - barHeight) * 0.5f);
            barContainerRect.offsetMax = new Vector2(0f, -(rowHeight - barHeight) * 0.5f);

            GameObject barBorderObject = new GameObject("Border", typeof(Image));
            barBorderObject.transform.SetParent(barContainer.transform, false);
            StretchRect(barBorderObject.GetComponent<RectTransform>());
            Image barBorderImage = barBorderObject.GetComponent<Image>();
            barBorderImage.sprite = shapeSprite;
            barBorderImage.type = Image.Type.Sliced;
            barBorderImage.color = new Color(0.06f, 0.06f, 0.07f);
            barBorderImage.raycastTarget = false;

            GameObject barTrackObject = new GameObject("Track", typeof(Image));
            barTrackObject.transform.SetParent(barContainer.transform, false);
            InsetRect(barTrackObject.GetComponent<RectTransform>(), barBorderThickness);
            Image barTrackImage = barTrackObject.GetComponent<Image>();
            barTrackImage.sprite = shapeSprite;
            barTrackImage.type = Image.Type.Sliced;
            barTrackImage.color = new Color(0.12f, 0.12f, 0.14f);
            barTrackImage.raycastTarget = false;

            // Sits behind Fill — on an XP gain it snaps ahead to the new target instantly while
            // Fill (drawn on top) grows up to meet it, the mirror of the health bar's damage trail
            // (there the trail lags behind and drains down; here it jumps ahead and gets caught up to).
            GameObject gainGhostObject = new GameObject("GainGhost", typeof(Image));
            gainGhostObject.transform.SetParent(barContainer.transform, false);
            InsetRect(gainGhostObject.GetComponent<RectTransform>(), barFillInset);
            Image gainGhostImage = gainGhostObject.GetComponent<Image>();
            gainGhostImage.sprite = solidSprite;
            gainGhostImage.type = Image.Type.Filled;
            gainGhostImage.fillMethod = Image.FillMethod.Horizontal;
            gainGhostImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            gainGhostImage.fillAmount = 0f;
            gainGhostImage.color = new Color(1f, 0.98f, 0.75f);
            gainGhostImage.raycastTarget = false;

            GameObject barFillObject = new GameObject("Fill", typeof(Image));
            barFillObject.transform.SetParent(barContainer.transform, false);
            InsetRect(barFillObject.GetComponent<RectTransform>(), barFillInset);
            Image barFillImage = barFillObject.GetComponent<Image>();
            barFillImage.sprite = solidSprite;
            barFillImage.type = Image.Type.Filled;
            barFillImage.fillMethod = Image.FillMethod.Horizontal;
            barFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            barFillImage.fillAmount = 0f;
            barFillImage.color = new Color(0.95f, 0.8f, 0.3f);
            barFillImage.raycastTarget = false;

            GameObject fractionObject = new GameObject("FractionText", typeof(Text));
            fractionObject.transform.SetParent(rootObject.transform, false);
            RectTransform fractionRect = fractionObject.GetComponent<RectTransform>();
            fractionRect.anchorMin = new Vector2(0f, 1f);
            fractionRect.anchorMax = new Vector2(1f, 1f);
            fractionRect.pivot = new Vector2(0.5f, 1f);
            fractionRect.anchoredPosition = new Vector2(0f, -(rowHeight + fractionGap));
            fractionRect.sizeDelta = new Vector2(0f, fractionHeight);
            Text fractionText = fractionObject.GetComponent<Text>();
            fractionText.font = GetGameFont();
            fractionText.fontSize = 11;
            fractionText.color = new Color(1f, 1f, 1f, 0.85f);
            fractionText.alignment = TextAnchor.MiddleCenter;
            fractionText.text = "0/100";
            fractionText.raycastTarget = false;

            CanvasGroup canvasGroup = rootObject.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            XpBarUI xpBarUI = rootObject.AddComponent<XpBarUI>();
            SerializedObject xpBarSo = new SerializedObject(xpBarUI);
            xpBarSo.FindProperty("playerStats").objectReferenceValue = playerStats;
            xpBarSo.FindProperty("canvasGroup").objectReferenceValue = canvasGroup;
            xpBarSo.FindProperty("levelText").objectReferenceValue = levelText;
            xpBarSo.FindProperty("fractionText").objectReferenceValue = fractionText;
            xpBarSo.FindProperty("fillImage").objectReferenceValue = barFillImage;
            xpBarSo.FindProperty("gainGhostImage").objectReferenceValue = gainGhostImage;
            xpBarSo.ApplyModifiedProperties();
        }

        // Always-visible in-game mirror of the Abilities page hotbar — thin white rings with a
        // transparent middle (so it reads as a subtle overlay, not a solid panel), numbers 1-10
        // to the left of each slot matching the actual key bound to it. Purely reactive to
        // AbilityLoadout; equip/unequip on the Abilities page is the only thing that changes it.
        private static void SetupAbilityHotbarHudUI(Transform hudRoot, Sprite shapeSprite, Sprite solidSprite)
        {
            Transform existing = hudRoot.Find("AbilityHotbarHud");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            const int slotCount = AbilityLoadout.SlotCount;
            const float slotSize = 36f;
            const float rowGap = 6f;
            const float numberWidth = 18f;
            const float numberGap = 6f;
            const float fillInset = 3f;
            const float iconInset = 7f;
            const float rightMargin = 28f;
            const float rowWidth = numberWidth + numberGap + slotSize;
            const float totalHeight = slotCount * slotSize + (slotCount - 1) * rowGap;

            Sprite ringSprite = CreateHollowRoundedRectSprite();

            GameObject rootObject = new GameObject("AbilityHotbarHud", typeof(RectTransform));
            rootObject.transform.SetParent(hudRoot, false);
            RectTransform rootRect = rootObject.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 0.5f);
            rootRect.anchorMax = new Vector2(1f, 0.5f);
            rootRect.pivot = new Vector2(1f, 0.5f);
            rootRect.anchoredPosition = new Vector2(-rightMargin, 0f);
            rootRect.sizeDelta = new Vector2(rowWidth, totalHeight);

            RectTransform[] slotRects = new RectTransform[slotCount];
            Image[] slotIcons = new Image[slotCount];
            Image[] slotRings = new Image[slotCount];
            Image[] cooldownOverlays = new Image[slotCount];
            Text[] cooldownTexts = new Text[slotCount];
            Image[] deniedFlashes = new Image[slotCount];

            for (int i = 0; i < slotCount; i++)
            {
                float y = -(i * (slotSize + rowGap));
                // Slots 1-9 show their digit; the 10th shows "0" since that's the physical key
                // AbilitiesPageUI actually binds it to (Digit1..Digit9, then Digit0).
                string label = i < 9 ? (i + 1).ToString() : "0";

                GameObject numberObject = new GameObject($"Number{i + 1}", typeof(Text));
                numberObject.transform.SetParent(rootObject.transform, false);
                RectTransform numberRect = numberObject.GetComponent<RectTransform>();
                numberRect.anchorMin = new Vector2(0f, 1f);
                numberRect.anchorMax = new Vector2(0f, 1f);
                numberRect.pivot = new Vector2(0f, 1f);
                numberRect.anchoredPosition = new Vector2(0f, y);
                numberRect.sizeDelta = new Vector2(numberWidth, slotSize);
                Text numberText = numberObject.GetComponent<Text>();
                numberText.font = GetGameFont();
                numberText.fontSize = 13;
                numberText.color = new Color(1f, 1f, 1f, 0.85f);
                numberText.alignment = TextAnchor.MiddleRight;
                numberText.text = label;
                numberText.raycastTarget = false;

                GameObject slotObject = new GameObject($"Slot{i + 1}", typeof(RectTransform));
                slotObject.transform.SetParent(rootObject.transform, false);
                RectTransform slotRect = slotObject.GetComponent<RectTransform>();
                slotRect.anchorMin = new Vector2(0f, 1f);
                slotRect.anchorMax = new Vector2(0f, 1f);
                slotRect.pivot = new Vector2(0f, 1f);
                slotRect.anchoredPosition = new Vector2(numberWidth + numberGap, y);
                slotRect.sizeDelta = new Vector2(slotSize, slotSize);
                slotRects[i] = slotRect;

                GameObject ringObject = new GameObject("Ring", typeof(Image));
                ringObject.transform.SetParent(slotObject.transform, false);
                StretchRect(ringObject.GetComponent<RectTransform>());
                Image ringImage = ringObject.GetComponent<Image>();
                ringImage.sprite = ringSprite;
                ringImage.type = Image.Type.Sliced;
                ringImage.color = new Color(1f, 1f, 1f, 0.75f);
                ringImage.raycastTarget = false;

                // Two barely-visible stacked outline passes soften the ring's edge into a faint
                // ambient glow, without a separate larger sprite that would blur the "thin, clean
                // frame" look the border itself is going for.
                Outline glowOuter = ringObject.AddComponent<Outline>();
                glowOuter.effectColor = new Color(1f, 1f, 1f, 0.05f);
                glowOuter.effectDistance = new Vector2(2f, -2f);
                Outline glowInner = ringObject.AddComponent<Outline>();
                glowInner.effectColor = new Color(1f, 1f, 1f, 0.1f);
                glowInner.effectDistance = new Vector2(1f, -1f);
                slotRings[i] = ringImage;

                GameObject fillObject = new GameObject("Fill", typeof(Image));
                fillObject.transform.SetParent(slotObject.transform, false);
                InsetRect(fillObject.GetComponent<RectTransform>(), fillInset);
                Image fillImage = fillObject.GetComponent<Image>();
                fillImage.sprite = shapeSprite;
                fillImage.type = Image.Type.Sliced;
                fillImage.color = new Color(0.08f, 0.08f, 0.09f, 0.4f);
                fillImage.raycastTarget = false;

                GameObject iconObject = new GameObject("Icon", typeof(Image));
                iconObject.transform.SetParent(slotObject.transform, false);
                InsetRect(iconObject.GetComponent<RectTransform>(), iconInset);
                Image iconImage = iconObject.GetComponent<Image>();
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                iconImage.enabled = false;
                slotIcons[i] = iconImage;

                // Cooldown sweep: a flat-cornered Filled Vertical rect anchored to the bottom, so
                // decreasing fillAmount from 1 to 0 makes its top edge recede downward until
                // nothing is left — same "Filled needs a real sprite to clip" note as the health/XP
                // bar fills applies here.
                GameObject cooldownObject = new GameObject("CooldownOverlay", typeof(Image));
                cooldownObject.transform.SetParent(slotObject.transform, false);
                InsetRect(cooldownObject.GetComponent<RectTransform>(), fillInset);
                Image cooldownImage = cooldownObject.GetComponent<Image>();
                cooldownImage.sprite = solidSprite;
                cooldownImage.type = Image.Type.Filled;
                cooldownImage.fillMethod = Image.FillMethod.Vertical;
                cooldownImage.fillOrigin = (int)Image.OriginVertical.Bottom;
                cooldownImage.fillAmount = 0f;
                cooldownImage.color = new Color(0.85f, 0.85f, 0.85f, 0.35f);
                cooldownImage.raycastTarget = false;
                cooldownImage.enabled = false;
                cooldownOverlays[i] = cooldownImage;

                GameObject cooldownTextObject = new GameObject("CooldownText", typeof(Text));
                cooldownTextObject.transform.SetParent(slotObject.transform, false);
                StretchRect(cooldownTextObject.GetComponent<RectTransform>());
                Text cooldownText = cooldownTextObject.GetComponent<Text>();
                cooldownText.font = GetGameFont();
                cooldownText.fontSize = 12;
                cooldownText.fontStyle = FontStyle.Bold;
                cooldownText.color = new Color(0.2f, 0.2f, 0.2f, 0.95f);
                cooldownText.alignment = TextAnchor.MiddleCenter;
                cooldownText.text = string.Empty;
                cooldownText.raycastTarget = false;
                cooldownText.enabled = false;
                // Dark grey text needs a light outline to stay legible over the icon underneath it
                // rather than just the light-grey cooldown sweep.
                Outline cooldownTextOutline = cooldownTextObject.AddComponent<Outline>();
                cooldownTextOutline.effectColor = new Color(1f, 1f, 1f, 0.7f);
                cooldownTextOutline.effectDistance = new Vector2(1f, -1f);
                cooldownTexts[i] = cooldownText;

                GameObject deniedFlashObject = new GameObject("DeniedFlash", typeof(Image));
                deniedFlashObject.transform.SetParent(slotObject.transform, false);
                StretchRect(deniedFlashObject.GetComponent<RectTransform>());
                Image deniedFlashImage = deniedFlashObject.GetComponent<Image>();
                deniedFlashImage.sprite = shapeSprite;
                deniedFlashImage.type = Image.Type.Sliced;
                deniedFlashImage.color = new Color(1f, 0.55f, 0.55f, 0f);
                deniedFlashImage.raycastTarget = false;
                deniedFlashes[i] = deniedFlashImage;
            }

            AbilityHotbarHudUI hudUI = rootObject.AddComponent<AbilityHotbarHudUI>();
            SerializedObject hudSo = new SerializedObject(hudUI);
            AssignObjectArray(hudSo, "slotRects", slotRects);
            AssignObjectArray(hudSo, "slotIcons", slotIcons);
            AssignObjectArray(hudSo, "slotRings", slotRings);
            AssignObjectArray(hudSo, "cooldownOverlays", cooldownOverlays);
            AssignObjectArray(hudSo, "cooldownTexts", cooldownTexts);
            AssignObjectArray(hudSo, "deniedFlashes", deniedFlashes);
            hudSo.ApplyModifiedProperties();
        }

        private static void AssignObjectArray(SerializedObject serializedObject, string propertyName, Object[] values)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        internal static void StretchRect(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        internal static void InsetRect(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        // Procedurally draws a small rounded-rectangle shape (a signed-distance-field circle-corner
        // test, no external art) and saves it as a 9-sliced Sprite, so one tiny generated asset can
        // be stretched to any bar width while keeping crisp, non-distorted rounded corners.
        internal static Sprite CreateRoundedRectSprite()
        {
            const int size = 64;
            const float radius = 8f;

            Color[] pixels = new Color[size * size];
            Vector2 half = new Vector2(size / 2f, size / 2f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - half;
                    float qx = Mathf.Abs(p.x) - half.x + radius;
                    float qy = Mathf.Abs(p.y) - half.y + radius;
                    float outsideDist = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    float insideDist = Mathf.Min(Mathf.Max(qx, qy), 0f);
                    float signedDistance = outsideDist + insideDist - radius;
                    float alpha = Mathf.Clamp01(0.5f - signedDistance);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(HealthbarShapePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(HealthbarShapePath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(HealthbarShapePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                float border = radius + 2f;
                importer.spriteBorder = new Vector4(border, border, border, border);
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(HealthbarShapePath);
        }

        // Image.Type.Filled apparently needs a real Sprite reference to correctly clip its partial
        // geometry — with sprite left null it kept rendering fully filled regardless of fillAmount.
        // This is a tiny fully-opaque solid square, purely so Filled has something valid to clip;
        // being uniform color, stretching it to any bar size introduces no visible artifacts.
        internal static Sprite CreateSolidSprite()
        {
            const int size = 4;

            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.white;
            }

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(HealthbarFillShapePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(HealthbarFillShapePath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(HealthbarFillShapePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(HealthbarFillShapePath);
        }

        // Same rounded-rect SDF as CreateRoundedRectSprite, but subtracts an inward-shrunk copy of
        // itself (shifted by ringThickness) to carve out a hollow band along the boundary — the
        // interior stays fully transparent so a 9-sliced Image reads as a thin outline only,
        // regardless of how large the slot it's stretched to fill is.
        internal static Sprite CreateHollowRoundedRectSprite()
        {
            const int size = 64;
            const float radius = 8f;
            const float ringThickness = 2f;

            Color[] pixels = new Color[size * size];
            Vector2 half = new Vector2(size / 2f, size / 2f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - half;
                    float qx = Mathf.Abs(p.x) - half.x + radius;
                    float qy = Mathf.Abs(p.y) - half.y + radius;
                    float outsideDist = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    float insideDist = Mathf.Min(Mathf.Max(qx, qy), 0f);
                    float signedDistance = outsideDist + insideDist - radius;

                    float outerMask = Mathf.Clamp01(0.5f - signedDistance);
                    float innerMask = Mathf.Clamp01(0.5f - (signedDistance + ringThickness));
                    float alpha = Mathf.Clamp01(outerMask - innerMask);

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(AbilityHotbarHudRingPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(AbilityHotbarHudRingPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(AbilityHotbarHudRingPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                float border = radius + ringThickness + 2f;
                importer.spriteBorder = new Vector4(border, border, border, border);
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(AbilityHotbarHudRingPath);
        }

        private const string CharacterTexturesFolder = "Assets/_Project/Art/Characters/RPGCharacterPack/Textures";
        private const string CharacterMaterialsFolder = "Assets/_Project/Materials/Characters";

        internal static void ApplyCharacterMaterial(GameObject model, string characterName)
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

            Shader toonShader = Shader.Find("Darclite/AshenLit");
            if (toonShader != null)
            {
                material.shader = toonShader;
            }

            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();

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
            // Prefer the Terrain once one exists — it replaces Floor as the actual ground, and
            // NavMeshSurface bakes correctly against a TerrainCollider the same way it does a
            // regular mesh collider.
            GameObject ground = GameObject.Find("Terrain");
            if (ground == null)
            {
                ground = GameObject.Find("Floor");
            }
            if (ground == null)
            {
                Debug.LogError("No 'Terrain' or 'Floor' GameObject found in the scene. Create one with Darclite/Create Terrain or Darclite/Create Floor first.");
                return;
            }

            NavMeshSurface surface = ground.GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                surface = ground.AddComponent<NavMeshSurface>();
            }

            surface.BuildNavMesh();
            Debug.Log($"NavMesh baked from '{ground.name}'.");
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

            BuildEnemyCharacter(enemy);

            Selection.activeGameObject = enemy;
            Debug.Log("Enemy character spawned and wired up.");
        }

        private const string BanditPrefabPath = "Assets/_Project/Prefabs/Bandit.prefab";

        // Reuses the exact same Rogue-model build as the training-dummy Enemy — the Bandit Beater
        // quest wants "the rogue enemy we have right now but 5 of them," not a different look.
        // Built once here into a prefab asset; BanditQuestSpawner just Instantiate()s it at
        // runtime, since everything in this file is Editor-only and unavailable during Play.
        [MenuItem("Darclite/Build Bandit Prefab")]
        public static void BuildBanditPrefab()
        {
            GameObject temp = new GameObject("Bandit");

            BuildEnemyCharacter(temp);

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Prefabs"))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "Prefabs");
            }

            PrefabUtility.SaveAsPrefabAsset(temp, BanditPrefabPath);
            Object.DestroyImmediate(temp);

            Debug.Log($"Bandit prefab built at {BanditPrefabPath}. Run 'Darclite/Setup Bandit Quest Encounter' next.");
        }

        // Builds a fully-configured Rogue-model combatant onto an already-created root GameObject
        // — shared by the scene "Enemy" (SetupEnemyCharacter) and the "Bandit" prefab template
        // (BuildBanditPrefab) so both stay identical without duplicating this setup twice.
        private static void BuildEnemyCharacter(GameObject enemy)
        {
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
            SetupHitEffect(enemy, combatant);

            PopulateAttackDurations(enemy.GetComponent<AttackCombo>());
            SetupEnemyHealthUI(enemy, combatant, bounds);

            CharacterAudio enemyAudio = enemy.GetComponent<CharacterAudio>();
            if (enemyAudio == null)
            {
                enemyAudio = enemy.AddComponent<CharacterAudio>();
            }
            PopulateCharacterAudio(enemyAudio);

            BlockDodge enemyBlockDodge = enemy.GetComponent<BlockDodge>();
            if (enemyBlockDodge == null)
            {
                enemyBlockDodge = enemy.AddComponent<BlockDodge>();
            }
            ApplyBlockDodgeTiming(enemyBlockDodge, respondToKeyboardInput: false);

            EnemyDeath enemyDeath = enemy.GetComponent<EnemyDeath>();
            if (enemyDeath == null)
            {
                enemyDeath = enemy.AddComponent<EnemyDeath>();
            }
            PopulateDeathDurations(enemyDeath);
        }

        private const float BanditSpawnRadius = 4f;

        // Wires the Bandit Beater quest to its actual gameplay encounter. Requires the Bandit
        // prefab (Darclite/Build Bandit Prefab) and the quest asset (created automatically by
        // Setup Quest NPC) to already exist.
        [MenuItem("Darclite/Setup Bandit Quest Encounter")]
        public static void SetupBanditQuestEncounter()
        {
            GameObject banditPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BanditPrefabPath);
            if (banditPrefab == null)
            {
                Debug.LogError($"[SceneBootstrapper] No Bandit prefab found at {BanditPrefabPath} — run 'Darclite/Build Bandit Prefab' first.");
                return;
            }

            QuestDefinition quest = GetOrCreateQuestNPCQuest();

            GameObject spawnerObject = GameObject.Find("BanditQuestSpawner");
            if (spawnerObject == null)
            {
                spawnerObject = new GameObject("BanditQuestSpawner");
                Undo.RegisterCreatedObjectUndo(spawnerObject, "Create Bandit Quest Spawner");
            }

            Transform spawnArea = spawnerObject.transform.Find("SpawnAreaCenter");
            if (spawnArea == null)
            {
                GameObject spawnAreaObject = new GameObject("SpawnAreaCenter");
                spawnAreaObject.transform.SetParent(spawnerObject.transform, false);
                // Placeholder location — drag this in the Scene view to wherever actually fits
                // the village once the terrain layout is sculpted.
                spawnAreaObject.transform.position = new Vector3(10f, 0f, -8f);
                spawnArea = spawnAreaObject.transform;
            }

            BanditQuestSpawner spawner = spawnerObject.GetComponent<BanditQuestSpawner>();
            if (spawner == null)
            {
                spawner = spawnerObject.AddComponent<BanditQuestSpawner>();
            }

            SerializedObject so = new SerializedObject(spawner);
            so.FindProperty("quest").objectReferenceValue = quest;
            so.FindProperty("banditPrefab").objectReferenceValue = banditPrefab;
            so.FindProperty("spawnAreaCenter").objectReferenceValue = spawnArea;
            so.FindProperty("spawnRadius").floatValue = BanditSpawnRadius;
            so.FindProperty("banditCount").intValue = BanditBeaterCount;
            so.ApplyModifiedProperties();

            Selection.activeGameObject = spawnerObject;
            Debug.Log("Bandit quest encounter wired up. Move 'BanditQuestSpawner/SpawnAreaCenter' in the Scene view to where the bandits should spawn.");
        }

        private const string NPCModelPath = "Assets/_Project/Art/Characters/RPGCharacterPack/Models/Ranger.fbx";
        private const string NPCModelCharacterName = "Ranger";
        private const int QuestNPCMaxHealth = 50;

        private const string QuestNPCPersonaPath = "Assets/_Project/Data/NPCPersonas/Elka.asset";
        private const string QuestNPCFirstEncounterGreeting =
            "Oh! A traveler — perfect timing. I could really use some help with something, if you're willing. " +
            "It's nothing I can trust to just anyone, mind you.";
        private const string QuestNPCTurnInDialogue =
            "You actually did it — the bandits are gone. Thank you, truly. I won't forget this.";

        // Creates the persona asset with starter content the first time this runs — from then on
        // this is a no-op and the asset is the source of truth. Edit the asset directly (or via
        // its own Inspector) to change what she knows/says; re-run Setup Quest NPC to re-bake it
        // into her LLMAgent's system prompt.
        private static NPCPersonaDefinition GetOrCreateQuestNPCPersona()
        {
            NPCPersonaDefinition persona = AssetDatabase.LoadAssetAtPath<NPCPersonaDefinition>(QuestNPCPersonaPath);
            if (persona != null)
            {
                // Backfill fields added after this asset was first created — everything past this
                // point only runs on first creation so hand-edited persona content sticks, but that
                // means a field introduced later (like the quest system) silently never gets wired
                // onto an asset that already existed before it was added.
                SerializedObject existingSo = new SerializedObject(persona);
                bool backfilled = false;

                SerializedProperty existingQuestProp = existingSo.FindProperty("offerableQuest");
                if (existingQuestProp != null && existingQuestProp.objectReferenceValue == null)
                {
                    existingQuestProp.objectReferenceValue = GetOrCreateQuestNPCQuest();
                    backfilled = true;
                }

                SerializedProperty existingGreetingProp = existingSo.FindProperty("firstEncounterGreeting");
                if (existingGreetingProp != null && string.IsNullOrEmpty(existingGreetingProp.stringValue))
                {
                    existingGreetingProp.stringValue = QuestNPCFirstEncounterGreeting;
                    backfilled = true;
                }

                SerializedProperty existingTurnInProp = existingSo.FindProperty("questTurnInDialogue");
                if (existingTurnInProp != null && string.IsNullOrEmpty(existingTurnInProp.stringValue))
                {
                    existingTurnInProp.stringValue = QuestNPCTurnInDialogue;
                    backfilled = true;
                }

                if (backfilled)
                {
                    existingSo.ApplyModifiedProperties();
                    AssetDatabase.SaveAssets();
                    Debug.Log("[SceneBootstrapper] Backfilled new field(s) onto the existing Elka persona asset.");
                }
                return persona;
            }

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data"))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "Data");
            }
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data/NPCPersonas"))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Data", "NPCPersonas");
            }

            persona = ScriptableObject.CreateInstance<NPCPersonaDefinition>();
            AssetDatabase.CreateAsset(persona, QuestNPCPersonaPath);

            SerializedObject so = new SerializedObject(persona);
            so.FindProperty("characterName").stringValue = "Elka";
            so.FindProperty("personality").stringValue =
                "You are warm but no-nonsense, and you speak in short, plain sentences. You are wary of outsiders until they prove themselves.";
            so.FindProperty("backstory").stringValue =
                "You are a seasoned ranger who serves as this village's quest-giver.";
            so.FindProperty("knownFacts").stringValue =
                "- You know the surrounding forest, its trails, and the wildlife within it well.\n" +
                "- You have lived in this village for many years and know most of its residents.\n" +
                "- You keep watch for travelers who might be capable of helping with tasks the village needs done.";
            so.FindProperty("forbiddenTopics").stringValue =
                "- You don't know anything about the wider world beyond the village and forest — don't invent details about " +
                "distant kingdoms, wars, or events you'd have no way of knowing.";
            so.FindProperty("currentGoal").stringValue =
                "You are eager for help and mention that you need it right away, in your very first greeting to the player — " +
                "don't hold back or wait. However, don't formally offer the specific quest until the player directly asks what " +
                "you need help with or clearly shows interest — until then, just say you need help without giving the details away.";
            so.FindProperty("offerableQuest").objectReferenceValue = GetOrCreateQuestNPCQuest();
            so.FindProperty("firstEncounterGreeting").stringValue = QuestNPCFirstEncounterGreeting;
            so.FindProperty("questTurnInDialogue").stringValue = QuestNPCTurnInDialogue;
            so.ApplyModifiedProperties();

            AssetDatabase.SaveAssets();
            return persona;
        }

        private const string QuestNPCQuestPath = "Assets/_Project/Data/Quests/BanditBeater.asset";
        private const int BanditBeaterCount = 5;

        private static QuestDefinition GetOrCreateQuestNPCQuest()
        {
            QuestDefinition quest = AssetDatabase.LoadAssetAtPath<QuestDefinition>(QuestNPCQuestPath);
            if (quest != null)
            {
                // Backfill fields added after this asset was first created — see the matching
                // comment in GetOrCreateQuestNPCPersona for why this only touches empty fields.
                SerializedObject existingSo = new SerializedObject(quest);
                SerializedProperty existingGiverProp = existingSo.FindProperty("questGiverName");
                if (existingGiverProp != null && string.IsNullOrEmpty(existingGiverProp.stringValue))
                {
                    existingGiverProp.stringValue = "Elka";
                    existingSo.ApplyModifiedProperties();
                    AssetDatabase.SaveAssets();
                    Debug.Log("[SceneBootstrapper] Backfilled 'questGiverName' onto the existing Bandit Beater quest asset.");
                }
                return quest;
            }

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data/Quests"))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Data", "Quests");
            }

            quest = ScriptableObject.CreateInstance<QuestDefinition>();
            AssetDatabase.CreateAsset(quest, QuestNPCQuestPath);

            SerializedObject so = new SerializedObject(quest);
            so.FindProperty("questId").stringValue = "bandit_beater";
            so.FindProperty("title").stringValue = "Bandit Beater";
            so.FindProperty("description").stringValue =
                "A band of bandits has been lurking around the village, causing trouble for anyone who passes by. " +
                "Elka wants them driven off for good.";
            so.FindProperty("objective").stringValue = $"Defeat the {BanditBeaterCount} bandits near the village.";
            so.FindProperty("rewardDescription").stringValue = "Elka's thanks — and her trust.";
            so.FindProperty("targetProgress").intValue = BanditBeaterCount;
            so.FindProperty("progressLabel").stringValue = "bandits defeated";
            so.FindProperty("questGiverName").stringValue = "Elka";
            so.ApplyModifiedProperties();

            AssetDatabase.SaveAssets();
            return quest;
        }

        // Subfolder of Application.persistentDataPath where every NPC's chat history is saved —
        // named per-character so a growing NPC roster never collides on one shared save file.
        // Internal so LLMSetupTools' memory-clearing dev command can find the same folder.
        internal const string NPCChatSaveFolder = "NPCChats";

        private static string GetChatSaveFileName(string characterName)
        {
            string sanitized = string.Join("_", characterName.Split(Path.GetInvalidFileNameChars()));
            return $"{NPCChatSaveFolder}/{sanitized}.json";
        }

        private static void SetupNPCDialogueAgent(GameObject npc, NPCPersonaDefinition persona)
        {
            GameObject hostObject = GameObject.Find("LLMHost");
            LLM llmHost = hostObject != null ? hostObject.GetComponent<LLM>() : null;
            if (llmHost == null)
            {
                Debug.LogWarning("[SceneBootstrapper] No 'LLMHost' found in the scene — run 'Darclite/LLM/Setup LLM Host (Download Model)' first, then re-run 'Setup Quest NPC'. Skipping her LLMAgent for now.");
                return;
            }

            LLMAgent agent = npc.GetComponent<LLMAgent>();
            if (agent == null)
            {
                agent = npc.AddComponent<LLMAgent>();
            }

            // Set explicitly rather than relying on LLMAgent's own auto-assign fallback — that
            // fallback runs partway through its own Awake(), after the point where it registers
            // itself with whatever `llm` is already set, so an agent left to auto-assign never
            // actually registers as a client of the host it finds.
            agent.llm = llmHost;
            agent.systemPrompt = persona.BuildSystemPrompt();
            agent.save = GetChatSaveFileName(persona.CharacterName);
            agent.overflowStrategy = UndreamAI.LlamaLib.ContextOverflowStrategy.Summarize;
        }

        [MenuItem("Darclite/Setup Quest NPC")]
        public static void SetupQuestNPC()
        {
            DestroyAllRootObjectsNamed("QuestNPC");

            GameObject npc = new GameObject("QuestNPC");
            npc.transform.position = new Vector3(-4f, 0f, 2f);
            Undo.RegisterCreatedObjectUndo(npc, "Create Quest NPC");

            GameObject npcAsset = AssetDatabase.LoadAssetAtPath<GameObject>(NPCModelPath);
            if (npcAsset == null)
            {
                Debug.LogError($"Could not find NPC model at {NPCModelPath}");
                return;
            }

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(npcAsset, npc.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // The imported rig can carry its own leftover colliders (seen on the Enemy model
            // earlier) — strip those so only our own explicit collider below controls her
            // physical presence.
            foreach (Collider modelCollider in model.GetComponentsInChildren<Collider>())
            {
                Object.DestroyImmediate(modelCollider);
            }

            ApplyCharacterMaterial(model, NPCModelCharacterName);

            // Matches SetupEnemyCharacter exactly (full shared controller, no manual bind-pose
            // correction) — the Rogue model uses this same setup with no floating, so the
            // earlier idle-only stub controller (and the ground-anchoring hacks it needed) is
            // gone in favor of just reusing the proven-working pattern.
            Animator animator = model.GetComponent<Animator>();
            if (animator == null)
            {
                animator = model.AddComponent<Animator>();
            }
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerControllerPath);
            animator.applyRootMotion = false;

            NavMeshAgent agent = npc.GetComponent<NavMeshAgent>();
            if (agent == null)
            {
                agent = npc.AddComponent<NavMeshAgent>();
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

            CapsuleCollider collider = npc.GetComponent<CapsuleCollider>();
            if (collider == null)
            {
                collider = npc.AddComponent<CapsuleCollider>();
            }
            if (bounds.size.y > 0f)
            {
                collider.height = bounds.size.y;
                collider.center = new Vector3(0f, bounds.size.y * 0.5f, 0f);
                // Her cloak/silhouette pads the raw bounds well beyond her actual body width, so
                // the plain player/enemy formula makes an oversized hitbox — shrink it in and cap
                // it lower so the player can't feel blocked from a wide margin around her.
                collider.radius = Mathf.Clamp(Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.6f, 0.2f, 0.45f);
            }

            if (npc.GetComponent<QuestNPCFleeController>() == null)
            {
                npc.AddComponent<QuestNPCFleeController>();
            }

            Combatant combatant = npc.GetComponent<Combatant>();
            if (combatant == null)
            {
                combatant = npc.AddComponent<Combatant>();
            }
            ApplyCombatantTiming(combatant, QuestNPCMaxHealth);
            SetupHitEffect(npc, combatant);

            CharacterAudio npcAudio = npc.GetComponent<CharacterAudio>();
            if (npcAudio == null)
            {
                npcAudio = npc.AddComponent<CharacterAudio>();
            }
            PopulateCharacterAudio(npcAudio);

            EnemyDeath npcDeath = npc.GetComponent<EnemyDeath>();
            if (npcDeath == null)
            {
                npcDeath = npc.AddComponent<EnemyDeath>();
            }
            PopulateDeathDurations(npcDeath);

            NPCPersonaDefinition persona = GetOrCreateQuestNPCPersona();
            SetupNPCDialogueAgent(npc, persona);
            SetupNPCInteraction(npc, model, bounds, persona);

            Selection.activeGameObject = npc;
            Debug.Log("Quest NPC spawned and wired up.");
        }

        private static void SetupNPCInteraction(GameObject npc, GameObject model, Bounds bounds, NPCPersonaDefinition persona)
        {
            float headHeight = (bounds.max.y - npc.transform.position.y) + 0.35f;

            Transform existingLookPoint = npc.transform.Find("LookPoint");
            if (existingLookPoint != null)
            {
                Object.DestroyImmediate(existingLookPoint.gameObject);
            }
            GameObject lookPointObject = new GameObject("LookPoint");
            lookPointObject.transform.SetParent(npc.transform, false);
            lookPointObject.transform.localPosition = new Vector3(0f, headHeight * 0.85f, 0f);

            Transform existingPrompt = npc.transform.Find("InteractPrompt");
            if (existingPrompt != null)
            {
                Object.DestroyImmediate(existingPrompt.gameObject);
            }

            Sprite badgeSprite = CreateRoundedRectSprite();

            GameObject promptCanvasObject = new GameObject("InteractPrompt", typeof(Canvas));
            promptCanvasObject.transform.SetParent(npc.transform, false);
            promptCanvasObject.transform.localPosition = new Vector3(0f, headHeight, 0f);
            promptCanvasObject.transform.localScale = Vector3.one * 0.012f;

            Canvas promptCanvas = promptCanvasObject.GetComponent<Canvas>();
            promptCanvas.renderMode = RenderMode.WorldSpace;

            RectTransform promptCanvasRect = promptCanvasObject.GetComponent<RectTransform>();
            promptCanvasRect.sizeDelta = new Vector2(80f, 80f);

            GameObject badgeObject = new GameObject("Badge", typeof(Image));
            badgeObject.transform.SetParent(promptCanvasObject.transform, false);
            StretchRect(badgeObject.GetComponent<RectTransform>());
            Image badgeImage = badgeObject.GetComponent<Image>();
            badgeImage.sprite = badgeSprite;
            badgeImage.type = Image.Type.Sliced;
            badgeImage.color = new Color(0f, 0f, 0f, 0.5f);

            GameObject eTextObject = new GameObject("EText", typeof(Text));
            eTextObject.transform.SetParent(promptCanvasObject.transform, false);
            StretchRect(eTextObject.GetComponent<RectTransform>());
            Text eText = eTextObject.GetComponent<Text>();
            eText.font = GetGameFont();
            eText.fontSize = 48;
            eText.fontStyle = FontStyle.Bold;
            eText.color = new Color(1f, 1f, 1f, 0.85f);
            eText.alignment = TextAnchor.MiddleCenter;
            eText.text = "E";

            promptCanvasObject.SetActive(false);

            NPCInteractable interactable = npc.GetComponent<NPCInteractable>();
            if (interactable == null)
            {
                interactable = npc.AddComponent<NPCInteractable>();
            }

            SerializedObject so = new SerializedObject(interactable);
            so.FindProperty("npcName").stringValue = persona.CharacterName;
            so.FindProperty("persona").objectReferenceValue = persona;
            so.FindProperty("lookPoint").objectReferenceValue = lookPointObject.transform;
            so.FindProperty("promptRoot").objectReferenceValue = promptCanvasObject;

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            SerializedProperty renderersProp = so.FindProperty("highlightRenderers");
            renderersProp.arraySize = renderers.Length;
            for (int i = 0; i < renderers.Length; i++)
            {
                renderersProp.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            }

            so.ApplyModifiedProperties();
        }

        private static readonly string[] DeathClipNames = { "Death", "Death2", "Death3" };

        private static void PopulateDeathDurations(EnemyDeath enemyDeath)
        {
            if (enemyDeath == null)
            {
                return;
            }

            SerializedObject so = new SerializedObject(enemyDeath);
            SerializedProperty durationsProp = so.FindProperty("deathClipDurations");
            if (durationsProp != null)
            {
                for (int i = 0; i < DeathClipNames.Length && i < durationsProp.arraySize; i++)
                {
                    durationsProp.GetArrayElementAtIndex(i).floatValue = GetFightClipLength(DeathClipNames[i]);
                }
            }

            so.ApplyModifiedProperties();
        }

        // Same layered bar look as the player's health bar (Border/Track/DamageTrail/HealthFill/
        // ChunkDividers, built with the same procedural sprites), just world-space and scaled down
        // to sit above the enemy's head instead of pinned to the screen. Starts hidden — EnemyHealthUI
        // only reveals it while the player's Power Sense ability is active.
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

            GameObject barObject = new GameObject("Healthbar", typeof(RectTransform));
            barObject.transform.SetParent(canvasObject.transform, false);

            RectTransform barRect = barObject.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0.5f, 0.5f);
            barRect.anchorMax = new Vector2(0.5f, 0.5f);
            barRect.pivot = new Vector2(0.5f, 0.5f);
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = new Vector2(160f, 24f);

            const float borderThickness = 2f;
            const float fillInset = 5f;
            Sprite shapeSprite = CreateRoundedRectSprite();
            Sprite solidSprite = CreateSolidSprite();

            GameObject borderObject = new GameObject("Border", typeof(Image));
            borderObject.transform.SetParent(barObject.transform, false);
            StretchRect(borderObject.GetComponent<RectTransform>());
            Image borderImage = borderObject.GetComponent<Image>();
            borderImage.sprite = shapeSprite;
            borderImage.type = Image.Type.Sliced;
            borderImage.color = new Color(0.06f, 0.06f, 0.07f);
            borderImage.raycastTarget = false;

            GameObject trackObject = new GameObject("Track", typeof(Image));
            trackObject.transform.SetParent(barObject.transform, false);
            InsetRect(trackObject.GetComponent<RectTransform>(), borderThickness);
            Image trackImage = trackObject.GetComponent<Image>();
            trackImage.sprite = shapeSprite;
            trackImage.type = Image.Type.Sliced;
            trackImage.color = new Color(0.15f, 0.05f, 0.05f);
            trackImage.raycastTarget = false;

            GameObject trailObject = new GameObject("DamageTrail", typeof(Image));
            trailObject.transform.SetParent(barObject.transform, false);
            InsetRect(trailObject.GetComponent<RectTransform>(), fillInset);
            Image damageTrailImage = trailObject.GetComponent<Image>();
            damageTrailImage.sprite = solidSprite;
            damageTrailImage.type = Image.Type.Filled;
            damageTrailImage.fillMethod = Image.FillMethod.Horizontal;
            damageTrailImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            damageTrailImage.fillAmount = 1f;
            damageTrailImage.color = new Color(0.95f, 0.75f, 0.15f);
            damageTrailImage.raycastTarget = false;

            GameObject fillObject = new GameObject("HealthFill", typeof(Image));
            fillObject.transform.SetParent(barObject.transform, false);
            InsetRect(fillObject.GetComponent<RectTransform>(), fillInset);
            Image healthFillImage = fillObject.GetComponent<Image>();
            healthFillImage.sprite = solidSprite;
            healthFillImage.type = Image.Type.Filled;
            healthFillImage.fillMethod = Image.FillMethod.Horizontal;
            healthFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            healthFillImage.fillAmount = 1f;
            healthFillImage.color = new Color(0.25f, 0.85f, 0.25f);
            healthFillImage.raycastTarget = false;

            GameObject dividersContainer = new GameObject("ChunkDividers", typeof(RectTransform));
            dividersContainer.transform.SetParent(barObject.transform, false);
            InsetRect(dividersContainer.GetComponent<RectTransform>(), fillInset);

            for (int i = 1; i < HealthBarChunkCount; i++)
            {
                float x = i / (float)HealthBarChunkCount;
                GameObject dividerObject = new GameObject($"Divider{i}", typeof(Image));
                dividerObject.transform.SetParent(dividersContainer.transform, false);

                RectTransform dividerRect = dividerObject.GetComponent<RectTransform>();
                dividerRect.anchorMin = new Vector2(x, 0f);
                dividerRect.anchorMax = new Vector2(x, 1f);
                dividerRect.pivot = new Vector2(0.5f, 0.5f);
                dividerRect.sizeDelta = new Vector2(1.5f, 0f);
                dividerRect.anchoredPosition = Vector2.zero;

                Image dividerImage = dividerObject.GetComponent<Image>();
                dividerImage.color = new Color(0f, 0f, 0f, 0.3f);
                dividerImage.raycastTarget = false;
            }

            EnemyHealthUI healthUI = canvasObject.AddComponent<EnemyHealthUI>();
            SerializedObject so = new SerializedObject(healthUI);
            so.FindProperty("combatant").objectReferenceValue = combatant;
            so.FindProperty("punchTarget").objectReferenceValue = barRect;
            so.FindProperty("healthFillImage").objectReferenceValue = healthFillImage;
            so.FindProperty("damageTrailImage").objectReferenceValue = damageTrailImage;
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

        internal static Bounds CalculateBounds(GameObject root)
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

        // ==================== Destructible Test Wall (Phase 0 placeholder) ====================

        // Placeholder-cube version of a destructible structure — built purely to test-drive the
        // DestructibleChunk pipeline (Lite damage, hard-knockback impacts, breaking, despawn)
        // before any real Blender art exists. A real house will get its own builder later that
        // reads Chunk_/Static_ naming off an imported model instead of generating cubes.
        private const string TestDestructibleWallName = "TestDestructibleWall";

        [MenuItem("Darclite/Build Test Destructible Wall")]
        public static void BuildTestDestructibleWall()
        {
            GameObject existing = GameObject.Find(TestDestructibleWallName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            int destructibleLayer = DestructibleLayerSetup.EnsureDestructibleLayerExists();

            GameObject root = new GameObject(TestDestructibleWallName);
            root.transform.position = new Vector3(10f, 0f, 10f);

            const float chunkSize = 1f;
            const int columns = 3;
            const int rows = 3;

            // Permanent solid geometry beneath the breakable panel — the wall never fully
            // disappears even once every chunk above it has broken away.
            GameObject baseObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseObject.name = "Static_Foundation";
            baseObject.transform.SetParent(root.transform, false);
            baseObject.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            baseObject.transform.localScale = new Vector3(columns * chunkSize, 1f, 0.5f);
            if (destructibleLayer >= 0)
            {
                baseObject.layer = destructibleLayer;
            }

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    GameObject chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    chunk.name = $"Chunk_Wall_{row}_{col}";
                    chunk.transform.SetParent(root.transform, false);
                    chunk.transform.localPosition = new Vector3(
                        (col - (columns - 1) * 0.5f) * chunkSize,
                        row * chunkSize,
                        0f);
                    chunk.transform.localScale = Vector3.one * (chunkSize * 0.95f);
                    if (destructibleLayer >= 0)
                    {
                        chunk.layer = destructibleLayer;
                    }

                    // RequireComponent(typeof(Rigidbody)) on DestructibleChunk auto-adds and
                    // configures the Rigidbody (kinematic, starts rigid) in its own Awake().
                    chunk.AddComponent<DestructibleChunk>();
                }
            }

            Debug.Log($"[SceneBootstrapper] Test destructible wall built at {root.transform.position} with {columns * rows} chunks.");
        }

        // ==================== House Prefab (Phase 2: real Blender art) ====================

        private const string HouseModelPath = "Assets/_Project/Art/Environment/House.fbx";
        private const string HousePrefabPath = "Assets/_Project/Prefabs/House.prefab";
        private const string HouseDarkModelPath = "Assets/_Project/Art/Environment/HouseDark.fbx";
        private const string HouseDarkPrefabPath = "Assets/_Project/Prefabs/HouseDark.prefab";
        private const string HouseCabinModelPath = "Assets/_Project/Art/Environment/HouseCabin.fbx";
        private const string HouseCabinPrefabPath = "Assets/_Project/Prefabs/HouseCabin.prefab";
        private const string StonePathModelPath = "Assets/_Project/Art/Environment/StonePath.fbx";
        private const string StonePathPrefabPath = "Assets/_Project/Prefabs/StonePath.prefab";
        private const string HouseTwoStoryModelPath = "Assets/_Project/Art/Environment/HouseTwoStory.fbx";
        private const string HouseTwoStoryPrefabPath = "Assets/_Project/Prefabs/HouseTwoStory.prefab";

        [MenuItem("Darclite/Build House Prefab")]
        public static void BuildHousePrefab()
        {
            BuildDestructibleStructurePrefab(HouseModelPath, HousePrefabPath);
        }

        [MenuItem("Darclite/Build House Dark Prefab")]
        public static void BuildHouseDarkPrefab()
        {
            BuildDestructibleStructurePrefab(HouseDarkModelPath, HouseDarkPrefabPath);
        }

        [MenuItem("Darclite/Build House Cabin Prefab")]
        public static void BuildHouseCabinPrefab()
        {
            BuildDestructibleStructurePrefab(HouseCabinModelPath, HouseCabinPrefabPath);
        }

        [MenuItem("Darclite/Build Stone Path Prefab")]
        public static void BuildStonePathPrefab()
        {
            BuildDestructibleStructurePrefab(StonePathModelPath, StonePathPrefabPath);
        }

        [MenuItem("Darclite/Build House Two Story Prefab")]
        public static void BuildHouseTwoStoryPrefab()
        {
            BuildDestructibleStructurePrefab(HouseTwoStoryModelPath, HouseTwoStoryPrefabPath);
        }

        // Walks an imported destructible-structure model's children by name convention (matching
        // BuildTestDestructibleWall's cube pieces): Chunk_-prefixed pieces get a convex
        // MeshCollider + Rigidbody + DestructibleChunk (RequireComponent auto-adds the
        // Rigidbody), Static_-prefixed pieces just get a plain MeshCollider.
        //
        // Built in total isolation from the open scene, the same way BuildBanditPrefab builds
        // its temp object — never searches for or touches any object already placed by hand in
        // the scene, so you can drop as many copies of the resulting prefab in wherever you want
        // (renamed however) and re-running this will only ever update the prefab asset itself.
        // Existing placed instances then pick up the change automatically, the same as any other
        // Unity prefab.
        private static void BuildDestructibleStructurePrefab(string modelPath, string prefabPath)
        {
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset == null)
            {
                Debug.LogError($"[SceneBootstrapper] Could not find model at {modelPath}");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            instance.name = "__DestructibleStructureBuildTemp__";

            int destructibleLayer = DestructibleLayerSetup.EnsureDestructibleLayerExists();

            Transform[] allChildren = instance.GetComponentsInChildren<Transform>(true);
            int chunkCount = 0;
            int staticCount = 0;
            foreach (Transform t in allChildren)
            {
                if (t == instance.transform)
                {
                    continue;
                }

                MeshFilter meshFilter = t.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    continue;
                }

                GameObject go = t.gameObject;
                if (destructibleLayer >= 0)
                {
                    go.layer = destructibleLayer;
                }

                if (go.name.StartsWith("Chunk_"))
                {
                    MeshCollider collider = go.GetComponent<MeshCollider>();
                    if (collider == null)
                    {
                        collider = go.AddComponent<MeshCollider>();
                    }
                    collider.convex = true;

                    if (go.GetComponent<DestructibleChunk>() == null)
                    {
                        go.AddComponent<DestructibleChunk>();
                    }
                    chunkCount++;
                }
                else if (go.name.StartsWith("Static_"))
                {
                    if (go.GetComponent<MeshCollider>() == null)
                    {
                        go.AddComponent<MeshCollider>();
                    }
                    staticCount++;
                }
            }

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Prefabs"))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "Prefabs");
            }
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            Object.DestroyImmediate(instance);

            Debug.Log($"[SceneBootstrapper] Prefab rebuilt at {prefabPath} ({chunkCount} destructible chunks, {staticCount} static pieces). Drag it from the Project window to place copies in the scene.");
        }
    }
}
