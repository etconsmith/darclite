using Darclite.CameraSystem;
using UnityEditor;
using UnityEngine;

namespace Darclite.EditorTools
{
    public static class PlayerLayerSetup
    {
        private const string PlayerLayerName = "Player";

        [MenuItem("Darclite/Setup Player Layer And Camera Collision")]
        public static void SetupPlayerLayer()
        {
            GameObject player = GameObject.Find("Player");
            if (player == null)
            {
                Debug.LogError("No 'Player' GameObject found in the scene.");
                return;
            }

            int playerLayer = EnsurePlayerLayerExists();
            if (playerLayer < 0)
            {
                return;
            }

            SetLayerRecursively(player, playerLayer);

            ThirdPersonOrbitCamera cam = Object.FindAnyObjectByType<ThirdPersonOrbitCamera>();
            if (cam == null)
            {
                Debug.LogError("No ThirdPersonOrbitCamera found in the scene.");
                return;
            }

            SerializedObject so = new SerializedObject(cam);
            SerializedProperty maskProp = so.FindProperty("collisionMask");
            maskProp.intValue &= ~(1 << playerLayer);
            so.ApplyModifiedProperties();

            Debug.Log($"Player and its children moved to layer '{PlayerLayerName}' ({playerLayer}); camera collision mask now excludes it.");
        }

        private static int EnsurePlayerLayerExists()
        {
            int existing = LayerMask.NameToLayer(PlayerLayerName);
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
                    layerSlot.stringValue = PlayerLayerName;
                    tagManager.ApplyModifiedProperties();
                    return i;
                }
            }

            Debug.LogError("No free layer slots available to create the 'Player' layer.");
            return -1;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
