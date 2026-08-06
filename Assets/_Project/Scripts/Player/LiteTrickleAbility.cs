using Darclite.Combat;
using Darclite.Core;
using UnityEngine;

namespace Darclite.Player
{
    // Toggle ability — passively regenerates a small fraction of max health per second while
    // toggled on AND the player is standing still, switched on/off exclusively via the toggle
    // button on its Abilities-page info panel (it can't be equipped to the hotbar). Moving just
    // pauses accrual rather than resetting it, so stepping away for a moment doesn't lose whatever
    // partial progress had already built up toward the next whole point of healing.
    [AddComponentMenu("Darclite/Lite Trickle Ability")]
    public class LiteTrickleAbility : MonoBehaviour
    {
        private const string AbilityName = "Lite Trickle";
        private const float HealFractionPerSecond = 0.004f;

        [SerializeField] private Combatant combatant;
        [SerializeField] private PlayerController playerController;

        private float _healAccumulator;

        public bool IsActive => AbilityLoadout.GetToggleState(AbilityName);

        private void Update()
        {
            if (!IsActive || combatant == null || playerController == null || playerController.IsMoving || combatant.IsDead)
            {
                return;
            }

            _healAccumulator += combatant.MaxHealth * HealFractionPerSecond * Time.deltaTime;
            if (_healAccumulator >= 1f)
            {
                int wholeHeal = Mathf.FloorToInt(_healAccumulator);
                combatant.Heal(wholeHeal);
                _healAccumulator -= wholeHeal;
            }
        }
    }
}
