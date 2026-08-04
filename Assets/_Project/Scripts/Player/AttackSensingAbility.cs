using Darclite.Core;
using UnityEngine;

namespace Darclite.Player
{
    // Toggle ability — pressing its hotbar key flips it on/off; no timed duration, it just stays
    // whichever state until pressed again. Read directly by BlockDodge, which extends the guard/
    // dodge window and allows canceling your own attack into a dodge while this is active.
    [AddComponentMenu("Darclite/Attack Sensing Ability")]
    public class AttackSensingAbility : MonoBehaviour
    {
        private const string AbilityName = "Attack Sensing I";

        // How much longer the guard/dodge invincibility window lasts while this is active.
        public const float GuardDurationMultiplier = 1.5f;

        public bool IsActive { get; private set; }

        private void OnEnable()
        {
            AbilityLoadout.Activated += HandleActivated;
        }

        private void OnDisable()
        {
            AbilityLoadout.Activated -= HandleActivated;
        }

        private void HandleActivated(int slotIndex)
        {
            if (AbilityLoadout.GetAbilityName(slotIndex) != AbilityName)
            {
                return;
            }

            IsActive = !IsActive;
        }
    }
}
