using UnityEditor;
using UnityEngine;

namespace Darclite.EditorTools
{
    public static class DodgeDirectionInspector
    {
        private static readonly string[] ClipNames =
        {
            "DodgeForward", "DodgeBack", "DodgeLeft", "DodgeRight"
        };

        [MenuItem("Darclite/Debug/Log Dodge Clip Directions")]
        public static void LogDodgeDirections()
        {
            foreach (string name in ClipNames)
            {
                string path = $"Assets/_Project/Animations/Mixamo/{name}.fbx";
                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
                bool found = false;

                foreach (Object asset in assets)
                {
                    if (asset is AnimationClip clip && !clip.name.Contains("__preview__"))
                    {
                        Vector3 speed = clip.averageSpeed;
                        Debug.Log($"{name}: averageSpeed = (x:{speed.x:0.000} lateral, y:{speed.y:0.000} vertical, z:{speed.z:0.000} forward/back), length {clip.length:0.00}s");
                        found = true;
                    }
                }

                if (!found)
                {
                    Debug.LogWarning($"{name}: clip not found at {path}");
                }
            }
        }
    }
}
