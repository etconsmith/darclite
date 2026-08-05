using Darclite.Core;
using UnityEngine;

namespace Darclite.Player
{
    // Toggle ability — switched on/off exclusively via the toggle button on its Abilities-page
    // info panel (it can't be equipped to the hotbar). Read directly by BlockDodge, which extends
    // the guard/dodge window and allows canceling your own attack into a dodge while this is active.
    [AddComponentMenu("Darclite/Attack Sensing Ability")]
    public class AttackSensingAbility : MonoBehaviour
    {
        private const string AbilityName = "Attack Sensing I";

        // How much longer the guard/dodge invincibility window lasts while this is active.
        public const float GuardDurationMultiplier = 1.5f;

        public bool IsActive => AbilityLoadout.GetToggleState(AbilityName);
    }
}
