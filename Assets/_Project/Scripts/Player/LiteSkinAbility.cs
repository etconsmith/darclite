using Darclite.Core;
using UnityEngine;

namespace Darclite.Player
{
    // Toggle ability — a small, always-on reduction to incoming damage while active, switched
    // on/off exclusively via the toggle button on its Abilities-page info panel (it can't be
    // equipped to the hotbar). Read directly by Combatant.ApplyDamage, stacking with Lite Bracing's
    // own (much larger, temporary) reduction rather than being replaced by it.
    [AddComponentMenu("Darclite/Lite Skin Ability")]
    public class LiteSkinAbility : MonoBehaviour
    {
        private const string AbilityName = "Lite Skin";

        public const float DamageReductionFraction = 0.05f;

        public bool IsActive => AbilityLoadout.GetToggleState(AbilityName);
    }
}
