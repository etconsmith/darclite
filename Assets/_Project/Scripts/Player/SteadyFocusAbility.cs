using Darclite.Core;
using UnityEngine;

namespace Darclite.Player
{
    // Toggle ability — a small, always-on reduction to camera shake while active, switched on/off
    // exclusively via the toggle button on its Abilities-page info panel (it can't be equipped to
    // the hotbar). Read directly by ThirdPersonOrbitCamera.Shake, applied regardless of what
    // triggered the shake (an ability cast, a hard landing, etc.).
    [AddComponentMenu("Darclite/Steady Focus Ability")]
    public class SteadyFocusAbility : MonoBehaviour
    {
        private const string AbilityName = "Steady Focus";

        public const float ShakeReductionFraction = 0.3f;

        public bool IsActive => AbilityLoadout.GetToggleState(AbilityName);
    }
}
