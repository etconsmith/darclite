using UnityEditor;
using UnityEngine;

namespace Darclite.EditorTools
{
    public static class GhostBakeInspector
    {
        [MenuItem("Darclite/Debug/Log Skinned Mesh Bake Info")]
        public static void LogBakeInfo()
        {
            GameObject player = GameObject.Find("Player");
            if (player == null)
            {
                Debug.LogError("No 'Player' GameObject found in the scene.");
                return;
            }

            SkinnedMeshRenderer[] renderers = player.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (renderers.Length == 0)
            {
                Debug.LogError("No SkinnedMeshRenderer found under Player.");
                return;
            }

            foreach (SkinnedMeshRenderer skinned in renderers)
            {
                Mesh baked = new Mesh();
                skinned.BakeMesh(baked);

                Debug.Log($"=== {skinned.gameObject.name} ===");
                Debug.Log($"  renderer.transform: pos={skinned.transform.position}, lossyScale={skinned.transform.lossyScale}");
                Debug.Log($"  renderer.localBounds: center={skinned.localBounds.center}, size={skinned.localBounds.size}");
                Debug.Log($"  renderer.bounds (world): center={skinned.bounds.center}, size={skinned.bounds.size}");

                if (skinned.rootBone != null)
                {
                    Debug.Log($"  rootBone: {skinned.rootBone.name}, pos={skinned.rootBone.position}, lossyScale={skinned.rootBone.lossyScale}");
                }
                else
                {
                    Debug.Log("  rootBone: null");
                }

                Debug.Log($"  bakedMesh.bounds (local, unscaled): center={baked.bounds.center}, size={baked.bounds.size}");

                Object.DestroyImmediate(baked);
            }
        }
    }
}
