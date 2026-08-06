using Darclite.Core;
using UnityEngine;

namespace Darclite.Player
{
    // Toggle ability — a small, always-on reduction to knockback distance while active, switched
    // on/off exclusively via the toggle button on its Abilities-page info panel (it can't be
    // equipped to the hotbar). Read directly by Combatant.KnockbackSlide.
    [AddComponentMenu("Darclite/Steady Stance Ability")]
    public class SteadyStanceAbility : MonoBehaviour
    {
        private const string AbilityName = "Steady Stance";

        public const float KnockbackReductionFraction = 0.2f;

        public bool IsActive => AbilityLoadout.GetToggleState(AbilityName);
    }
}
