using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Darclite.EditorTools
{
    // GrassClump.fbx, StonePathDetail.fbx, and StonePaver.fbx all import with two compounding bugs
    // that never showed up on the House/HouseDark/HouseCabin/HouseTwoStory models despite using the
    // same Blender export call: (1) their vertex data lands in Blender's Z-up local space instead of
    // Unity's Y-up, and (2) they come in 100x too small. Confirmed via
    // SceneBootstrapper.DebugPrintDetailMeshBounds, which showed each mesh's "tall"/"thin" axis
    // sitting on local Z instead of Y, and absolute sizes ~100x smaller than modeled (e.g. a
    // 0.5m-wide paver importing with a 0.005 bounds size). Regular prefab rendering never revealed
    // either issue because it goes through the GameObject's transform and scene-placement scaling;
    // Unity Terrain's Detail Mesh renderer reads raw local mesh vertices directly and ignores both,
    // so the Blender-space orientation and true (tiny) size show through directly.
    //
    // This rotates each affected mesh's vertex data by 90 degrees about local X (Blender's Z-up ->
    // Y-up swap) and scales it up 100x every time it's (re)imported, so the fix survives future
    // reimports without depending on the original Blender scene still existing.
    public class DetailMeshOrientationFix : AssetPostprocessor
    {
        private const float ScaleCorrection = 100f;

        private static readonly HashSet<string> AffectedModelPaths = new HashSet<string>
        {
            "Assets/_Project/Art/Environment/GrassClump.fbx",
            "Assets/_Project/Art/Environment/StonePathDetail.fbx",
            "Assets/_Project/Art/Environment/StonePaver.fbx"
        };

        private void OnPostprocessModel(GameObject model)
        {
            if (!AffectedModelPaths.Contains(assetPath))
            {
                return;
            }

            Quaternion correction = Quaternion.Euler(90f, 0f, 0f);
            MeshFilter[] meshFilters = model.GetComponentsInChildren<MeshFilter>(true);
            foreach (MeshFilter meshFilter in meshFilters)
            {
                Mesh mesh = meshFilter.sharedMesh;
                if (mesh == null) continue;

                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = (correction * vertices[i]) * ScaleCorrection;
                }
                mesh.vertices = vertices;

                Vector3[] normals = mesh.normals;
                if (normals != null && normals.Length == vertices.Length)
                {
                    for (int i = 0; i < normals.Length; i++)
                    {
                        normals[i] = correction * normals[i];
                    }
                    mesh.normals = normals;
                }

                mesh.RecalculateBounds();
                // Recompute rather than manually rotate the baked tangents — hand-rotating them
                // was causing Unity's importer to report "inconsistent result" across reimports
                // (likely fighting with its own Mikktspace tangent-generation step), and neither
                // mesh needs custom baked tangents in the first place.
                mesh.RecalculateTangents();
            }
        }

        [MenuItem("Darclite/Force Reimport Detail Meshes")]
        public static void ForceReimportDetailMeshes()
        {
            foreach (string path in AffectedModelPaths)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Debug.Log($"[DetailMeshOrientationFix] Reimported {path} with the orientation + 100x scale correction applied.");
            }

            Debug.Log("[DetailMeshOrientationFix] Done. Run 'Darclite/Debug Print Detail Mesh Bounds' to confirm — sizes should now read in real meters and the tall/thin axis should be on Y instead of Z. If GrassClump or StonePathDetail were already added as Terrain detail prototypes with manually-inflated Width/Height sliders to compensate for the old tiny size, reset those sliders back down (~0.8-1.2) now that the mesh itself is correctly sized, or they'll be 100x too big. If already painted, remove and re-add the detail prototypes (or repaint) to pick up the corrected meshes.");
        }
    }
}
