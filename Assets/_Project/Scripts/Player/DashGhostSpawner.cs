using UnityEngine;

namespace Darclite.Player
{
    public static class DashGhostSpawner
    {
        public static void Spawn(GameObject modelRoot, Color color, float lifetime)
        {
            Shader ghostShader = Shader.Find("Darclite/GhostUnlit");
            if (ghostShader == null || modelRoot == null)
            {
                return;
            }

            foreach (Renderer renderer in modelRoot.GetComponentsInChildren<Renderer>())
            {
                if (renderer.GetComponent<ExcludeFromDashGhost>() != null)
                {
                    continue;
                }

                Mesh mesh = null;
                Transform referenceTransform = renderer.transform;
                Vector3 scale = referenceTransform.lossyScale;

                if (renderer is SkinnedMeshRenderer skinned)
                {
                    mesh = new Mesh();
                    skinned.BakeMesh(mesh);

                    // BakeMesh's output vertices are already in real-world scale (positioned
                    // relative to the root bone), so no additional lossyScale should be applied.
                    if (skinned.rootBone != null)
                    {
                        referenceTransform = skinned.rootBone;
                    }
                    scale = Vector3.one;
                }
                else if (renderer.TryGetComponent(out MeshFilter meshFilter) && meshFilter.sharedMesh != null)
                {
                    mesh = meshFilter.sharedMesh;
                }

                if (mesh == null)
                {
                    continue;
                }

                GameObject ghost = new GameObject("DashGhost");
                ghost.transform.SetPositionAndRotation(referenceTransform.position, referenceTransform.rotation);
                ghost.transform.localScale = scale;

                MeshFilter ghostFilter = ghost.AddComponent<MeshFilter>();
                ghostFilter.sharedMesh = mesh;

                Material material = new Material(ghostShader);
                material.SetColor("_Color", color);

                MeshRenderer ghostRenderer = ghost.AddComponent<MeshRenderer>();
                ghostRenderer.sharedMaterial = material;

                DashGhostFader fader = ghost.AddComponent<DashGhostFader>();
                fader.Initialize(material, color, lifetime);
            }
        }
    }
}
