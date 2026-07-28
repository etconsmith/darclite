using UnityEditor;
using UnityEngine;

namespace Darclite.EditorTools
{
    public static class AnimationClipInspector
    {
        private static readonly string[] ClipNames =
        {
            "Idle", "Walk", "WalkBack", "WalkLeft", "WalkRight",
            "Run", "RunBack", "RunLeft", "RunRight", "Jump",
            "DodgeForward", "DodgeBack", "DodgeLeft", "DodgeRight"
        };

        [MenuItem("Darclite/Debug/Log Mixamo Clip Lengths")]
        public static void LogClipLengths()
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
                        Debug.Log($"{name}: {clip.length:0.000}s (frames: {clip.length * clip.frameRate:0}, frameRate: {clip.frameRate})");
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
