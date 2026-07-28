using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Darclite.EditorTools
{
    public static class SkeletonInspector
    {
        private static readonly string[] CharacterNames =
        {
            "Warrior", "Monk", "Rogue", "Cleric", "Ranger", "Wizard"
        };

        [MenuItem("Darclite/Debug/Dump Character Skeletons")]
        public static void DumpSkeletons()
        {
            StringBuilder sb = new StringBuilder();

            foreach (string name in CharacterNames)
            {
                string path = $"Assets/_Project/Art/Characters/RPGCharacterPack/Models/{name}.fbx";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    sb.AppendLine($"[Missing] {name} at {path}");
                    continue;
                }

                sb.AppendLine($"=== {name} ===");
                AppendHierarchy(prefab.transform, sb, 0);

                sb.AppendLine($"--- {name} animation clips ---");
                Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                bool foundClip = false;
                foreach (Object asset in subAssets)
                {
                    if (asset is AnimationClip clip)
                    {
                        sb.AppendLine($"  {clip.name} ({clip.length:0.00}s)");
                        foundClip = true;
                    }
                }
                if (!foundClip)
                {
                    sb.AppendLine("  (none)");
                }

                sb.AppendLine();
            }

            string outputPath = Path.Combine(Application.dataPath, "..", "skeleton_dump.txt");
            File.WriteAllText(outputPath, sb.ToString());
            Debug.Log($"Skeleton dump written to {outputPath}");
        }

        private static void AppendHierarchy(Transform t, StringBuilder sb, int depth)
        {
            sb.AppendLine(new string(' ', depth * 2) + t.name);
            foreach (Transform child in t)
            {
                AppendHierarchy(child, sb, depth + 1);
            }
        }
    }
}
