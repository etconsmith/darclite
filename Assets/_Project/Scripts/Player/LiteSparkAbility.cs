using Darclite.Core;
using UnityEngine;

namespace Darclite.Player
{
    // Toggle ability — a small, always-on damage boost while active, switched on/off exclusively
    // via the toggle button on its Abilities-page info panel (it can't be equipped to the hotbar).
    // Read directly by AttackCombo and applied to every punch, stacking with whatever else is
    // active (Lite Concentration, Forceful Strike) rather than being replaced by them.
    [AddComponentMenu("Darclite/Lite Spark Ability")]
    public class LiteSparkAbility : MonoBehaviour
    {
        private const string AbilityName = "Lite Spark";

        public const float DamageMultiplier = 1.05f;

        public bool IsActive => AbilityLoadout.GetToggleState(AbilityName);
    }
}
