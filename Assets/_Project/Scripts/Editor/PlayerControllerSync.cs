using Darclite.Player;
using UnityEditor;
using UnityEngine;

namespace Darclite.EditorTools
{
    public static class PlayerControllerSync
    {
        [MenuItem("Darclite/Debug/Sync Dodge Values From Script")]
        public static void SyncDodgeValues()
        {
            GameObject player = GameObject.Find("Player");
            if (player == null)
            {
                Debug.LogError("No 'Player' GameObject found in the scene.");
                return;
            }

            PlayerController controller = player.GetComponent<PlayerController>();
            if (controller == null)
            {
                Debug.LogError("Player has no PlayerController component.");
                return;
            }

            SerializedObject so = new SerializedObject(controller);
            LogAndSync(so, "doubleTapWindow");
            LogAndSync(so, "dodgeSpeed");
            LogAndSync(so, "dodgeDuration");
            LogAndSync(so, "dodgeCooldown");
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(controller);
        }

        private static void LogAndSync(SerializedObject so, string propertyName)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"[PlayerControllerSync] Could not find property '{propertyName}'.");
                return;
            }

            float before = property.floatValue;

            // Reading the script's compiled default requires a scratch instance, since the
            // serialized scene value silently overrides the C# field default otherwise.
            GameObject temp = new GameObject("__TempDefaults") { hideFlags = HideFlags.HideAndDontSave };
            PlayerController fresh = temp.AddComponent<PlayerController>();
            SerializedObject freshSo = new SerializedObject(fresh);
            float scriptDefault = freshSo.FindProperty(propertyName).floatValue;
            Object.DestroyImmediate(temp);

            property.floatValue = scriptDefault;
            Debug.Log($"[PlayerControllerSync] {propertyName}: {before} -> {scriptDefault}");
        }
    }
}
