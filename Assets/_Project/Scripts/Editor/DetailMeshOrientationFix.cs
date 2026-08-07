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
    // This rotates each affected mesh's vertex data by -90 degrees about local X (Blender's Z-up ->
    // Y-up swap, sign verified against an early single-mesh version of Tree.fbx before it moved to
    // the destructible-prefab pipeline below) and scales it up 100x every time it's (re)imported, so
    // the fix survives future reimports without depending on the original Blender scene still
    // existing. Requires SceneBootstrapper.RevertTerrainDetailMeshBakeAxisConversion to have been run
    // once on GrassClump/StonePathDetail so this is the only correction applied to any of these.
    //
    // Tree.fbx is deliberately NOT in this list even though it came from the same Blender session and
    // needs the same underlying correction — it's now a multi-object Chunk_/Static_ hierarchy (for
    // BuildDestructibleStructurePrefab) instead of one single mesh, and this postprocessor's per-mesh
    // vertex rotation doesn't touch each child's relative position, so applying it here would
    // scramble the hierarchy. Tree.fbx gets the same rotate+scale correction pre-baked directly into
    // its root transform in Blender instead, before export.
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

            Quaternion correction = Quaternion.Euler(-90f, 0f, 0f);
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

                // Each of these was built in Blender with its origin at the bounding-box center
                // (object.origin_set BOUNDS), not at its base. Terrain places detail instances with
                // the prototype's origin exactly on the ground, so a center-pivoted mesh ends up
                // buried up to its middle. Shift every vertex up so the lowest point sits at Y=0.
                float minY = float.MaxValue;
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (vertices[i].y < minY) minY = vertices[i].y;
                }
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i].y -= minY;
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
                // Deliberately leave tangents untouched — none of these meshes use normal maps, so
                // tangent data is unused either way, and both hand-rotating baked tangents and
                // calling RecalculateTangents() were ruled out as the source of Unity's "importer
                // generated inconsistent result" warning (it persisted after removing both).
            }
        }

        [MenuItem("Darclite/Force Reimport Detail Meshes")]
        public static void ForceReimportDetailMeshes()
        {
            foreach (string path in AffectedModelPaths)
            {
                // All four of these went through a Blender join() at some point, which can leave
                // coincident/duplicate vertices along seams. Unity's FBX importer welds those during
                // import, and that welding step appears to be order-dependent for these meshes,
                // which is the likely source of the "importer generated inconsistent result"
                // warning on reimport. None of these need welding at this poly count/visual scale,
                // so just turn it off rather than chase the non-determinism further.
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer != null && importer.weldVertices)
                {
                    importer.weldVertices = false;
                    importer.SaveAndReimport();
                }
                else
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
                Debug.Log($"[DetailMeshOrientationFix] Reimported {path} with the orientation, scale, and ground-pivot correction applied.");
            }

            Debug.Log("[DetailMeshOrientationFix] Done. Run 'Darclite/Debug Print Detail Mesh Bounds' to confirm — sizes should now read in real meters and the tall/thin axis should be on Y instead of Z. If GrassClump or StonePathDetail were already added as Terrain detail prototypes with manually-inflated Width/Height sliders to compensate for the old tiny size, reset those sliders back down (~0.8-1.2) now that the mesh itself is correctly sized, or they'll be 100x too big. If already painted, remove and re-add the detail prototypes (or repaint) to pick up the corrected meshes.");
        }
    }
}
