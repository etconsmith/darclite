using UnityEngine;

namespace Darclite.Player
{
    // Marks a renderer that lives under the character model but isn't part of its visible body —
    // an ability VFX proxy mesh, for example — so DashGhostSpawner's generic "clone every renderer
    // under the model" sweep skips it instead of spawning an unwanted ghost duplicate of it.
    [AddComponentMenu("Darclite/Exclude From Dash Ghost")]
    public class ExcludeFromDashGhost : MonoBehaviour
    {
    }
}
