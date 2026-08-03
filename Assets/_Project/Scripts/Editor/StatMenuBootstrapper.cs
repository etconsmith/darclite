using System.IO;
using Darclite.Combat;
using Darclite.Core;
using Darclite.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Darclite.EditorTools
{
    // Builds the Assassin's Creed Origins-inspired stat menu: a full-screen blurred overlay with
    // a navy grid panel, a header tab bar (Stats and Lite are clickable pages; Strength/Vitality/
    // Dexterity stay darkened placeholders until they have content), 4 stat rows the player can
    // bank ability points into on the Stats page, a live character preview render, and the Lite
    // page's 4 ability trees (currently just their locked first node each).
    // Run in order: Setup Character Preview Stage -> Setup World Blur Volume -> Setup Stat Menu UI.
    public static class StatMenuBootstrapper
    {
        // ==================== Character Preview Stage ====================

        private const string CharacterPreviewLayerName = "CharacterPreview";
        private const string CharacterPreviewRenderTexturePath = "Assets/_Project/Textures/StatMenuCharacterPreview.renderTexture";

        [MenuItem("Darclite/Stat Menu/Setup Character Preview Stage")]
        public static void SetupCharacterPreviewStage()
        {
            int previewLayer = EnsureCharacterPreviewLayerExists();
            if (previewLayer < 0)
            {
                return;
            }

            GameObject stage = GameObject.Find("StatMenuPreviewStage");
            if (stage == null)
            {
                stage = new GameObject("StatMenuPreviewStage");
                Undo.RegisterCreatedObjectUndo(stage, "Create Stat Menu Preview Stage");
            }
            // Tucked far from the playable area — the camera only ever renders its own dedicated
            // layer regardless, but keeping it well out of the way avoids any chance of overlap.
            stage.transform.position = new Vector3(0f, 500f, 0f);

            Transform existingModel = stage.transform.Find("Model");
            if (existingModel != null)
            {
                Object.DestroyImmediate(existingModel.gameObject);
            }

            GameObject warriorAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SceneBootstrapper.WarriorModelPath);
            if (warriorAsset == null)
            {
                Debug.LogError($"[StatMenuBootstrapper] Could not find Warrior model at {SceneBootstrapper.WarriorModelPath}");
                return;
            }

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(warriorAsset, stage.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            foreach (Collider modelCollider in model.GetComponentsInChildren<Collider>())
            {
                Object.DestroyImmediate(modelCollider);
            }

            SceneBootstrapper.ApplyCharacterMaterial(model, "Warrior");

            Animator animator = model.GetComponent<Animator>();
            if (animator == null)
            {
                animator = model.AddComponent<Animator>();
            }
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(SceneBootstrapper.PlayerControllerPath);
            animator.applyRootMotion = false;

            Bounds bounds = SceneBootstrapper.CalculateBounds(model);
            float modelHeight = bounds.size.y > 0f ? bounds.size.y : 1.8f;

            Transform existingCamera = stage.transform.Find("PreviewCamera");
            GameObject cameraObject = existingCamera != null ? existingCamera.gameObject : new GameObject("PreviewCamera", typeof(Camera));
            cameraObject.transform.SetParent(stage.transform, false);

            // Solved from the vertical FOV instead of guessed multipliers, so the framing is
            // actually correct regardless of this model's real height: distance is whatever makes
            // the frustum's vertical span equal targetFrameHeight at that distance.
            const float fieldOfView = 40f;
            const float frameMarginFraction = 1.3f; // 30% headroom split above the head/below the feet
            float targetFrameHeight = modelHeight * frameMarginFraction;
            float halfFovRadians = Mathf.Deg2Rad * (fieldOfView * 0.5f);
            float cameraDistance = (targetFrameHeight * 0.5f) / Mathf.Tan(halfFovRadians);
            // True vertical center of the model (bounds already run from feet at local y=0 up to
            // modelHeight), so the frustum is centered on the whole body, not skewed to one end.
            float cameraHeight = modelHeight * 0.5f;

            // If she ends up facing away from the camera, this rig's authored "forward" is the
            // opposite of the usual +Z convention assumed here — flip this camera's Z
            // position/rotation 180 degrees.
            cameraObject.transform.localPosition = new Vector3(0f, cameraHeight, cameraDistance);
            cameraObject.transform.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);

            Camera previewCamera = cameraObject.GetComponent<Camera>();
            if (previewCamera == null)
            {
                previewCamera = cameraObject.AddComponent<Camera>();
            }
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            previewCamera.cullingMask = 1 << previewLayer;
            previewCamera.fieldOfView = fieldOfView;
            previewCamera.nearClipPlane = 0.05f;
            previewCamera.farClipPlane = Mathf.Max(modelHeight * 4f, 5f);
            previewCamera.orthographic = false;

            EnsureTexturesFolder();

            // 480x640 (3:4) matches the RawImage's 420x560 UI size exactly, so nothing stretches.
            const int previewTextureWidth = 480;
            const int previewTextureHeight = 640;

            RenderTexture previewTexture = AssetDatabase.LoadAssetAtPath<RenderTexture>(CharacterPreviewRenderTexturePath);
            if (previewTexture == null)
            {
                previewTexture = new RenderTexture(previewTextureWidth, previewTextureHeight, 16, RenderTextureFormat.ARGB32) { name = "StatMenuCharacterPreview" };
                AssetDatabase.CreateAsset(previewTexture, CharacterPreviewRenderTexturePath);
            }
            else if (previewTexture.width != previewTextureWidth || previewTexture.height != previewTextureHeight)
            {
                // Left over from an earlier size (e.g. the original 512x640) — resize in place so
                // the existing asset reference elsewhere (the RawImage) doesn't need re-wiring.
                previewTexture.Release();
                previewTexture.width = previewTextureWidth;
                previewTexture.height = previewTextureHeight;
                EditorUtility.SetDirty(previewTexture);
                AssetDatabase.SaveAssets();
            }
            previewCamera.targetTexture = previewTexture;

            SetLayerRecursively(stage, previewLayer);

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                mainCamera.cullingMask &= ~(1 << previewLayer);
            }
            else
            {
                Debug.LogWarning("[StatMenuBootstrapper] No main camera found — couldn't exclude the CharacterPreview layer from it. Exclude it manually if the preview character shows up during normal gameplay.");
            }

            Selection.activeGameObject = stage;
            Debug.Log("Character preview stage set up. Run 'Darclite/Stat Menu/Setup World Blur Volume' next.");
        }

        private static int EnsureCharacterPreviewLayerExists()
        {
            int existing = LayerMask.NameToLayer(CharacterPreviewLayerName);
            if (existing >= 0)
            {
                return existing;
            }

            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty layerSlot = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(layerSlot.stringValue))
                {
                    layerSlot.stringValue = CharacterPreviewLayerName;
                    tagManager.ApplyModifiedProperties();
                    return i;
                }
            }

            Debug.LogError("[StatMenuBootstrapper] No free layer slots available to create the 'CharacterPreview' layer.");
            return -1;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        // ==================== World Blur Volume ====================

        private const string BlurVolumeProfilePath = "Assets/_Project/Settings/StatMenuBlurProfile.asset";

        [MenuItem("Darclite/Stat Menu/Setup World Blur Volume")]
        public static void SetupWorldBlurVolume()
        {
            GameObject volumeObject = GameObject.Find("StatMenuBlurVolume");
            if (volumeObject == null)
            {
                volumeObject = new GameObject("StatMenuBlurVolume");
                Undo.RegisterCreatedObjectUndo(volumeObject, "Create Stat Menu Blur Volume");
            }

            Volume volume = volumeObject.GetComponent<Volume>();
            if (volume == null)
            {
                volume = volumeObject.AddComponent<Volume>();
            }

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(BlurVolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, BlurVolumeProfilePath);
            }

            if (!profile.TryGet(out DepthOfField dof))
            {
                // VolumeProfile.Add<T>() creates the override in memory but doesn't reliably
                // persist it as a sub-asset of the profile file on its own — without the explicit
                // AddObjectToAsset call, it survives only until the next domain reload, then
                // becomes a dangling/destroyed reference the moment anything reads Volume.profile.
                dof = profile.Add<DepthOfField>(true);
                AssetDatabase.AddObjectToAsset(dof, profile);
            }

            // Gaussian mode with a very short start/end distance blurs almost everything beyond
            // arm's reach of the camera fairly uniformly — a stand-in for a true full-screen blur
            // using only URP's built-in volume stack (no custom shader/render feature needed).
            dof.active = true;
            dof.mode.overrideState = true;
            dof.mode.value = DepthOfFieldMode.Gaussian;
            dof.gaussianStart.overrideState = true;
            dof.gaussianStart.value = 0.1f;
            dof.gaussianEnd.overrideState = true;
            dof.gaussianEnd.value = 3f;
            dof.gaussianMaxRadius.overrideState = true;
            dof.gaussianMaxRadius.value = 4f;
            dof.highQualitySampling.overrideState = true;
            dof.highQualitySampling.value = true;

            if (!profile.TryGet(out ColorAdjustments colorAdjustments))
            {
                colorAdjustments = profile.Add<ColorAdjustments>(true);
                AssetDatabase.AddObjectToAsset(colorAdjustments, profile);
            }

            // Darkens the scene along with the blur (both ramp together via the same Volume
            // weight) so the menu reads as a clear foreground rather than just a soft background.
            colorAdjustments.active = true;
            colorAdjustments.postExposure.overrideState = true;
            colorAdjustments.postExposure.value = -2.5f;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            volume.isGlobal = true;
            // sharedProfile, not profile — Volume.profile is a runtime-only property that isn't
            // serialized, so assigning it from an editor script (outside Play mode) silently gets
            // lost; the next Play session reads back an empty auto-created profile instead. This
            // is exactly what the "profile=" (blank) diagnostic log showed.
            volume.sharedProfile = profile;
            volume.priority = 10f;
            // Off by default — StatMenuUI ramps this to 1 while the menu is open and back to 0
            // when it closes, so normal gameplay is never blurred.
            volume.weight = 0f;

            EnsureCameraSupportsPostProcessing();

            Selection.activeGameObject = volumeObject;
            Debug.Log("World blur volume set up. Run 'Darclite/Stat Menu/Setup Stat Menu UI' next.");
        }

        // A Volume has zero visible effect unless the camera looking at it opts into post
        // processing (off by default on a plain Camera) and the URP asset has its depth texture
        // enabled (Depth of Field needs scene depth to compute blur). Both are easy to miss since
        // neither produces an error — the effect just silently does nothing.
        internal static void EnsureCameraSupportsPostProcessing()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogWarning("[StatMenuBootstrapper] No main camera found — can't verify it supports post-processing/blur.");
                return;
            }

            UniversalAdditionalCameraData cameraData = mainCamera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
            {
                cameraData = mainCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }

            if (!cameraData.renderPostProcessing)
            {
                cameraData.renderPostProcessing = true;
                EditorUtility.SetDirty(cameraData);
                Debug.Log("[StatMenuBootstrapper] Enabled 'Post Processing' on the main camera — it was off, which is why the blur had no effect.");
            }

            // Everything, so the blur volume (sitting on the default layer) always reaches the
            // main camera regardless of what layer it or the camera end up on later.
            if (cameraData.volumeLayerMask != ~0)
            {
                cameraData.volumeLayerMask = ~0;
                EditorUtility.SetDirty(cameraData);
            }

            UniversalRenderPipelineAsset urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset != null && !urpAsset.supportsCameraDepthTexture)
            {
                urpAsset.supportsCameraDepthTexture = true;
                EditorUtility.SetDirty(urpAsset);
                Debug.Log("[StatMenuBootstrapper] Enabled 'Depth Texture' on the active URP asset — Depth of Field needs it to compute blur.");
            }

            AssetDatabase.SaveAssets();
        }

        // ==================== Stat Menu UI ====================

        [MenuItem("Darclite/Stat Menu/Setup Stat Menu UI")]
        public static void SetupStatMenu()
        {
            SceneBootstrapper.EnsureEventSystem();

            GameObject existingCanvas = GameObject.Find("StatMenuCanvas");
            if (existingCanvas != null)
            {
                Object.DestroyImmediate(existingCanvas);
            }

            GameObject canvasObject = new GameObject("StatMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            SetupUIAudioPlayer(canvasObject);

            GameObject panelRoot = new GameObject("StatMenuPanel", typeof(RectTransform));
            panelRoot.transform.SetParent(canvasObject.transform, false);
            SceneBootstrapper.StretchRect(panelRoot.GetComponent<RectTransform>());

            Sprite gridSprite = CreateGridPatternSprite();
            Sprite vignetteSprite = CreateVignetteSprite();
            Sprite glowCircleSprite = CreateGlowCircleSprite();

            BuildBackground(panelRoot.transform, gridSprite, vignetteSprite);
            BuildHeaderBar(panelRoot.transform);
            var tabBar = BuildHeaderTabBar(panelRoot.transform);

            GameObject statsPageContent = new GameObject("StatsPageContent", typeof(RectTransform));
            statsPageContent.transform.SetParent(panelRoot.transform, false);
            SceneBootstrapper.StretchRect(statsPageContent.GetComponent<RectTransform>());

            const float rowStartY = -220f;
            const float rowSpacing = 90f;
            const float rowX = 60f;

            (Text pointsText, Button plusButton) liteRow = CreateStatRow(statsPageContent.transform, "LITE", rowX, rowStartY, glowCircleSprite);
            (Text pointsText, Button plusButton) strengthRow = CreateStatRow(statsPageContent.transform, "STRENGTH", rowX, rowStartY - rowSpacing, glowCircleSprite);
            (Text pointsText, Button plusButton) vitalityRow = CreateStatRow(statsPageContent.transform, "VITALITY", rowX, rowStartY - rowSpacing * 2f, glowCircleSprite);
            (Text pointsText, Button plusButton) dexterityRow = CreateStatRow(statsPageContent.transform, "DEXTERITY", rowX, rowStartY - rowSpacing * 3f, glowCircleSprite);

            Text abilityPointsNumberText = BuildAbilityPointsReadout(statsPageContent.transform, rowX, rowStartY - rowSpacing * 3f - 90f);

            RawImage previewImage = BuildCharacterPreview(statsPageContent.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-60f, -80f), new Vector2(420f, 560f));
            Text hpText = BuildHpText(statsPageContent.transform);
            var xpBar = BuildXpBar(statsPageContent.transform);

            var litePage = BuildLitePageContent(panelRoot.transform);
            var abilitiesPage = BuildAbilitiesPageContent(panelRoot.transform);

            panelRoot.SetActive(false);

            GameObject player = GameObject.Find("Player");
            PlayerStats playerStats = null;
            Combatant playerCombatant = null;
            if (player != null)
            {
                playerStats = player.GetComponent<PlayerStats>();
                if (playerStats == null)
                {
                    playerStats = player.AddComponent<PlayerStats>();
                }
                playerCombatant = player.GetComponent<Combatant>();
            }
            else
            {
                Debug.LogWarning("[StatMenuBootstrapper] No 'Player' GameObject found — run 'Darclite/Setup Player Character' first, then re-run this.");
            }

            GameObject blurVolumeObject = GameObject.Find("StatMenuBlurVolume");
            Volume blurVolume = blurVolumeObject != null ? blurVolumeObject.GetComponent<Volume>() : null;
            if (blurVolume == null)
            {
                Debug.LogWarning("[StatMenuBootstrapper] No blur Volume found — run 'Darclite/Stat Menu/Setup World Blur Volume' first, then re-run this.");
            }

            StatMenuUI statMenuUI = canvasObject.GetComponent<StatMenuUI>();
            if (statMenuUI == null)
            {
                statMenuUI = canvasObject.AddComponent<StatMenuUI>();
            }

            SerializedObject so = new SerializedObject(statMenuUI);
            so.FindProperty("panelRoot").objectReferenceValue = panelRoot;
            so.FindProperty("playerStats").objectReferenceValue = playerStats;
            so.FindProperty("playerCombatant").objectReferenceValue = playerCombatant;
            so.FindProperty("blurVolume").objectReferenceValue = blurVolume;
            so.FindProperty("abilityPointsNumberText").objectReferenceValue = abilityPointsNumberText;
            so.FindProperty("hpText").objectReferenceValue = hpText;
            so.FindProperty("xpLevelText").objectReferenceValue = xpBar.levelText;
            so.FindProperty("xpFillImage").objectReferenceValue = xpBar.fillImage;
            so.FindProperty("xpFractionText").objectReferenceValue = xpBar.fractionText;
            so.FindProperty("liteRow.pointsText").objectReferenceValue = liteRow.pointsText;
            so.FindProperty("liteRow.plusButton").objectReferenceValue = liteRow.plusButton;
            so.FindProperty("strengthRow.pointsText").objectReferenceValue = strengthRow.pointsText;
            so.FindProperty("strengthRow.plusButton").objectReferenceValue = strengthRow.plusButton;
            so.FindProperty("vitalityRow.pointsText").objectReferenceValue = vitalityRow.pointsText;
            so.FindProperty("vitalityRow.plusButton").objectReferenceValue = vitalityRow.plusButton;
            so.FindProperty("dexterityRow.pointsText").objectReferenceValue = dexterityRow.pointsText;
            so.FindProperty("dexterityRow.plusButton").objectReferenceValue = dexterityRow.plusButton;

            so.FindProperty("statsTabButton").objectReferenceValue = tabBar.stats.button;
            so.FindProperty("liteTabButton").objectReferenceValue = tabBar.lite.button;
            so.FindProperty("abilitiesTabButton").objectReferenceValue = tabBar.abilities.button;
            so.FindProperty("statsTab.tabText").objectReferenceValue = tabBar.stats.tabText;
            so.FindProperty("statsTab.underline").objectReferenceValue = tabBar.stats.underline;
            so.FindProperty("statsTab.pageContent").objectReferenceValue = statsPageContent;
            so.FindProperty("liteTab.tabText").objectReferenceValue = tabBar.lite.tabText;
            so.FindProperty("liteTab.underline").objectReferenceValue = tabBar.lite.underline;
            so.FindProperty("liteTab.pageContent").objectReferenceValue = litePage.content;
            so.FindProperty("liteAvailablePointsText").objectReferenceValue = litePage.availablePointsText;
            so.FindProperty("abilitiesTab.tabText").objectReferenceValue = tabBar.abilities.tabText;
            so.FindProperty("abilitiesTab.underline").objectReferenceValue = tabBar.abilities.underline;
            so.FindProperty("abilitiesTab.pageContent").objectReferenceValue = abilitiesPage;
            so.ApplyModifiedProperties();

            if (previewImage.texture == null)
            {
                Debug.LogWarning("[StatMenuBootstrapper] Character preview has no RenderTexture — run 'Darclite/Stat Menu/Setup Character Preview Stage' first, then re-run this.");
            }

            Selection.activeGameObject = canvasObject;
            Debug.Log("Stat menu UI set up. Press Q in Play mode to toggle it.");
        }

        private static void BuildBackground(Transform parent, Sprite gridSprite, Sprite vignetteSprite)
        {
            GameObject navyBg = new GameObject("NavyBackground", typeof(Image));
            navyBg.transform.SetParent(parent, false);
            SceneBootstrapper.StretchRect(navyBg.GetComponent<RectTransform>());
            Image navyBgImage = navyBg.GetComponent<Image>();
            navyBgImage.color = new Color(0.04f, 0.07f, 0.13f, 0.88f);
            navyBgImage.raycastTarget = true;

            GameObject gridBg = new GameObject("GridPattern", typeof(Image));
            gridBg.transform.SetParent(parent, false);
            SceneBootstrapper.StretchRect(gridBg.GetComponent<RectTransform>());
            Image gridImage = gridBg.GetComponent<Image>();
            gridImage.sprite = gridSprite;
            gridImage.type = Image.Type.Tiled;
            gridImage.color = new Color(0.4f, 0.55f, 0.75f, 0.5f);
            gridImage.raycastTarget = false;

            GameObject vignetteBg = new GameObject("Vignette", typeof(Image));
            vignetteBg.transform.SetParent(parent, false);
            SceneBootstrapper.StretchRect(vignetteBg.GetComponent<RectTransform>());
            Image vignetteImage = vignetteBg.GetComponent<Image>();
            vignetteImage.sprite = vignetteSprite;
            vignetteImage.type = Image.Type.Simple;
            vignetteImage.color = Color.white;
            vignetteImage.raycastTarget = false;
        }

        // A dark, near-opaque strip behind the tab bar with a thin separator beneath it — like
        // the reference image's top bar, distinguishing the header from the page content below
        // rather than letting the tabs float directly on the navy grid background.
        private static void BuildHeaderBar(Transform parent)
        {
            const float headerHeight = 64f;

            GameObject headerBackground = new GameObject("HeaderBackground", typeof(Image));
            headerBackground.transform.SetParent(parent, false);
            RectTransform headerRect = headerBackground.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0f, headerHeight);
            Image headerImage = headerBackground.GetComponent<Image>();
            headerImage.color = new Color(0.05f, 0.05f, 0.06f, 0.92f);
            headerImage.raycastTarget = false;

            GameObject separatorObject = new GameObject("HeaderSeparator", typeof(Image));
            separatorObject.transform.SetParent(parent, false);
            RectTransform separatorRect = separatorObject.GetComponent<RectTransform>();
            separatorRect.anchorMin = new Vector2(0f, 1f);
            separatorRect.anchorMax = new Vector2(1f, 1f);
            separatorRect.pivot = new Vector2(0.5f, 1f);
            separatorRect.anchoredPosition = new Vector2(0f, -headerHeight);
            separatorRect.sizeDelta = new Vector2(0f, 2f);
            Image separatorImage = separatorObject.GetComponent<Image>();
            separatorImage.color = new Color(1f, 1f, 1f, 0.18f);
            separatorImage.raycastTarget = false;
        }

        // Stats, Lite, and Abilities have real pages so far, so only those three get a
        // Button/underline — Strength/Vitality/Dexterity stay non-interactive darkened
        // placeholders in the bar until those trees have content of their own.
        private static ((Text tabText, GameObject underline, Button button) stats, (Text tabText, GameObject underline, Button button) lite, (Text tabText, GameObject underline, Button button) abilities) BuildHeaderTabBar(Transform parent)
        {
            string[] tabNames = { "STATS", "LITE", "STRENGTH", "VITALITY", "DEXTERITY", "ABILITIES" };
            const float tabWidth = 150f;
            const float headerY = -30f;
            float totalWidth = tabWidth * tabNames.Length;
            float startX = -totalWidth / 2f + tabWidth / 2f;

            (Text tabText, GameObject underline, Button button) statsRefs = default;
            (Text tabText, GameObject underline, Button button) liteRefs = default;
            (Text tabText, GameObject underline, Button button) abilitiesRefs = default;

            for (int i = 0; i < tabNames.Length; i++)
            {
                bool isActive = i == 0;
                bool isInteractive = i == 0 || i == 1 || i == 5;
                var refs = CreateHeaderTab(parent, tabNames[i], startX + i * tabWidth, tabWidth, headerY, isActive, isInteractive);
                if (i == 0) statsRefs = refs;
                if (i == 1) liteRefs = refs;
                if (i == 5) abilitiesRefs = refs;
            }

            return (statsRefs, liteRefs, abilitiesRefs);
        }

        private static (Text tabText, GameObject underline, Button button) CreateHeaderTab(Transform parent, string label, float x, float width, float y, bool isActive, bool isInteractive)
        {
            GameObject tabObject = new GameObject($"Tab_{label}", typeof(Text));
            tabObject.transform.SetParent(parent, false);
            RectTransform tabRect = tabObject.GetComponent<RectTransform>();
            tabRect.anchorMin = new Vector2(0.5f, 1f);
            tabRect.anchorMax = new Vector2(0.5f, 1f);
            tabRect.pivot = new Vector2(0.5f, 1f);
            tabRect.anchoredPosition = new Vector2(x, y);
            tabRect.sizeDelta = new Vector2(width, 30f);

            Text tabText = tabObject.GetComponent<Text>();
            tabText.font = SceneBootstrapper.GetGameFont();
            tabText.fontSize = 18;
            tabText.fontStyle = FontStyle.Bold;
            tabText.alignment = TextAnchor.MiddleCenter;
            tabText.text = label;
            tabText.color = isActive ? Color.white : new Color(1f, 1f, 1f, 0.5f);

            Button button = null;
            if (isInteractive)
            {
                tabText.raycastTarget = true;
                button = tabObject.AddComponent<Button>();
                button.targetGraphic = tabText;
            }
            else
            {
                tabText.raycastTarget = false;
            }

            GameObject underline = null;
            if (isInteractive)
            {
                underline = new GameObject("Underline", typeof(Image));
                underline.transform.SetParent(tabObject.transform, false);
                RectTransform underlineRect = underline.GetComponent<RectTransform>();
                underlineRect.anchorMin = new Vector2(0.5f, 0f);
                underlineRect.anchorMax = new Vector2(0.5f, 0f);
                underlineRect.pivot = new Vector2(0.5f, 0f);
                underlineRect.anchoredPosition = new Vector2(0f, -4f);
                underlineRect.sizeDelta = new Vector2(width * 0.7f, 2f);
                Image underlineImage = underline.GetComponent<Image>();
                underlineImage.color = Color.white;
                underlineImage.raycastTarget = false;
                underline.SetActive(isActive);
            }

            return (tabText, underline, button);
        }

        private static (Text pointsText, Button plusButton) CreateStatRow(Transform parent, string label, float x, float y, Sprite iconSprite)
        {
            GameObject rowObject = new GameObject($"StatRow_{label}", typeof(RectTransform));
            rowObject.transform.SetParent(parent, false);
            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(x, y);
            rowRect.sizeDelta = new Vector2(480f, 70f);

            GameObject pointsObject = new GameObject("PointsText", typeof(Text));
            pointsObject.transform.SetParent(rowObject.transform, false);
            RectTransform pointsRect = pointsObject.GetComponent<RectTransform>();
            pointsRect.anchorMin = new Vector2(0f, 0.5f);
            pointsRect.anchorMax = new Vector2(0f, 0.5f);
            pointsRect.pivot = new Vector2(0.5f, 0.5f);
            pointsRect.anchoredPosition = new Vector2(20f, 0f);
            pointsRect.sizeDelta = new Vector2(40f, 40f);
            Text pointsText = pointsObject.GetComponent<Text>();
            pointsText.font = SceneBootstrapper.GetGameFont();
            pointsText.fontSize = 26;
            pointsText.fontStyle = FontStyle.Bold;
            pointsText.color = Color.white;
            pointsText.alignment = TextAnchor.MiddleCenter;
            pointsText.text = "0";
            pointsText.raycastTarget = false;

            GameObject plusObject = new GameObject("PlusButton", typeof(Image), typeof(Button));
            plusObject.transform.SetParent(rowObject.transform, false);
            RectTransform plusRect = plusObject.GetComponent<RectTransform>();
            plusRect.anchorMin = new Vector2(0f, 0.5f);
            plusRect.anchorMax = new Vector2(0f, 0.5f);
            plusRect.pivot = new Vector2(0.5f, 0.5f);
            plusRect.anchoredPosition = new Vector2(65f, 0f);
            plusRect.sizeDelta = new Vector2(32f, 32f);
            Image plusImage = plusObject.GetComponent<Image>();
            plusImage.sprite = SceneBootstrapper.CreateRoundedRectSprite();
            plusImage.type = Image.Type.Sliced;
            plusImage.color = new Color(0.2f, 0.35f, 0.3f, 0.9f);
            Button plusButton = plusObject.GetComponent<Button>();
            plusButton.targetGraphic = plusImage;

            GameObject plusTextObject = new GameObject("Text", typeof(Text));
            plusTextObject.transform.SetParent(plusObject.transform, false);
            SceneBootstrapper.StretchRect(plusTextObject.GetComponent<RectTransform>());
            Text plusText = plusTextObject.GetComponent<Text>();
            plusText.font = SceneBootstrapper.GetGameFont();
            plusText.fontSize = 22;
            plusText.fontStyle = FontStyle.Bold;
            plusText.color = Color.white;
            plusText.alignment = TextAnchor.MiddleCenter;
            plusText.text = "+";
            plusText.raycastTarget = false;

            GameObject iconObject = new GameObject("IconCircle", typeof(Image));
            iconObject.transform.SetParent(rowObject.transform, false);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = new Vector2(130f, 0f);
            iconRect.sizeDelta = new Vector2(64f, 64f);
            Image iconImage = iconObject.GetComponent<Image>();
            iconImage.sprite = iconSprite;
            iconImage.type = Image.Type.Simple;
            iconImage.raycastTarget = false;

            GameObject labelObject = new GameObject("LabelText", typeof(Text));
            labelObject.transform.SetParent(rowObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0.5f);
            labelRect.anchorMax = new Vector2(0f, 0.5f);
            labelRect.pivot = new Vector2(0f, 0.5f);
            labelRect.anchoredPosition = new Vector2(175f, 0f);
            labelRect.sizeDelta = new Vector2(280f, 40f);
            Text labelText = labelObject.GetComponent<Text>();
            labelText.font = SceneBootstrapper.GetGameFont();
            labelText.fontSize = 22;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color = Color.white;
            labelText.alignment = TextAnchor.MiddleLeft;
            labelText.text = label;
            labelText.raycastTarget = false;

            return (pointsText, plusButton);
        }

        private static Text BuildAbilityPointsReadout(Transform parent, float x, float y)
        {
            GameObject numberObject = new GameObject("AbilityPointsNumber", typeof(Text));
            numberObject.transform.SetParent(parent, false);
            RectTransform numberRect = numberObject.GetComponent<RectTransform>();
            numberRect.anchorMin = new Vector2(0f, 1f);
            numberRect.anchorMax = new Vector2(0f, 1f);
            numberRect.pivot = new Vector2(0f, 1f);
            numberRect.anchoredPosition = new Vector2(x, y);
            numberRect.sizeDelta = new Vector2(200f, 40f);
            Text numberText = numberObject.GetComponent<Text>();
            numberText.font = SceneBootstrapper.GetGameFont();
            numberText.fontSize = 30;
            numberText.fontStyle = FontStyle.Bold;
            numberText.color = Color.white;
            numberText.alignment = TextAnchor.MiddleLeft;
            numberText.text = "0";
            numberText.raycastTarget = false;

            GameObject labelObject = new GameObject("AbilityPointsLabel", typeof(Text));
            labelObject.transform.SetParent(parent, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.pivot = new Vector2(0f, 1f);
            labelRect.anchoredPosition = new Vector2(x, y - 34f);
            labelRect.sizeDelta = new Vector2(260f, 26f);
            Text labelText = labelObject.GetComponent<Text>();
            labelText.font = SceneBootstrapper.GetGameFont();
            labelText.fontSize = 15;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color = new Color(1f, 1f, 1f, 0.85f);
            labelText.alignment = TextAnchor.MiddleLeft;
            labelText.text = "ABILITY POINTS";
            labelText.raycastTarget = false;

            return numberText;
        }

        // Shows the same live camera feed (same RenderTexture) everywhere it's used — there's one
        // shared preview stage/camera (see SetupCharacterPreviewStage) always rendering the
        // Warrior model's default Animator state, so any RawImage pointed at this texture shows
        // it doing its idle animation with no extra setup.
        private static RawImage BuildCharacterPreview(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject previewObject = new GameObject("CharacterPreview", typeof(RawImage));
            previewObject.transform.SetParent(parent, false);
            RectTransform previewRect = previewObject.GetComponent<RectTransform>();
            previewRect.anchorMin = anchorMin;
            previewRect.anchorMax = anchorMax;
            previewRect.pivot = pivot;
            previewRect.anchoredPosition = anchoredPosition;
            previewRect.sizeDelta = size;
            RawImage previewImage = previewObject.GetComponent<RawImage>();
            previewImage.texture = AssetDatabase.LoadAssetAtPath<RenderTexture>(CharacterPreviewRenderTexturePath);
            previewImage.raycastTarget = false;
            return previewImage;
        }

        private static Text BuildHpText(Transform parent)
        {
            GameObject hpObject = new GameObject("HpText", typeof(Text));
            hpObject.transform.SetParent(parent, false);
            RectTransform hpRect = hpObject.GetComponent<RectTransform>();
            hpRect.anchorMin = new Vector2(1f, 1f);
            hpRect.anchorMax = new Vector2(1f, 1f);
            hpRect.pivot = new Vector2(1f, 1f);
            hpRect.anchoredPosition = new Vector2(-60f, -655f);
            hpRect.sizeDelta = new Vector2(420f, 30f);
            Text hpText = hpObject.GetComponent<Text>();
            hpText.font = SceneBootstrapper.GetGameFont();
            hpText.fontSize = 20;
            hpText.fontStyle = FontStyle.Bold;
            hpText.color = Color.white;
            hpText.alignment = TextAnchor.MiddleCenter;
            hpText.text = "HP  300/300";
            hpText.raycastTarget = false;
            return hpText;
        }

        private static (Text levelText, Image fillImage, Text fractionText) BuildXpBar(Transform parent)
        {
            GameObject xpBarContainer = new GameObject("XpBar", typeof(RectTransform));
            xpBarContainer.transform.SetParent(parent, false);
            RectTransform xpBarRect = xpBarContainer.GetComponent<RectTransform>();
            xpBarRect.anchorMin = new Vector2(1f, 1f);
            xpBarRect.anchorMax = new Vector2(1f, 1f);
            xpBarRect.pivot = new Vector2(1f, 1f);
            xpBarRect.anchoredPosition = new Vector2(-60f, -700f);
            xpBarRect.sizeDelta = new Vector2(420f, 40f);

            GameObject levelCircleObject = new GameObject("LevelCircle", typeof(Image));
            levelCircleObject.transform.SetParent(xpBarContainer.transform, false);
            RectTransform levelCircleRect = levelCircleObject.GetComponent<RectTransform>();
            levelCircleRect.anchorMin = new Vector2(0f, 0.5f);
            levelCircleRect.anchorMax = new Vector2(0f, 0.5f);
            levelCircleRect.pivot = new Vector2(0.5f, 0.5f);
            levelCircleRect.anchoredPosition = new Vector2(20f, 0f);
            levelCircleRect.sizeDelta = new Vector2(36f, 36f);
            Image levelCircleImage = levelCircleObject.GetComponent<Image>();
            levelCircleImage.sprite = SceneBootstrapper.CreateRoundedRectSprite();
            levelCircleImage.type = Image.Type.Sliced;
            levelCircleImage.color = new Color(0.08f, 0.08f, 0.09f, 0.95f);

            GameObject levelTextObject = new GameObject("Text", typeof(Text));
            levelTextObject.transform.SetParent(levelCircleObject.transform, false);
            SceneBootstrapper.StretchRect(levelTextObject.GetComponent<RectTransform>());
            Text levelText = levelTextObject.GetComponent<Text>();
            levelText.font = SceneBootstrapper.GetGameFont();
            levelText.fontSize = 18;
            levelText.fontStyle = FontStyle.Bold;
            levelText.color = Color.white;
            levelText.alignment = TextAnchor.MiddleCenter;
            levelText.text = "1";
            levelText.raycastTarget = false;

            GameObject barContainer = new GameObject("BarContainer", typeof(RectTransform));
            barContainer.transform.SetParent(xpBarContainer.transform, false);
            RectTransform barContainerRect = barContainer.GetComponent<RectTransform>();
            barContainerRect.anchorMin = new Vector2(0f, 0.5f);
            barContainerRect.anchorMax = new Vector2(0f, 0.5f);
            barContainerRect.pivot = new Vector2(0f, 0.5f);
            barContainerRect.anchoredPosition = new Vector2(46f, 0f);
            barContainerRect.sizeDelta = new Vector2(374f, 16f);

            GameObject xpBorderObject = new GameObject("Border", typeof(Image));
            xpBorderObject.transform.SetParent(barContainer.transform, false);
            SceneBootstrapper.StretchRect(xpBorderObject.GetComponent<RectTransform>());
            Image xpBorderImage = xpBorderObject.GetComponent<Image>();
            xpBorderImage.sprite = SceneBootstrapper.CreateRoundedRectSprite();
            xpBorderImage.type = Image.Type.Sliced;
            xpBorderImage.color = new Color(0.06f, 0.06f, 0.07f);

            GameObject xpTrackObject = new GameObject("Track", typeof(Image));
            xpTrackObject.transform.SetParent(barContainer.transform, false);
            SceneBootstrapper.InsetRect(xpTrackObject.GetComponent<RectTransform>(), 2f);
            Image xpTrackImage = xpTrackObject.GetComponent<Image>();
            xpTrackImage.sprite = SceneBootstrapper.CreateRoundedRectSprite();
            xpTrackImage.type = Image.Type.Sliced;
            xpTrackImage.color = new Color(0.12f, 0.12f, 0.14f);

            GameObject xpFillObject = new GameObject("Fill", typeof(Image));
            xpFillObject.transform.SetParent(barContainer.transform, false);
            SceneBootstrapper.InsetRect(xpFillObject.GetComponent<RectTransform>(), 4f);
            Image xpFillImage = xpFillObject.GetComponent<Image>();
            xpFillImage.sprite = SceneBootstrapper.CreateSolidSprite();
            xpFillImage.type = Image.Type.Filled;
            xpFillImage.fillMethod = Image.FillMethod.Horizontal;
            xpFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            xpFillImage.fillAmount = 0f;
            xpFillImage.color = new Color(0.95f, 0.8f, 0.3f);

            GameObject fractionObject = new GameObject("FractionText", typeof(Text));
            fractionObject.transform.SetParent(xpBarContainer.transform, false);
            RectTransform fractionRect = fractionObject.GetComponent<RectTransform>();
            fractionRect.anchorMin = new Vector2(0f, 0f);
            fractionRect.anchorMax = new Vector2(1f, 0f);
            fractionRect.pivot = new Vector2(0.5f, 1f);
            fractionRect.anchoredPosition = new Vector2(0f, -20f);
            fractionRect.sizeDelta = new Vector2(0f, 20f);
            Text fractionText = fractionObject.GetComponent<Text>();
            fractionText.font = SceneBootstrapper.GetGameFont();
            fractionText.fontSize = 14;
            fractionText.color = new Color(1f, 1f, 1f, 0.8f);
            fractionText.alignment = TextAnchor.MiddleCenter;
            fractionText.text = "0/100";
            fractionText.raycastTarget = false;

            return (levelText, xpFillImage, fractionText);
        }

        // ==================== Lite Page ====================

        // Each tree is a chain of tiers rather than one flat ability — a later tier's prerequisite
        // is always the tier immediately before it in the same array, and unlocking it fully
        // replaces that earlier tier everywhere (Lite page node, Abilities page icon, hotbar)
        // rather than sitting alongside it. All costs are 0 for now — no points-spending economy
        // exists yet, so unlocking only checks the prerequisite chain.
        private static readonly (string treeTitle, (string abilityName, string iconFileName, string description, int cost)[] tiers)[] LiteTrees =
        {
            ("Attack", new[]
            {
                ("Lite Concentration", "Lite Concentration",
                    "Focus your Lite into every strike, increasing the power of your attacks.", 0),
                ("Lite Concentration II", "Lite Concentration",
                    "Focus your Lite into every strike, increasing the power of your attacks even further.", 0),
            }),
            ("Sense", new[]
            {
                ("Power Sense 1", "Power Sense 1",
                    "Sense the vitality of nearby enemies, revealing their health above their heads.", 0),
            }),
            ("Defense", new[]
            {
                ("Lite Bracing", "Lite Bracing",
                    "Brace yourself with Lite, reducing incoming damage.", 0),
            }),
            ("Restoration", new[]
            {
                ("Recovery Lite", "Recovery",
                    "Channel Lite passively to recover health more quickly over time.", 0),
            }),
        };

        // Branches off an existing chain without joining it — its prerequisite must already be
        // unlocked, but unlocking the branch neither replaces the prerequisite nor is replaced by
        // anything else in that chain (unlike LiteTrees' tiers, which always supersede the tier
        // right before them). Lite Release branches off Lite Concentration I specifically, so it
        // stays available whether or not the player has since upgraded to Lite Concentration II.
        private static readonly (string treeTitle, string prerequisiteAbilityName, string abilityName, string iconFileName, string description, int cost)[] LiteTreeBranches =
        {
            ("Attack", "Lite Concentration", "Lite Release", "Lite Release",
                "Release a burst of raw Lite energy, damaging and knocking back every enemy nearby.", 0),
        };

        private static (GameObject content, Text availablePointsText) BuildLitePageContent(Transform parent)
        {
            GameObject content = new GameObject("LitePageContent", typeof(RectTransform));
            content.transform.SetParent(parent, false);
            SceneBootstrapper.StretchRect(content.GetComponent<RectTransform>());

            Sprite lockedBackground = CreateLockedNodeBackgroundSprite();

            AbilityInfoPanelUI infoPanel = BuildAbilityInfoPanel(content.transform, new Vector2(-60f, -110f), new Vector2(420f, 260f));

            int treeCount = LiteTrees.Length;
            const float columnSpacing = 300f;
            const float baseNodeY = 40f; // slightly below center — trees grow upward from here
            float startX = -columnSpacing * (treeCount - 1) / 2f;

            for (int i = 0; i < treeCount; i++)
            {
                var tree = LiteTrees[i];
                float x = startX + i * columnSpacing;
                BuildTreeChain(content.transform, tree.treeTitle, tree.tiers, x, baseNodeY, lockedBackground, infoPanel);
                BuildTreeBranches(content.transform, tree, x, baseNodeY, lockedBackground, infoPanel);
            }

            Text availablePointsText = BuildLiteAvailablePointsReadout(content.transform);

            content.SetActive(false);
            return (content, availablePointsText);
        }

        // Bottom-of-page readout for how many banked Lite points are still unspent — the same
        // pool the Stats page's LITE row banks into, just shown here since this is where you'd
        // actually spend them once node-unlocking exists.
        private static Text BuildLiteAvailablePointsReadout(Transform parent)
        {
            GameObject container = new GameObject("AvailablePoints", typeof(RectTransform));
            container.transform.SetParent(parent, false);
            RectTransform containerRect = container.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0f);
            containerRect.anchorMax = new Vector2(0.5f, 0f);
            containerRect.pivot = new Vector2(0.5f, 0f);
            containerRect.anchoredPosition = new Vector2(0f, 40f);
            containerRect.sizeDelta = new Vector2(400f, 60f);

            GameObject numberObject = new GameObject("Number", typeof(Text));
            numberObject.transform.SetParent(container.transform, false);
            RectTransform numberRect = numberObject.GetComponent<RectTransform>();
            numberRect.anchorMin = new Vector2(0.5f, 1f);
            numberRect.anchorMax = new Vector2(0.5f, 1f);
            numberRect.pivot = new Vector2(0.5f, 1f);
            numberRect.anchoredPosition = Vector2.zero;
            numberRect.sizeDelta = new Vector2(400f, 32f);
            Text numberText = numberObject.GetComponent<Text>();
            numberText.font = SceneBootstrapper.GetGameFont();
            numberText.fontSize = 26;
            numberText.fontStyle = FontStyle.Bold;
            numberText.color = Color.white;
            numberText.alignment = TextAnchor.MiddleCenter;
            numberText.text = "0";
            numberText.raycastTarget = false;

            GameObject labelObject = new GameObject("Label", typeof(Text));
            labelObject.transform.SetParent(container.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.5f, 1f);
            labelRect.anchorMax = new Vector2(0.5f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -30f);
            labelRect.sizeDelta = new Vector2(400f, 24f);
            Text labelText = labelObject.GetComponent<Text>();
            labelText.font = SceneBootstrapper.GetGameFont();
            labelText.fontSize = 14;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color = new Color(1f, 1f, 1f, 0.85f);
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.text = "LITE POINTS TO SPEND";
            labelText.raycastTarget = false;

            // Two separate elements (number + label), matching the ability points readout's
            // style — only the number needs live updates, so that's the one returned/wired.
            return numberText;
        }

        private const float TreeTierSpacing = 190f;

        // Builds every tier of one tree stacked vertically, wiring each tier's prerequisite to
        // the ability name of the tier directly below it, and drawing a connecting line between
        // each consecutive pair. Only the base (first) tier gets the tree's title label — later
        // tiers stack under the same title rather than repeating it.
        private static void BuildTreeChain(Transform parent, string treeTitle, (string abilityName, string iconFileName, string description, int cost)[] tiers, float x, float baseY, Sprite lockedBackground, AbilityInfoPanelUI infoPanel)
        {
            for (int tierIndex = 0; tierIndex < tiers.Length; tierIndex++)
            {
                var tier = tiers[tierIndex];
                float y = baseY + tierIndex * TreeTierSpacing;
                string prerequisiteAbilityName = tierIndex > 0 ? tiers[tierIndex - 1].abilityName : string.Empty;

                if (tierIndex > 0)
                {
                    BuildTreeConnectorLine(parent, x, y - TreeTierSpacing, y);
                }

                BuildTreeNode(parent, treeTitle, tier, prerequisiteAbilityName, x, y, lockedBackground, infoPanel, showTitle: tierIndex == 0);
            }
        }

        // Plain vertical white bar — tiers in the same chain always sit directly above one
        // another (same x), so this is just the generic two-point connector below with both
        // points sharing an x.
        private static void BuildTreeConnectorLine(Transform parent, float x, float fromY, float toY)
        {
            BuildTreeConnectorLineBetween(parent, new Vector2(x, fromY), new Vector2(x, toY));
        }

        // Generic connector between two arbitrary points — used for the vertical tier-chain bars
        // above and for diagonal branch connectors (BuildTreeBranches), which hang a node off to
        // the side of its prerequisite instead of stacking directly above it. Forced to the first
        // sibling slot so it renders behind every node regardless of build order.
        private static void BuildTreeConnectorLineBetween(Transform parent, Vector2 from, Vector2 to)
        {
            GameObject lineObject = new GameObject("Connector", typeof(Image));
            lineObject.transform.SetParent(parent, false);
            RectTransform lineRect = lineObject.GetComponent<RectTransform>();
            lineRect.anchorMin = new Vector2(0.5f, 0.5f);
            lineRect.anchorMax = new Vector2(0.5f, 0.5f);
            lineRect.pivot = new Vector2(0.5f, 0.5f);
            lineRect.anchoredPosition = (from + to) * 0.5f;
            lineRect.sizeDelta = new Vector2(4f, Vector2.Distance(from, to));

            Vector2 diff = to - from;
            lineRect.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(diff.x, diff.y) * Mathf.Rad2Deg);

            Image lineImage = lineObject.GetComponent<Image>();
            lineImage.color = new Color(1f, 1f, 1f, 0.6f);
            lineImage.raycastTarget = false;

            lineObject.transform.SetAsFirstSibling();
        }

        private const float BranchXOffset = 160f;
        private const float BranchYOffset = 90f;

        // Draws any branch nodes hanging off this tree's chain — same clickable node as a normal
        // tier, just positioned diagonally off its prerequisite instead of stacking above it,
        // since unlocking it doesn't continue (or get superseded by) that chain.
        private static void BuildTreeBranches(Transform parent, (string treeTitle, (string abilityName, string iconFileName, string description, int cost)[] tiers) tree, float x, float baseY, Sprite lockedBackground, AbilityInfoPanelUI infoPanel)
        {
            foreach (var branch in LiteTreeBranches)
            {
                if (branch.treeTitle != tree.treeTitle)
                {
                    continue;
                }

                int prerequisiteTierIndex = Mathf.Max(0, System.Array.FindIndex(tree.tiers, t => t.abilityName == branch.prerequisiteAbilityName));
                float prerequisiteY = baseY + prerequisiteTierIndex * TreeTierSpacing;
                float branchX = x + BranchXOffset;
                float branchY = prerequisiteY + BranchYOffset;

                BuildTreeConnectorLineBetween(parent, new Vector2(x, prerequisiteY), new Vector2(branchX, branchY));
                BuildTreeNode(parent, tree.treeTitle, (branch.abilityName, branch.iconFileName, branch.description, branch.cost),
                    branch.prerequisiteAbilityName, branchX, branchY, lockedBackground, infoPanel, showTitle: false);
            }
        }

        private static void BuildTreeNode(Transform parent, string treeTitle, (string abilityName, string iconFileName, string description, int cost) tier, string prerequisiteAbilityName, float x, float y, Sprite lockedBackground, AbilityInfoPanelUI infoPanel, bool showTitle)
        {
            string sourcePath = $"Assets/_Project/Art/UI/{tier.iconFileName}.png";
            string outputPath = $"Assets/_Project/Textures/Icons/{tier.iconFileName}.png";
            Sprite iconSprite = ConvertGlyphToTransparentSprite(sourcePath, outputPath);
            Sprite glowSprite = CreateGlowCircleSprite();
            Sprite ringSprite = CreateHoverRingSprite();

            // Fixed outer node — never scales, so the glow halo and tree title underneath stay
            // put while the "Visual" child (background/icon/border) enlarges on hover.
            GameObject nodeObject = new GameObject($"Node_{tier.abilityName}", typeof(RectTransform));
            nodeObject.transform.SetParent(parent, false);
            RectTransform nodeRect = nodeObject.GetComponent<RectTransform>();
            nodeRect.anchorMin = new Vector2(0.5f, 0.5f);
            nodeRect.anchorMax = new Vector2(0.5f, 0.5f);
            nodeRect.pivot = new Vector2(0.5f, 0.5f);
            nodeRect.anchoredPosition = new Vector2(x, y);
            nodeRect.sizeDelta = new Vector2(90f, 90f);

            GameObject glowObject = new GameObject("HoverGlow", typeof(Image));
            glowObject.transform.SetParent(nodeObject.transform, false);
            RectTransform glowRect = glowObject.GetComponent<RectTransform>();
            glowRect.anchorMin = new Vector2(0.5f, 0.5f);
            glowRect.anchorMax = new Vector2(0.5f, 0.5f);
            glowRect.pivot = new Vector2(0.5f, 0.5f);
            glowRect.anchoredPosition = Vector2.zero;
            glowRect.sizeDelta = new Vector2(170f, 170f);
            Image glowImage = glowObject.GetComponent<Image>();
            glowImage.sprite = glowSprite;
            glowImage.type = Image.Type.Simple;
            glowImage.color = new Color(1f, 0.82f, 0.35f, 0.9f);
            glowImage.raycastTarget = false;

            GameObject visualObject = new GameObject("Visual", typeof(RectTransform));
            visualObject.transform.SetParent(nodeObject.transform, false);
            SceneBootstrapper.StretchRect(visualObject.GetComponent<RectTransform>());

            GameObject backgroundObject = new GameObject("Background", typeof(Image));
            backgroundObject.transform.SetParent(visualObject.transform, false);
            SceneBootstrapper.StretchRect(backgroundObject.GetComponent<RectTransform>());
            Image backgroundImage = backgroundObject.GetComponent<Image>();
            backgroundImage.sprite = lockedBackground;
            backgroundImage.type = Image.Type.Simple;
            // Raycastable — this is the Graphic AbilityNodeUI's pointer events fire against.
            backgroundImage.raycastTarget = true;

            Image iconImage = null;
            if (iconSprite != null)
            {
                GameObject iconObject = new GameObject("Icon", typeof(Image));
                iconObject.transform.SetParent(visualObject.transform, false);
                RectTransform iconRect = iconObject.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = Vector2.zero;
                iconRect.sizeDelta = new Vector2(52f, 52f);
                iconImage = iconObject.GetComponent<Image>();
                iconImage.sprite = iconSprite;
                iconImage.type = Image.Type.Simple;
                // Starting/locked look — AbilityNodeUI.RefreshLockVisual() re-applies the correct
                // tint on enable based on actual unlock state, so this just matches the locked
                // case as a sane default before that first runs.
                iconImage.color = new Color(0.55f, 0.57f, 0.62f, 0.9f);
                iconImage.raycastTarget = false;
            }

            GameObject borderObject = new GameObject("HoverBorder", typeof(Image));
            borderObject.transform.SetParent(visualObject.transform, false);
            RectTransform borderRect = borderObject.GetComponent<RectTransform>();
            borderRect.anchorMin = new Vector2(0.5f, 0.5f);
            borderRect.anchorMax = new Vector2(0.5f, 0.5f);
            borderRect.pivot = new Vector2(0.5f, 0.5f);
            borderRect.anchoredPosition = Vector2.zero;
            borderRect.sizeDelta = new Vector2(108f, 108f);
            Image borderImage = borderObject.GetComponent<Image>();
            borderImage.sprite = ringSprite;
            borderImage.type = Image.Type.Simple;
            borderImage.color = new Color(1f, 0.92f, 0.6f, 1f);
            borderImage.raycastTarget = false;

            if (showTitle)
            {
                GameObject titleObject = new GameObject("TreeTitle", typeof(Text));
                titleObject.transform.SetParent(nodeObject.transform, false);
                RectTransform titleRect = titleObject.GetComponent<RectTransform>();
                titleRect.anchorMin = new Vector2(0.5f, 0f);
                titleRect.anchorMax = new Vector2(0.5f, 0f);
                titleRect.pivot = new Vector2(0.5f, 1f);
                titleRect.anchoredPosition = new Vector2(0f, -14f);
                titleRect.sizeDelta = new Vector2(220f, 30f);
                Text titleText = titleObject.GetComponent<Text>();
                titleText.font = SceneBootstrapper.GetGameFont();
                titleText.fontSize = 18;
                titleText.fontStyle = FontStyle.Bold;
                titleText.color = new Color(1f, 1f, 1f, 0.85f);
                titleText.alignment = TextAnchor.MiddleCenter;
                titleText.text = treeTitle.ToUpperInvariant();
                titleText.raycastTarget = false;
            }

            AbilityNodeUI nodeUI = backgroundObject.AddComponent<AbilityNodeUI>();
            SerializedObject nodeSo = new SerializedObject(nodeUI);
            nodeSo.FindProperty("visualRoot").objectReferenceValue = visualObject.GetComponent<RectTransform>();
            nodeSo.FindProperty("hoverGlowImage").objectReferenceValue = glowImage;
            nodeSo.FindProperty("hoverBorderImage").objectReferenceValue = borderImage;
            nodeSo.FindProperty("iconImage").objectReferenceValue = iconImage;
            nodeSo.FindProperty("infoPanel").objectReferenceValue = infoPanel;
            nodeSo.FindProperty("abilityName").stringValue = tier.abilityName;
            nodeSo.FindProperty("abilityDescription").stringValue = tier.description;
            nodeSo.FindProperty("treeTitle").stringValue = treeTitle;
            nodeSo.FindProperty("cost").intValue = tier.cost;
            nodeSo.FindProperty("iconSprite").objectReferenceValue = iconSprite;
            nodeSo.FindProperty("prerequisiteAbilityName").stringValue = prerequisiteAbilityName;
            nodeSo.ApplyModifiedProperties();
        }

        private const string UIAudioFolder = "Assets/_Project/Audio/UIAudio";

        // Project-wide convention: pop on hovering something new, click on clicking something.
        private static void SetupUIAudioPlayer(GameObject canvasObject)
        {
            UIAudioPlayer audioPlayer = canvasObject.GetComponent<UIAudioPlayer>();
            if (audioPlayer == null)
            {
                audioPlayer = canvasObject.AddComponent<UIAudioPlayer>();
            }

            AudioSource audioSource = canvasObject.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = canvasObject.AddComponent<AudioSource>();
            }
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            AudioClip hoverClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{UIAudioFolder}/pop.mp3");
            AudioClip clickClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{UIAudioFolder}/click.mp3");
            if (hoverClip == null || clickClip == null)
            {
                Debug.LogWarning($"[StatMenuBootstrapper] Could not find pop.mp3/click.mp3 under {UIAudioFolder} — UI sounds will be silent.");
            }

            SerializedObject audioSo = new SerializedObject(audioPlayer);
            audioSo.FindProperty("audioSource").objectReferenceValue = audioSource;
            audioSo.FindProperty("hoverClip").objectReferenceValue = hoverClip;
            audioSo.FindProperty("clickClip").objectReferenceValue = clickClip;
            audioSo.ApplyModifiedProperties();
        }

        // Shared info panel builder — one shared panel per page (matches the reference's fixed
        // panel that just updates its contents), reused for both the Lite page's hover tooltip
        // and the Abilities page's larger, persistent click-to-pin panel.
        private static AbilityInfoPanelUI BuildAbilityInfoPanel(Transform parent, Vector2 anchoredPosition, Vector2 size, bool includeCenterIcon = false)
        {
            const float panelPadding = 20f;

            GameObject rootObject = new GameObject("AbilityInfoPanel", typeof(RectTransform));
            rootObject.transform.SetParent(parent, false);
            RectTransform rootRect = rootObject.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 1f);
            rootRect.anchoredPosition = anchoredPosition;
            rootRect.sizeDelta = size;

            GameObject panelObject = new GameObject("Panel", typeof(RectTransform));
            panelObject.transform.SetParent(rootObject.transform, false);
            SceneBootstrapper.StretchRect(panelObject.GetComponent<RectTransform>());

            Sprite roundedRect = SceneBootstrapper.CreateRoundedRectSprite();

            GameObject borderObject = new GameObject("Border", typeof(Image));
            borderObject.transform.SetParent(panelObject.transform, false);
            SceneBootstrapper.StretchRect(borderObject.GetComponent<RectTransform>());
            Image borderImage = borderObject.GetComponent<Image>();
            borderImage.sprite = roundedRect;
            borderImage.type = Image.Type.Sliced;
            borderImage.color = new Color(0.85f, 0.7f, 0.35f, 0.55f);
            borderImage.raycastTarget = false;

            GameObject backgroundObject = new GameObject("Background", typeof(Image));
            backgroundObject.transform.SetParent(panelObject.transform, false);
            SceneBootstrapper.InsetRect(backgroundObject.GetComponent<RectTransform>(), 2f);
            Image backgroundImage = backgroundObject.GetComponent<Image>();
            backgroundImage.sprite = roundedRect;
            backgroundImage.type = Image.Type.Sliced;
            backgroundImage.color = new Color(0.06f, 0.07f, 0.1f, 0.96f);
            backgroundImage.raycastTarget = false;

            GameObject titleObject = new GameObject("Title", typeof(Text));
            titleObject.transform.SetParent(panelObject.transform, false);
            RectTransform titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -panelPadding);
            titleRect.sizeDelta = new Vector2(-panelPadding * 2f, 32f);
            Text titleText = titleObject.GetComponent<Text>();
            titleText.font = SceneBootstrapper.GetGameFont();
            titleText.fontSize = 20;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.text = "ABILITY NAME";
            titleText.raycastTarget = false;

            GameObject separatorObject = new GameObject("Separator", typeof(Image));
            separatorObject.transform.SetParent(panelObject.transform, false);
            RectTransform separatorRect = separatorObject.GetComponent<RectTransform>();
            separatorRect.anchorMin = new Vector2(0f, 1f);
            separatorRect.anchorMax = new Vector2(1f, 1f);
            separatorRect.pivot = new Vector2(0.5f, 1f);
            separatorRect.anchoredPosition = new Vector2(0f, -(panelPadding + 34f));
            separatorRect.sizeDelta = new Vector2(-panelPadding * 2f, 2f);
            Image separatorImage = separatorObject.GetComponent<Image>();
            separatorImage.color = new Color(1f, 1f, 1f, 0.2f);
            separatorImage.raycastTarget = false;

            // Dead center of the WHOLE panel, not just the space below the separator — reserves
            // its own band so the description (built next) starts below it instead of overlapping.
            Image selectedIconImage = null;
            float descriptionTopInset = panelPadding + 44f;
            if (includeCenterIcon)
            {
                const float centerIconGlowSize = 190f;
                selectedIconImage = BuildCenteredAbilityIcon(panelObject.transform, 130f, centerIconGlowSize);
                descriptionTopInset = size.y * 0.5f + centerIconGlowSize * 0.5f + 15f;
            }

            GameObject descriptionObject = new GameObject("Description", typeof(Text));
            descriptionObject.transform.SetParent(panelObject.transform, false);
            RectTransform descriptionRect = descriptionObject.GetComponent<RectTransform>();
            descriptionRect.anchorMin = new Vector2(0f, 0f);
            descriptionRect.anchorMax = new Vector2(1f, 1f);
            descriptionRect.offsetMin = new Vector2(panelPadding, 44f);
            descriptionRect.offsetMax = new Vector2(-panelPadding, -descriptionTopInset);
            Text descriptionText = descriptionObject.GetComponent<Text>();
            descriptionText.font = SceneBootstrapper.GetGameFont();
            descriptionText.fontSize = 16;
            descriptionText.color = new Color(1f, 1f, 1f, 0.85f);
            descriptionText.alignment = TextAnchor.UpperLeft;
            descriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            descriptionText.verticalOverflow = VerticalWrapMode.Truncate;
            descriptionText.text = "Hover an ability to see its details here.";
            descriptionText.raycastTarget = false;

            GameObject treeLabelObject = new GameObject("TreeLabel", typeof(Text));
            treeLabelObject.transform.SetParent(panelObject.transform, false);
            RectTransform treeLabelRect = treeLabelObject.GetComponent<RectTransform>();
            treeLabelRect.anchorMin = new Vector2(0f, 0f);
            treeLabelRect.anchorMax = new Vector2(0f, 0f);
            treeLabelRect.pivot = new Vector2(0f, 0f);
            treeLabelRect.anchoredPosition = new Vector2(panelPadding, 18f);
            treeLabelRect.sizeDelta = new Vector2(220f, 24f);
            Text treeLabelText = treeLabelObject.GetComponent<Text>();
            treeLabelText.font = SceneBootstrapper.GetGameFont();
            treeLabelText.fontSize = 13;
            treeLabelText.fontStyle = FontStyle.Bold;
            treeLabelText.color = new Color(1f, 1f, 1f, 0.55f);
            treeLabelText.alignment = TextAnchor.MiddleLeft;
            treeLabelText.text = string.Empty;
            treeLabelText.raycastTarget = false;

            GameObject costObject = new GameObject("Cost", typeof(Text));
            costObject.transform.SetParent(panelObject.transform, false);
            RectTransform costRect = costObject.GetComponent<RectTransform>();
            costRect.anchorMin = new Vector2(1f, 0f);
            costRect.anchorMax = new Vector2(1f, 0f);
            costRect.pivot = new Vector2(1f, 0f);
            costRect.anchoredPosition = new Vector2(-panelPadding, 18f);
            costRect.sizeDelta = new Vector2(160f, 24f);
            Text costText = costObject.GetComponent<Text>();
            costText.font = SceneBootstrapper.GetGameFont();
            costText.fontSize = 13;
            costText.fontStyle = FontStyle.Bold;
            costText.color = Color.white;
            costText.alignment = TextAnchor.MiddleRight;
            costText.text = string.Empty;
            costText.raycastTarget = false;

            panelObject.SetActive(false);

            AbilityInfoPanelUI infoPanelUI = rootObject.AddComponent<AbilityInfoPanelUI>();
            SerializedObject infoPanelSo = new SerializedObject(infoPanelUI);
            infoPanelSo.FindProperty("panelRoot").objectReferenceValue = panelObject;
            infoPanelSo.FindProperty("titleText").objectReferenceValue = titleText;
            infoPanelSo.FindProperty("descriptionText").objectReferenceValue = descriptionText;
            infoPanelSo.FindProperty("treeLabelText").objectReferenceValue = treeLabelText;
            infoPanelSo.FindProperty("costText").objectReferenceValue = costText;
            infoPanelSo.FindProperty("selectedIconImage").objectReferenceValue = selectedIconImage;
            infoPanelSo.ApplyModifiedProperties();

            return infoPanelUI;
        }

        // Faint ambient glow behind a plain icon, no border/card — meant to sit centered inside
        // an AbilityInfoPanelUI's own panel rather than floating elsewhere on the page.
        private static Image BuildCenteredAbilityIcon(Transform parent, float displaySize, float glowSize)
        {
            GameObject rootObject = new GameObject("SelectedAbilityIcon", typeof(RectTransform));
            rootObject.transform.SetParent(parent, false);
            RectTransform rootRect = rootObject.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = new Vector2(displaySize, displaySize);

            GameObject glowObject = new GameObject("Glow", typeof(Image));
            glowObject.transform.SetParent(rootObject.transform, false);
            RectTransform glowRect = glowObject.GetComponent<RectTransform>();
            glowRect.anchorMin = new Vector2(0.5f, 0.5f);
            glowRect.anchorMax = new Vector2(0.5f, 0.5f);
            glowRect.pivot = new Vector2(0.5f, 0.5f);
            glowRect.anchoredPosition = Vector2.zero;
            glowRect.sizeDelta = new Vector2(glowSize, glowSize);
            Image glowImage = glowObject.GetComponent<Image>();
            glowImage.sprite = CreateGlowCircleSprite();
            glowImage.type = Image.Type.Simple;
            glowImage.color = new Color(1f, 0.85f, 0.45f, 0.4f);
            glowImage.raycastTarget = false;

            GameObject iconObject = new GameObject("Icon", typeof(Image));
            iconObject.transform.SetParent(rootObject.transform, false);
            SceneBootstrapper.StretchRect(iconObject.GetComponent<RectTransform>());
            Image iconImage = iconObject.GetComponent<Image>();
            iconImage.type = Image.Type.Simple;
            iconImage.color = Color.white;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;

            return iconImage;
        }

        // ==================== Abilities Page ====================

        private static readonly Color LiteCategoryColor = new Color(0.95f, 0.8f, 0.3f);
        private static readonly Color StrengthCategoryColor = new Color(0.85f, 0.35f, 0.3f);
        private static readonly Color VitalityCategoryColor = new Color(0.4f, 0.85f, 0.45f);
        private static readonly Color DexterityCategoryColor = new Color(0.35f, 0.7f, 0.85f);

        private static GameObject BuildAbilitiesPageContent(Transform parent)
        {
            GameObject content = new GameObject("AbilitiesPageContent", typeof(RectTransform));
            content.transform.SetParent(parent, false);
            SceneBootstrapper.StretchRect(content.GetComponent<RectTransform>());

            Sprite slotBackground = CreateSlotBackgroundSprite();

            // Left-aligned 2x5 grid rather than one long centered row — a single row of 10 at a
            // readable size didn't fit on screen. Each slot's number sits above it as its own
            // label, so the two rows' number labels never fight for space with the slot art.
            const int hotbarColumns = 5;
            const int hotbarRows = 2;
            const float hotbarSlotSize = 150f;
            const float hotbarGapX = 16f;
            const float hotbarGapY = 20f;
            const float hotbarNumberHeight = 26f;
            const float hotbarNumberGap = 4f;
            const float hotbarLeftMargin = 60f;
            const float hotbarY = -110f;
            const float hotbarCellHeight = hotbarNumberHeight + hotbarNumberGap + hotbarSlotSize;
            const float hotbarTotalHeight = hotbarCellHeight * hotbarRows + hotbarGapY * (hotbarRows - 1);
            const float hotbarTotalWidth = hotbarColumns * hotbarSlotSize + (hotbarColumns - 1) * hotbarGapX;

            AbilityHotbarSlotUI[] hotbarSlots = new AbilityHotbarSlotUI[hotbarColumns * hotbarRows];
            for (int row = 0; row < hotbarRows; row++)
            {
                for (int col = 0; col < hotbarColumns; col++)
                {
                    int index = row * hotbarColumns + col;
                    float x = hotbarLeftMargin + col * (hotbarSlotSize + hotbarGapX);
                    float cellTopY = hotbarY - row * (hotbarCellHeight + hotbarGapY);
                    hotbarSlots[index] = BuildAbilityHotbarSlot(content.transform, index + 1, x, cellTopY, hotbarSlotSize, slotBackground);
                }
            }

            const float infoPanelY = -110f;
            const float infoPanelWidth = 420f;
            const float infoPanelHeight = 460f;
            const float infoPanelRightMargin = 60f;
            AbilityInfoPanelUI infoPanel = BuildAbilityInfoPanel(content.transform, new Vector2(-infoPanelRightMargin, infoPanelY), new Vector2(infoPanelWidth, infoPanelHeight), includeCenterIcon: true);

            // A live character preview fills the horizontal gap between the (left-aligned) hotbar
            // and the (right-aligned) info panel. Screen-center (anchor 0.5) is NOT the middle of
            // that gap, since the hotbar's reserved width and the panel's reserved width differ —
            // offset by half that difference so it actually lands in the gap's visual center
            // regardless of resolution, instead of needing either element's exact pixel bounds.
            float hotbarRightEdge = hotbarLeftMargin + hotbarTotalWidth;
            float panelReservedWidth = infoPanelRightMargin + infoPanelWidth;
            float gapCenterOffsetX = (hotbarRightEdge - panelReservedWidth) * 0.5f;
            const float previewWidth = 300f;
            const float previewHeight = 400f;
            BuildCharacterPreview(content.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(gapCenterOffsetX, hotbarY), new Vector2(previewWidth, previewHeight));

            // Below both the hotbar and the info panel (whichever reaches lower) — stacking
            // vertically instead of squeezing beside the panel avoids any horizontal overlap
            // regardless of screen width.
            float hotbarBottom = hotbarY - hotbarTotalHeight;
            float infoPanelBottom = infoPanelY - infoPanelHeight;
            float boxTopY = Mathf.Min(hotbarBottom, infoPanelBottom) - 40f;
            const float boxWidth = 300f;
            const float boxHeight = 210f;
            const float boxGap = 24f;
            const int categoryCount = 4;
            float boxesTotalWidth = categoryCount * boxWidth + (categoryCount - 1) * boxGap;
            float boxesStartX = -boxesTotalWidth / 2f + boxWidth / 2f;

            BuildCategoryBox(content.transform, "LITE", LiteCategoryColor, boxesStartX, boxTopY, boxWidth, boxHeight, LiteTrees, slotBackground);
            BuildCategoryBox(content.transform, "STRENGTH", StrengthCategoryColor, boxesStartX + (boxWidth + boxGap), boxTopY, boxWidth, boxHeight, null, slotBackground);
            BuildCategoryBox(content.transform, "VITALITY", VitalityCategoryColor, boxesStartX + (boxWidth + boxGap) * 2f, boxTopY, boxWidth, boxHeight, null, slotBackground);
            BuildCategoryBox(content.transform, "DEXTERITY", DexterityCategoryColor, boxesStartX + (boxWidth + boxGap) * 3f, boxTopY, boxWidth, boxHeight, null, slotBackground);

            // Last sibling so it renders above every other element on the page — actively
            // dragged icons and the shrink-and-return animation both reparent here temporarily.
            GameObject dragLayerObject = new GameObject("DragLayer", typeof(RectTransform));
            dragLayerObject.transform.SetParent(content.transform, false);
            RectTransform dragLayerRect = dragLayerObject.GetComponent<RectTransform>();
            SceneBootstrapper.StretchRect(dragLayerRect);

            content.SetActive(false);

            AbilitiesPageUI pageUI = content.AddComponent<AbilitiesPageUI>();
            SerializedObject pageSo = new SerializedObject(pageUI);
            SerializedProperty slotsProp = pageSo.FindProperty("hotbarSlots");
            slotsProp.arraySize = hotbarSlots.Length;
            for (int i = 0; i < hotbarSlots.Length; i++)
            {
                slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = hotbarSlots[i];
            }
            pageSo.FindProperty("infoPanel").objectReferenceValue = infoPanel;
            pageSo.FindProperty("dragLayer").objectReferenceValue = dragLayerRect;

            var defaultTree = LiteTrees[0];
            var defaultAbility = defaultTree.tiers[0];
            pageSo.FindProperty("defaultAbilityName").stringValue = defaultAbility.abilityName;
            pageSo.FindProperty("defaultAbilityDescription").stringValue = defaultAbility.description;
            pageSo.FindProperty("defaultTreeTitle").stringValue = defaultTree.treeTitle;
            pageSo.FindProperty("defaultCost").intValue = defaultAbility.cost;
            pageSo.FindProperty("defaultIconSprite").objectReferenceValue = ConvertGlyphToTransparentSprite(
                $"Assets/_Project/Art/UI/{defaultAbility.iconFileName}.png",
                $"Assets/_Project/Textures/Icons/{defaultAbility.iconFileName}.png");
            pageSo.ApplyModifiedProperties();

            return content;
        }

        // x is the slot's LEFT edge (not center) and cellTopY is the top of the whole cell —
        // the number label is built first, above the slot, so the two never overlap regardless
        // of which row a slot is in.
        private static AbilityHotbarSlotUI BuildAbilityHotbarSlot(Transform parent, int number, float x, float cellTopY, float size, Sprite backgroundSprite)
        {
            const float numberHeight = 26f;
            const float numberGap = 4f;

            GameObject numberObject = new GameObject($"Number{number}", typeof(Text));
            numberObject.transform.SetParent(parent, false);
            RectTransform numberRect = numberObject.GetComponent<RectTransform>();
            numberRect.anchorMin = new Vector2(0f, 1f);
            numberRect.anchorMax = new Vector2(0f, 1f);
            numberRect.pivot = new Vector2(0f, 1f);
            numberRect.anchoredPosition = new Vector2(x, cellTopY);
            numberRect.sizeDelta = new Vector2(size, numberHeight);
            Text numberText = numberObject.GetComponent<Text>();
            numberText.font = SceneBootstrapper.GetGameFont();
            numberText.fontSize = 18;
            numberText.fontStyle = FontStyle.Bold;
            numberText.color = new Color(1f, 1f, 1f, 0.9f);
            numberText.alignment = TextAnchor.MiddleCenter;
            numberText.text = number.ToString();
            numberText.raycastTarget = false;
            AddGlow(numberObject, new Color(1f, 0.9f, 0.5f), 0.4f, 0.55f);

            float slotTopY = cellTopY - numberHeight - numberGap;

            GameObject slotObject = new GameObject($"Slot{number}", typeof(RectTransform));
            slotObject.transform.SetParent(parent, false);
            RectTransform slotRect = slotObject.GetComponent<RectTransform>();
            slotRect.anchorMin = new Vector2(0f, 1f);
            slotRect.anchorMax = new Vector2(0f, 1f);
            slotRect.pivot = new Vector2(0f, 1f);
            slotRect.anchoredPosition = new Vector2(x, slotTopY);
            slotRect.sizeDelta = new Vector2(size, size);

            GameObject borderObject = new GameObject("Border", typeof(Image));
            borderObject.transform.SetParent(slotObject.transform, false);
            SceneBootstrapper.StretchRect(borderObject.GetComponent<RectTransform>());
            Image borderImage = borderObject.GetComponent<Image>();
            borderImage.sprite = SceneBootstrapper.CreateRoundedRectSprite();
            borderImage.type = Image.Type.Sliced;
            borderImage.color = new Color(0.45f, 0.6f, 0.7f, 0.55f);
            borderImage.raycastTarget = false;

            GameObject backgroundObject = new GameObject("Background", typeof(Image));
            backgroundObject.transform.SetParent(slotObject.transform, false);
            SceneBootstrapper.InsetRect(backgroundObject.GetComponent<RectTransform>(), 5f);
            Image backgroundImage = backgroundObject.GetComponent<Image>();
            backgroundImage.sprite = backgroundSprite;
            backgroundImage.type = Image.Type.Sliced;
            // Raycastable — this is what the drop (and the number-key pulse's implicit hover
            // area) actually registers against; AbilityHotbarSlotUI lives on the slot root and
            // still receives OnDrop via Unity's raycast-target-to-handler-parent bubbling.
            backgroundImage.raycastTarget = true;

            AbilityHotbarSlotUI slotUI = slotObject.AddComponent<AbilityHotbarSlotUI>();
            SerializedObject slotSo = new SerializedObject(slotUI);
            slotSo.FindProperty("slotRect").objectReferenceValue = slotRect;
            slotSo.FindProperty("slotIndex").intValue = number - 1;
            slotSo.ApplyModifiedProperties();

            return slotUI;
        }

        private static void BuildCategoryBox(Transform parent, string label, Color glowColor, float x, float y, float width, float height,
            (string treeTitle, (string abilityName, string iconFileName, string description, int cost)[] tiers)[] trees,
            Sprite slotBackground)
        {
            GameObject boxObject = new GameObject($"Box_{label}", typeof(RectTransform));
            boxObject.transform.SetParent(parent, false);
            RectTransform boxRect = boxObject.GetComponent<RectTransform>();
            boxRect.anchorMin = new Vector2(0.5f, 1f);
            boxRect.anchorMax = new Vector2(0.5f, 1f);
            boxRect.pivot = new Vector2(0.5f, 1f);
            boxRect.anchoredPosition = new Vector2(x, y);
            boxRect.sizeDelta = new Vector2(width, height);

            GameObject borderObject = new GameObject("Border", typeof(Image));
            borderObject.transform.SetParent(boxObject.transform, false);
            SceneBootstrapper.StretchRect(borderObject.GetComponent<RectTransform>());
            Image borderImage = borderObject.GetComponent<Image>();
            borderImage.sprite = SceneBootstrapper.CreateRoundedRectSprite();
            borderImage.type = Image.Type.Sliced;
            borderImage.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0.4f);
            borderImage.raycastTarget = false;

            GameObject backgroundObject = new GameObject("Background", typeof(Image));
            backgroundObject.transform.SetParent(boxObject.transform, false);
            SceneBootstrapper.InsetRect(backgroundObject.GetComponent<RectTransform>(), 2f);
            Image backgroundImage = backgroundObject.GetComponent<Image>();
            backgroundImage.sprite = SceneBootstrapper.CreateRoundedRectSprite();
            backgroundImage.type = Image.Type.Sliced;
            backgroundImage.color = new Color(0.05f, 0.06f, 0.08f, 0.75f);
            backgroundImage.raycastTarget = false;

            GameObject headerObject = new GameObject("Header", typeof(Text));
            headerObject.transform.SetParent(boxObject.transform, false);
            RectTransform headerRect = headerObject.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = new Vector2(0f, -14f);
            headerRect.sizeDelta = new Vector2(-20f, 26f);
            Text headerText = headerObject.GetComponent<Text>();
            headerText.font = SceneBootstrapper.GetGameFont();
            headerText.fontSize = 18;
            headerText.fontStyle = FontStyle.Bold;
            // Crisp white fill so the letters stay legible against their own colored glow — using
            // the category color for both the fill AND the halo made them blur into one blob.
            headerText.color = Color.white;
            headerText.alignment = TextAnchor.MiddleCenter;
            headerText.text = label;
            headerText.raycastTarget = false;
            AddGlow(headerObject, glowColor);

            if (trees == null || trees.Length == 0)
            {
                return;
            }

            int totalIcons = 0;
            foreach (var tree in trees)
            {
                totalIcons += tree.tiers.Length;
                foreach (var branch in LiteTreeBranches)
                {
                    if (branch.treeTitle == tree.treeTitle)
                    {
                        totalIcons++;
                    }
                }
            }

            const float iconSize = 48f;
            const float iconGap = 12f;
            const float iconY = -60f;
            float rowWidth = totalIcons * iconSize + (totalIcons - 1) * iconGap;
            float startX = -rowWidth / 2f;

            // Every tier of every tree lays out left-to-right in one flat row (a multi-tier chain
            // just contributes more than one icon), but only tiers within the SAME chain ever
            // reference each other for the supersede/replace behavior.
            int iconPosition = 0;
            foreach (var tree in trees)
            {
                AbilityTierGateUI previousGate = null;
                for (int tierIndex = 0; tierIndex < tree.tiers.Length; tierIndex++)
                {
                    var tier = tree.tiers[tierIndex];
                    float iconX = startX + iconPosition * (iconSize + iconGap) + iconSize / 2f;
                    AbilityIconUI icon = BuildAbilityIcon(boxObject.transform,
                        (tree.treeTitle, tier.abilityName, tier.iconFileName, tier.description, tier.cost),
                        iconX, iconY, iconSize, slotBackground);

                    // On NodeRoot (the whole draggable unit), not the sub-object AbilityIconUI
                    // itself lives on — hiding needs to hide background+icon+glow+border together,
                    // and NodeRoot is what actually gets reparented on drag/equip.
                    GameObject gateObject = icon.NodeRoot.gameObject;
                    gateObject.AddComponent<CanvasGroup>();
                    AbilityTierGateUI gate = gateObject.AddComponent<AbilityTierGateUI>();
                    SerializedObject gateSo = new SerializedObject(gate);
                    gateSo.FindProperty("icon").objectReferenceValue = icon;
                    gateSo.FindProperty("previousTier").objectReferenceValue = previousGate;
                    gateSo.ApplyModifiedProperties();

                    // Back-fill the tier before this one — it doesn't know what supersedes it
                    // until this (later) tier actually exists.
                    if (previousGate != null)
                    {
                        SerializedObject previousGateSo = new SerializedObject(previousGate);
                        previousGateSo.FindProperty("supersededByAbilityName").stringValue = tier.abilityName;
                        previousGateSo.ApplyModifiedProperties();
                    }

                    previousGate = gate;
                    iconPosition++;
                }

                foreach (var branch in LiteTreeBranches)
                {
                    if (branch.treeTitle != tree.treeTitle)
                    {
                        continue;
                    }

                    float iconX = startX + iconPosition * (iconSize + iconGap) + iconSize / 2f;
                    AbilityIconUI branchIcon = BuildAbilityIcon(boxObject.transform,
                        (tree.treeTitle, branch.abilityName, branch.iconFileName, branch.description, branch.cost),
                        iconX, iconY, iconSize, slotBackground);

                    GameObject branchGateObject = branchIcon.NodeRoot.gameObject;
                    branchGateObject.AddComponent<CanvasGroup>();
                    AbilityTierGateUI branchGate = branchGateObject.AddComponent<AbilityTierGateUI>();
                    SerializedObject branchGateSo = new SerializedObject(branchGate);
                    branchGateSo.FindProperty("icon").objectReferenceValue = branchIcon;
                    // No previousTier/supersededByAbilityName — a branch stands alone, it doesn't
                    // replace its prerequisite and nothing replaces it.
                    branchGateSo.ApplyModifiedProperties();

                    iconPosition++;
                }
            }
        }

        // Smaller, title-less sibling of BuildTreeNode's ability node — used for the icons
        // inside each Abilities-page category box. Unlike the Lite tree's locked nodes, these
        // render at full brightness once unlocked (an AbilityTierGateUI added alongside this in
        // BuildCategoryBox handles staying hidden until then), stay persistently highlighted once
        // selected (not just on hover), and can be dragged into a hotbar slot — all driven by
        // AbilityIconUI rather than the Lite tree's AbilityNodeUI.
        private static AbilityIconUI BuildAbilityIcon(Transform parent, (string treeTitle, string abilityName, string iconFileName, string description, int cost) ability, float x, float y, float size, Sprite backgroundSprite)
        {
            string sourcePath = $"Assets/_Project/Art/UI/{ability.iconFileName}.png";
            string outputPath = $"Assets/_Project/Textures/Icons/{ability.iconFileName}.png";
            Sprite iconSprite = ConvertGlyphToTransparentSprite(sourcePath, outputPath);
            Sprite glowSprite = CreateGlowCircleSprite();
            Sprite ringSprite = CreateHoverRingSprite();

            GameObject nodeObject = new GameObject($"Icon_{ability.abilityName}", typeof(RectTransform));
            nodeObject.transform.SetParent(parent, false);
            RectTransform nodeRect = nodeObject.GetComponent<RectTransform>();
            nodeRect.anchorMin = new Vector2(0.5f, 1f);
            nodeRect.anchorMax = new Vector2(0.5f, 1f);
            nodeRect.pivot = new Vector2(0.5f, 1f);
            nodeRect.anchoredPosition = new Vector2(x, y);
            nodeRect.sizeDelta = new Vector2(size, size);

            float glowSize = size * 1.6f;
            GameObject glowObject = new GameObject("HoverGlow", typeof(Image));
            glowObject.transform.SetParent(nodeObject.transform, false);
            RectTransform glowRect = glowObject.GetComponent<RectTransform>();
            glowRect.anchorMin = new Vector2(0.5f, 0.5f);
            glowRect.anchorMax = new Vector2(0.5f, 0.5f);
            glowRect.pivot = new Vector2(0.5f, 0.5f);
            glowRect.anchoredPosition = Vector2.zero;
            glowRect.sizeDelta = new Vector2(glowSize, glowSize);
            Image glowImage = glowObject.GetComponent<Image>();
            glowImage.sprite = glowSprite;
            glowImage.type = Image.Type.Simple;
            glowImage.color = new Color(1f, 0.82f, 0.35f, 0.9f);
            glowImage.raycastTarget = false;

            GameObject visualObject = new GameObject("Visual", typeof(RectTransform));
            visualObject.transform.SetParent(nodeObject.transform, false);
            SceneBootstrapper.StretchRect(visualObject.GetComponent<RectTransform>());

            GameObject backgroundObject = new GameObject("Background", typeof(Image));
            backgroundObject.transform.SetParent(visualObject.transform, false);
            SceneBootstrapper.StretchRect(backgroundObject.GetComponent<RectTransform>());
            Image backgroundImage = backgroundObject.GetComponent<Image>();
            backgroundImage.sprite = backgroundSprite;
            backgroundImage.type = Image.Type.Sliced;
            backgroundImage.raycastTarget = true;

            if (iconSprite != null)
            {
                GameObject iconObject = new GameObject("Icon", typeof(Image));
                iconObject.transform.SetParent(visualObject.transform, false);
                RectTransform iconRect = iconObject.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = Vector2.zero;
                iconRect.sizeDelta = new Vector2(size * 0.62f, size * 0.62f);
                Image iconImage = iconObject.GetComponent<Image>();
                iconImage.sprite = iconSprite;
                iconImage.type = Image.Type.Simple;
                iconImage.color = Color.white;
                iconImage.raycastTarget = false;
            }

            float borderSize = size * 1.18f;
            GameObject borderObject = new GameObject("HoverBorder", typeof(Image));
            borderObject.transform.SetParent(visualObject.transform, false);
            RectTransform borderRect = borderObject.GetComponent<RectTransform>();
            borderRect.anchorMin = new Vector2(0.5f, 0.5f);
            borderRect.anchorMax = new Vector2(0.5f, 0.5f);
            borderRect.pivot = new Vector2(0.5f, 0.5f);
            borderRect.anchoredPosition = Vector2.zero;
            borderRect.sizeDelta = new Vector2(borderSize, borderSize);
            Image borderImage = borderObject.GetComponent<Image>();
            borderImage.sprite = ringSprite;
            borderImage.type = Image.Type.Simple;
            borderImage.color = new Color(1f, 0.92f, 0.6f, 1f);
            borderImage.raycastTarget = false;

            AbilityIconUI iconUI = backgroundObject.AddComponent<AbilityIconUI>();
            SerializedObject nodeSo = new SerializedObject(iconUI);
            nodeSo.FindProperty("nodeRoot").objectReferenceValue = nodeRect;
            nodeSo.FindProperty("visualRoot").objectReferenceValue = visualObject.GetComponent<RectTransform>();
            nodeSo.FindProperty("hoverGlowImage").objectReferenceValue = glowImage;
            nodeSo.FindProperty("hoverBorderImage").objectReferenceValue = borderImage;
            nodeSo.FindProperty("abilityName").stringValue = ability.abilityName;
            nodeSo.FindProperty("abilityDescription").stringValue = ability.description;
            nodeSo.FindProperty("treeTitle").stringValue = ability.treeTitle;
            nodeSo.FindProperty("cost").intValue = ability.cost;
            nodeSo.FindProperty("iconSprite").objectReferenceValue = iconSprite;
            nodeSo.ApplyModifiedProperties();

            return iconUI;
        }

        // Layers two soft Outline effects behind a Text to fake a gentle neon glow without a
        // custom shader — a tight, brighter pass for definition and a wider, fainter pass for
        // halo. Scale factors let smaller text (e.g. the hotbar numbers) use a proportionally
        // fainter glow so it doesn't swallow the glyph strokes and become unreadable.
        private static void AddGlow(GameObject textObject, Color glowColor, float distanceScale = 1f, float alphaScale = 1f)
        {
            Outline softOutline = textObject.AddComponent<Outline>();
            softOutline.effectColor = new Color(glowColor.r, glowColor.g, glowColor.b, 0.35f * alphaScale);
            softOutline.effectDistance = new Vector2(3f, 3f) * distanceScale;
            softOutline.useGraphicAlpha = true;

            Outline tightOutline = textObject.AddComponent<Outline>();
            tightOutline.effectColor = new Color(glowColor.r, glowColor.g, glowColor.b, 0.55f * alphaScale);
            tightOutline.effectDistance = new Vector2(1.5f, 1.5f) * distanceScale;
            tightOutline.useGraphicAlpha = true;
        }

        // ==================== Procedural texture generation ====================

        private const string GridPatternTexturePath = "Assets/_Project/Textures/StatMenuGridPattern.png";
        private const string VignetteTexturePath = "Assets/_Project/Textures/StatMenuVignette.png";
        private const string GlowCircleTexturePath = "Assets/_Project/Textures/StatMenuGlowCircle.png";
        private const string LockedNodeTexturePath = "Assets/_Project/Textures/StatMenuLockedNode.png";
        private const string HoverRingTexturePath = "Assets/_Project/Textures/StatMenuHoverRing.png";
        private const string SlotBackgroundTexturePath = "Assets/_Project/Textures/StatMenuSlotBackground.png";

        private static void EnsureTexturesFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Textures"))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "Textures");
            }
        }

        private static void EnsureIconsFolder()
        {
            EnsureTexturesFolder();
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Textures/Icons"))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Textures", "Icons");
            }
        }

        // The provided ability icons are white glyphs on an OPAQUE black background (confirmed by
        // sampling pixels — genuine alpha=255, not transparency), so used directly they'd render
        // as solid black squares. Treats luminance as the alpha channel instead: white stays
        // opaque, black becomes transparent, leaving just the glyph. Writes a separate processed
        // copy under Textures/Icons rather than touching the original art.
        private static Sprite ConvertGlyphToTransparentSprite(string sourceAssetPath, string outputAssetPath)
        {
            Sprite existingOutput = AssetDatabase.LoadAssetAtPath<Sprite>(outputAssetPath);
            if (existingOutput != null)
            {
                return existingOutput;
            }

            if (!File.Exists(sourceAssetPath))
            {
                Debug.LogError($"[StatMenuBootstrapper] Icon source not found at {sourceAssetPath}");
                return null;
            }

            EnsureIconsFolder();

            byte[] sourceBytes = File.ReadAllBytes(sourceAssetPath);
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            source.LoadImage(sourceBytes);

            Color[] sourcePixels = source.GetPixels();
            Color[] outputPixels = new Color[sourcePixels.Length];
            for (int i = 0; i < sourcePixels.Length; i++)
            {
                float luminance = sourcePixels[i].r;
                outputPixels[i] = new Color(1f, 1f, 1f, luminance);
            }

            Texture2D output = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            output.SetPixels(outputPixels);
            output.Apply();

            File.WriteAllBytes(outputAssetPath, output.EncodeToPNG());
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(output);

            AssetDatabase.ImportAsset(outputAssetPath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(outputAssetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(outputAssetPath);
        }

        // Flat, non-glowing dark circle with a subtle lighter ring — the "locked" counterpart to
        // CreateGlowCircleSprite's "lit up" look.
        private static Sprite CreateLockedNodeBackgroundSprite()
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(LockedNodeTexturePath);
            if (existing != null)
            {
                return existing;
            }

            EnsureTexturesFolder();

            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float radius = size / 2f;
            Color fillColor = new Color(0.16f, 0.18f, 0.22f);
            Color ringColor = new Color(0.35f, 0.38f, 0.45f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float t = dist / radius;

                    float alpha;
                    Color color;
                    if (t <= 0.85f)
                    {
                        color = fillColor;
                        alpha = 1f;
                    }
                    else if (t <= 1f)
                    {
                        color = ringColor;
                        alpha = 1f;
                    }
                    else
                    {
                        color = ringColor;
                        alpha = 0f;
                    }

                    pixels[y * size + x] = new Color(color.r, color.g, color.b, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(LockedNodeTexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(LockedNodeTexturePath, ImportAssetOptions.ForceUpdate);
            TextureImporter lockedImporter = AssetImporter.GetAtPath(LockedNodeTexturePath) as TextureImporter;
            if (lockedImporter != null)
            {
                lockedImporter.textureType = TextureImporterType.Sprite;
                lockedImporter.spriteImportMode = SpriteImportMode.Single;
                lockedImporter.mipmapEnabled = false;
                lockedImporter.filterMode = FilterMode.Bilinear;
                lockedImporter.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(LockedNodeTexturePath);
        }

        // Rounded-square "unlocked" card background for the Abilities page's hotbar/icon slots —
        // same signed-distance-field box math as SceneBootstrapper.CreateRoundedRectSprite, but
        // with a dark blue-grey fill baked in (that one is plain white, meant to be tinted via
        // Image.color, which wouldn't give these slots their own fixed look).
        private static Sprite CreateSlotBackgroundSprite()
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(SlotBackgroundTexturePath);
            if (existing != null)
            {
                return existing;
            }

            EnsureTexturesFolder();

            const int size = 64;
            const float radius = 10f;
            Color fillColor = new Color(0.09f, 0.13f, 0.17f);

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
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
                    float alpha = Mathf.Clamp01(0.5f - signedDistance) * 0.88f;
                    pixels[y * size + x] = new Color(fillColor.r, fillColor.g, fillColor.b, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(SlotBackgroundTexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(SlotBackgroundTexturePath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(SlotBackgroundTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Bilinear;
                float border = radius + 2f;
                importer.spriteBorder = new Vector4(border, border, border, border);
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(SlotBackgroundTexturePath);
        }

        // A true hex grid needs precise tiling math to avoid visible seams — this diagonal
        // lattice is a simpler, guaranteed-seamless stand-in for the reference's hex pattern.
        // Swap in real hex art later if the difference matters.
        private static Sprite CreateGridPatternSprite()
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(GridPatternTexturePath);
            if (existing != null)
            {
                return existing;
            }

            EnsureTexturesFolder();

            const int size = 64;
            const int spacing = 16;
            const int lineThickness = 1;
            Color lineColor = Color.white;
            Color clear = new Color(0f, 0f, 0f, 0f);

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int diagA = ((x + y) % spacing + spacing) % spacing;
                    int diagB = ((x - y) % spacing + spacing) % spacing;
                    bool onLine = diagA < lineThickness || diagB < lineThickness;
                    pixels[y * size + x] = onLine ? lineColor : clear;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(GridPatternTexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(GridPatternTexturePath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(GridPatternTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(GridPatternTexturePath);
        }

        private static Sprite CreateVignetteSprite()
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(VignetteTexturePath);
            if (existing != null)
            {
                return existing;
            }

            EnsureTexturesFolder();

            const int size = 256;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxDist = center.magnitude;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / maxDist;
                    float alpha = Mathf.Clamp01(Mathf.InverseLerp(0.4f, 1f, dist));
                    alpha *= alpha;
                    pixels[y * size + x] = new Color(0f, 0f, 0f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(VignetteTexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(VignetteTexturePath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(VignetteTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(VignetteTexturePath);
        }

        // Placeholder for the real category icons the user is designing — styled like the
        // reference's "lit up" (unlocked) nodes: a solid core with a soft outer glow.
        private static Sprite CreateGlowCircleSprite()
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(GlowCircleTexturePath);
            if (existing != null)
            {
                return existing;
            }

            EnsureTexturesFolder();

            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float radius = size / 2f;
            Color glowColor = new Color(1f, 0.82f, 0.35f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float t = dist / radius;

                    float alpha;
                    if (t <= 0.72f)
                    {
                        alpha = 1f;
                    }
                    else if (t <= 1f)
                    {
                        alpha = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.72f, 1f, t));
                    }
                    else
                    {
                        alpha = 0f;
                    }

                    pixels[y * size + x] = new Color(glowColor.r, glowColor.g, glowColor.b, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(GlowCircleTexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(GlowCircleTexturePath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(GlowCircleTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(GlowCircleTexturePath);
        }

        // A thin soft-edged ring, transparent everywhere else — fades in over a locked node's
        // background on hover to read as a glowing border without a separate 9-sliced asset.
        private static Sprite CreateHoverRingSprite()
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(HoverRingTexturePath);
            if (existing != null)
            {
                return existing;
            }

            EnsureTexturesFolder();

            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float radius = size / 2f;
            const float ringCenter = 0.88f;
            const float ringHalfWidth = 0.08f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float t = dist / radius;

                    float distFromRing = Mathf.Abs(t - ringCenter);
                    float alpha = Mathf.Clamp01(1f - distFromRing / ringHalfWidth);
                    alpha *= alpha;

                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(HoverRingTexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(HoverRingTexturePath, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(HoverRingTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(HoverRingTexturePath);
        }
    }
}
