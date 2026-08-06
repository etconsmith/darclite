using UnityEditor;
using UnityEngine;

namespace Darclite.EditorTools
{
    // Mirrors PlayerLayerSetup's layer-creation pattern — writes directly into
    // ProjectSettings/TagManager.asset via SerializedObject rather than requiring the user to
    // create the layer by hand in Edit > Project Settings > Tags and Layers.
    public static class DestructibleLayerSetup
    {
        public const string DestructibleLayerName = "Destructible";

        public static int EnsureDestructibleLayerExists()
        {
            int existing = LayerMask.NameToLayer(DestructibleLayerName);
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
                    layerSlot.stringValue = DestructibleLayerName;
                    tagManager.ApplyModifiedProperties();
                    return i;
                }
            }

            Debug.LogError("No free layer slots available to create the 'Destructible' layer.");
            return -1;
        }
    }
}
