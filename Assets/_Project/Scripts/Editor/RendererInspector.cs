using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Darclite.EditorTools
{
    public static class RendererInspector
    {
        [MenuItem("Darclite/Debug/Dump Player Renderers")]
        public static void DumpPlayerRenderers()
        {
            GameObject player = GameObject.Find("Player");
            if (player == null)
            {
                Debug.LogError("No 'Player' GameObject found in the scene.");
                return;
            }

            StringBuilder sb = new StringBuilder();
            foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>())
            {
                Bounds localBounds = renderer is SkinnedMeshRenderer skinned
                    ? skinned.localBounds
                    : (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null
                        ? mf.sharedMesh.bounds
                        : default);

                sb.AppendLine($"=== {renderer.gameObject.name} ({renderer.GetType().Name}) ===");
                sb.AppendLine($"  worldBounds center: {renderer.bounds.center}  size: {renderer.bounds.size}");
                sb.AppendLine($"  localBounds center: {localBounds.center}  size: {localBounds.size}");
                sb.AppendLine($"  lossyScale: {renderer.transform.lossyScale}");
                sb.AppendLine($"  materials: {string.Join(", ", System.Array.ConvertAll(renderer.sharedMaterials, m => m != null ? m.name : "null"))}");
                sb.AppendLine();
            }

            string outputPath = Path.Combine(Application.dataPath, "..", "renderer_dump.txt");
            File.WriteAllText(outputPath, sb.ToString());
            Debug.Log($"Renderer dump written to {outputPath}");
        }
    }
}
