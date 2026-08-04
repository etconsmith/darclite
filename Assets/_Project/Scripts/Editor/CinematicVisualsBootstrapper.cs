using System.IO;
using Darclite.CameraSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Darclite.EditorTools
{
    // Post-processing/lighting pass toward a more "realistic PBR cinematic" look. Pairs with the
    // AshenLit shader upgrade (soft-lit, desaturated characters) and the
    // URP asset tweaks (MSAA, HDR color grading, SSAO After Opaque) done alongside this.
    public static class CinematicVisualsBootstrapper
    {
        private const string GlobalProfilePath = "Assets/Settings/SampleSceneProfile.asset";
        private const string GameplayProfilePath = "Assets/_Project/Settings/GameplayPostProcessingProfile.asset";

        [MenuItem("Darclite/Visuals/Setup Cinematic Post Processing")]
        public static void SetupCinematicPostProcessing()
        {
            VolumeProfile globalProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GlobalProfilePath);
            if (globalProfile == null)
            {
                Debug.LogError($"[CinematicVisualsBootstrapper] Could not find global Volume Profile at {GlobalProfilePath}.");
                return;
            }

            // Already Neutral tonemapping on this profile — ACES gives a filmic highlight rolloff
            // instead of a flat clamp, which is most of what reads as "cinematic" in one toggle.
            if (globalProfile.TryGet(out Tonemapping tonemapping))
            {
                tonemapping.active = true;
                tonemapping.mode.overrideState = true;
                tonemapping.mode.value = TonemappingMode.ACES;
            }

            // Contrast/saturation stay neutral — ACES alone already supplies the filmic curve, and
            // pushing those further read as heavy-handed (reported twice). A small positive
            // exposure lift, though, gently raises shadow detail that ACES's own rolloff crushes
            // harder than the old Neutral mode did, without flattening the overall contrast.
            ColorAdjustments colorAdjustments = EnsureComponent<ColorAdjustments>(globalProfile);
            colorAdjustments.active = true;
            colorAdjustments.postExposure.overrideState = true;
            colorAdjustments.postExposure.value = 0.15f;
            colorAdjustments.contrast.overrideState = true;
            colorAdjustments.contrast.value = 0f;
            colorAdjustments.saturation.overrideState = true;
            colorAdjustments.saturation.value = 0f;

            EditorUtility.SetDirty(globalProfile);

            VolumeProfile gameplayProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GameplayProfilePath);
            if (gameplayProfile == null)
            {
                Debug.LogError($"[CinematicVisualsBootstrapper] Could not find gameplay Volume Profile at {GameplayProfilePath}.");
                return;
            }

            // Was on low-quality filtering — high quality gives a softer, less blocky glow.
            if (gameplayProfile.TryGet(out Bloom bloom))
            {
                bloom.highQualityFiltering.overrideState = true;
                bloom.highQualityFiltering.value = true;
            }

            // Was present but disabled from earlier work. Gaussian mode kept bleeding blur onto
            // player/enemy silhouettes against blurred background even after pushing its distance
            // and radius down twice — a structural limitation of that mode's simple two-threshold
            // ramp, not a tuning problem. Bokeh mode computes blur from actual focus-distance/
            // aperture math instead, which separates foreground/background more carefully. Focus
            // distance covers typical combat range (player/enemies) in focus; aperture is high
            // (f/11) for a gentle, gradual falloff rather than a strong background blur.
            if (gameplayProfile.TryGet(out DepthOfField depthOfField))
            {
                depthOfField.active = true;
                depthOfField.mode.overrideState = true;
                depthOfField.mode.value = DepthOfFieldMode.Bokeh;
                depthOfField.focusDistance.overrideState = true;
                depthOfField.focusDistance.value = 15f;
                depthOfField.aperture.overrideState = true;
                depthOfField.aperture.value = 2.8f;
                depthOfField.focalLength.overrideState = true;
                depthOfField.focalLength.value = 85f;
            }

            // Deliberately faint — meant to be felt as texture, not seen as an obvious filter.
            FilmGrain filmGrain = EnsureComponent<FilmGrain>(gameplayProfile);
            filmGrain.active = true;
            filmGrain.intensity.overrideState = true;
            filmGrain.intensity.value = 0.12f;
            filmGrain.response.overrideState = true;
            filmGrain.response.value = 0.8f;

            ChromaticAberration chromaticAberration = EnsureComponent<ChromaticAberration>(gameplayProfile);
            chromaticAberration.active = true;
            chromaticAberration.intensity.overrideState = true;
            chromaticAberration.intensity.value = 0.08f;

            EditorUtility.SetDirty(gameplayProfile);
            AssetDatabase.SaveAssets();

            Debug.Log("[CinematicVisualsBootstrapper] Cinematic post-processing configured — ACES tonemapping, subtle color grade, higher quality bloom, distance blur past ~25-55m, faint film grain and chromatic aberration.");
        }

        // VolumeProfile.Add<T>() only creates the component in memory — it does NOT register it as
        // a sub-asset of the profile file, so without AddObjectToAsset the reference goes dangling
        // the moment the AssetDatabase reloads (the exact bug that corrupted these two profiles the
        // first time this ran). Always route new component creation through this.
        private static T EnsureComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T component))
            {
                return component;
            }

            component = profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        private const string ProceduralSkyboxPath = "Assets/Settings/DarcliteProceduralSky.mat";
        // Unity ships this as part of the core SRP package — reusing it instead of hand-authoring a
        // LensFlareDataSRP asset avoids guessing at the data-driven flare element format.
        private const string DefaultLensFlarePath = "Packages/com.unity.render-pipelines.core/Runtime/RenderPipelineResources/Default Lens Flare (SRP).asset";

        // Skybox + sun rig — a procedural sky (tracks the actual directional light's direction, so
        // the visible sun disc always matches where the light is actually coming from) plus a
        // lens flare on that same light for the "sun rays" flourish. Deliberately muted/desaturated
        // tint to stay in the same Ashen-esque family as the character shader rather than a vivid
        // saturated sky that would clash with it.
        [MenuItem("Darclite/Visuals/Setup Skybox and Sun Lighting")]
        public static void SetupSkyboxAndSunLighting()
        {
            Shader proceduralSkyShader = Shader.Find("Skybox/Procedural");
            if (proceduralSkyShader == null)
            {
                Debug.LogError("[CinematicVisualsBootstrapper] Could not find the built-in Skybox/Procedural shader.");
                return;
            }

            Material sky = AssetDatabase.LoadAssetAtPath<Material>(ProceduralSkyboxPath);
            if (sky == null)
            {
                sky = new Material(proceduralSkyShader);
                AssetDatabase.CreateAsset(sky, ProceduralSkyboxPath);
            }
            else
            {
                sky.shader = proceduralSkyShader;
            }

            sky.SetFloat("_SunSize", 0.09f);
            sky.SetFloat("_SunSizeConvergence", 5f);
            sky.SetFloat("_AtmosphereThickness", 0.9f);
            sky.SetColor("_SkyTint", new Color(0.55f, 0.58f, 0.62f));
            sky.SetColor("_GroundColor", new Color(0.5f, 0.47f, 0.44f));
            sky.SetFloat("_Exposure", 1.2f);
            EditorUtility.SetDirty(sky);
            AssetDatabase.SaveAssets();

            GameObject sunObject = GameObject.Find("Directional Light");
            if (sunObject == null)
            {
                Debug.LogError("[CinematicVisualsBootstrapper] Could not find a 'Directional Light' GameObject in the scene.");
                return;
            }
            Light sun = sunObject.GetComponent<Light>();

            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            // Subtle haze — mainly here to give the sun's rays/flare something to read against,
            // not to obscure the scene. Tuned low on purpose; raise fogDensity if you want more.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.6f, 0.61f, 0.63f);
            RenderSettings.fogDensity = 0.006f;

            // A shallow-ish angle reads more dramatically (long shadows, grazing light) than sun
            // straight overhead — nudge if it doesn't suit your map's layout.
            sunObject.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            if (sun != null)
            {
                sun.color = new Color(1f, 0.95f, 0.85f);
                sun.intensity = Mathf.Max(sun.intensity, 1.2f);

                LensFlareComponentSRP flare = sunObject.GetComponent<LensFlareComponentSRP>();
                if (flare == null)
                {
                    flare = sunObject.AddComponent<LensFlareComponentSRP>();
                }
                flare.lensFlareData = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(DefaultLensFlarePath);
                flare.intensity = 1.2f;
                flare.scale = 1f;
                flare.useOcclusion = true;
                flare.occlusionRadius = 0.3f;
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[CinematicVisualsBootstrapper] Procedural sky + sun lighting configured. Remember to save the scene (Ctrl+S). " +
                "Adjust the Directional Light's rotation in the Inspector to change where shadows/rays fall.");
        }

        private const string RendererDataPath = "Assets/Settings/PC_Renderer.asset";
        private const string GodRayShaderName = "Darclite/GodRays";
        private const string GodRayMaterialPath = "Assets/_Project/Materials/GodRays.mat";
        private const string GodRayFeatureName = "God Rays";

        // Screen-space volumetric god rays via URP's built-in Full Screen Pass Renderer Feature —
        // reusing Unity's own feature for the render-pipeline integration (rather than a hand-written
        // ScriptableRendererFeature/RenderGraph pass) so the only custom code is the shader itself
        // and a small script tracking the sun's screen position (GodRaySunTracker).
        [MenuItem("Darclite/Visuals/Setup Volumetric God Rays")]
        public static void SetupGodRays()
        {
            Shader godRayShader = Shader.Find(GodRayShaderName);
            if (godRayShader == null)
            {
                Debug.LogError($"[CinematicVisualsBootstrapper] Could not find shader {GodRayShaderName} — check the Console for a GodRays.shader compile error.");
                return;
            }

            Material godRayMaterial = AssetDatabase.LoadAssetAtPath<Material>(GodRayMaterialPath);
            if (godRayMaterial == null)
            {
                godRayMaterial = new Material(godRayShader);
                AssetDatabase.CreateAsset(godRayMaterial, GodRayMaterialPath);
            }
            else
            {
                godRayMaterial.shader = godRayShader;
            }

            // Explicit, not left to shader defaults — a material that already existed on disk
            // keeps whatever values it was first saved with even after the shader's own defaults
            // change (the exact reason the first run's dangerously-high intensity/weight survived
            // a shader edit). Always force these back to known-safe values.
            godRayMaterial.SetColor("_RayColor", new Color(1f, 0.92f, 0.75f));
            godRayMaterial.SetFloat("_RayIntensity", 0.4f);
            godRayMaterial.SetFloat("_RayDecay", 0.96f);
            godRayMaterial.SetFloat("_RayWeight", 0.06f);
            godRayMaterial.SetFloat("_RayDensity", 1f);
            godRayMaterial.SetFloat("_RayContrast", 3f);
            EditorUtility.SetDirty(godRayMaterial);

            UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererDataPath);
            if (rendererData == null)
            {
                Debug.LogError($"[CinematicVisualsBootstrapper] Could not find renderer data at {RendererDataPath}.");
                return;
            }

            FullScreenPassRendererFeature godRayFeature = null;
            foreach (ScriptableRendererFeature existing in rendererData.rendererFeatures)
            {
                if (existing != null && existing is FullScreenPassRendererFeature existingFullScreen && existingFullScreen.name == GodRayFeatureName)
                {
                    godRayFeature = existingFullScreen;
                    break;
                }
            }

            if (godRayFeature == null)
            {
                godRayFeature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                godRayFeature.name = GodRayFeatureName;
                rendererData.rendererFeatures.Add(godRayFeature);
                // Same lesson as VolumeProfile.Add<T>() earlier this session — a freshly created
                // ScriptableObject reference in a list isn't actually persisted until it's
                // registered as a sub-asset, or the reference goes dangling on the next reload.
                AssetDatabase.AddObjectToAsset(godRayFeature, rendererData);
            }

            godRayFeature.passMaterial = godRayMaterial;
            godRayFeature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
            godRayFeature.requirements = ScriptableRenderPassInput.Depth;
            godRayFeature.fetchColorBuffer = true;

            rendererData.SetDirty();
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();

            GameObject sunObject = GameObject.Find("Directional Light");
            Light sun = sunObject != null ? sunObject.GetComponent<Light>() : null;
            if (sun == null)
            {
                Debug.LogWarning("[CinematicVisualsBootstrapper] No 'Directional Light' found — the god ray effect is wired up but has nothing to track yet. Run 'Setup Skybox and Sun Lighting' first, or assign a light manually on the GodRaySunTracker component.");
            }

            GameObject cameraObject = GameObject.Find("Main Camera");
            if (cameraObject != null)
            {
                GodRaySunTracker tracker = cameraObject.GetComponent<GodRaySunTracker>();
                if (tracker == null)
                {
                    tracker = cameraObject.AddComponent<GodRaySunTracker>();
                }
                SerializedObject trackerSo = new SerializedObject(tracker);
                trackerSo.FindProperty("sun").objectReferenceValue = sun;
                trackerSo.FindProperty("godRayMaterial").objectReferenceValue = godRayMaterial;
                trackerSo.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }
            else
            {
                Debug.LogWarning("[CinematicVisualsBootstrapper] No 'Main Camera' found — couldn't wire up the sun tracker automatically.");
            }

            Debug.Log("[CinematicVisualsBootstrapper] Volumetric god rays configured. Remember to save the scene (Ctrl+S). " +
                "This only reads well where the sun can actually be occluded by something (trees, rooftops, terrain) — " +
                "tune Ray Intensity/Decay/Weight/Density on the 'GodRays' material to taste.");
        }

        private const string ReflectionProbeObjectName = "Darclite Reflection Probe";
        private const float ReflectionProbeBoxSize = 40f;
        private const float ReflectionProbeHeight = 24f;

        // Baked (not realtime) — cheap at runtime, and this project's already been flagged for
        // performance issues, so a realtime probe isn't worth the risk for what's mainly a
        // specular/reflection quality improvement.
        [MenuItem("Darclite/Visuals/Add Reflection Probe")]
        public static void AddReflectionProbe()
        {
            GameObject probeObject = GameObject.Find(ReflectionProbeObjectName);
            if (probeObject == null)
            {
                probeObject = new GameObject(ReflectionProbeObjectName, typeof(ReflectionProbe));
                Undo.RegisterCreatedObjectUndo(probeObject, "Create Reflection Probe");
            }

            GameObject player = GameObject.Find("Player");
            Vector3 position = player != null ? player.transform.position + Vector3.up * 2f : new Vector3(0f, 2f, 0f);
            probeObject.transform.position = position;

            ReflectionProbe probe = probeObject.GetComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
            probe.hdr = true;
            probe.boxProjection = true;
            probe.resolution = 256;
            probe.size = new Vector3(ReflectionProbeBoxSize, ReflectionProbeHeight, ReflectionProbeBoxSize);
            probe.center = Vector3.zero;
            probe.intensity = 1f;

            Selection.activeGameObject = probeObject;
            Debug.Log("[CinematicVisualsBootstrapper] Reflection probe placed at the player's spawn position — " +
                "move/resize it in the Scene view to cover the area you want reflections in, then use " +
                "Window > Rendering > Lighting > Generate Lighting (or right-click the probe > Bake) to bake it. " +
                "Add more copies of this GameObject for other areas of the map.");
        }
    }
}
