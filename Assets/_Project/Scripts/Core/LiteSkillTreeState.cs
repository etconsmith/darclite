using System;
using System.Collections.Generic;

namespace Darclite.Core
{
    // Tracks which Lite skill-tree abilities the player has unlocked this session. Every ability
    // currently costs 0 (no points-spending economy exists yet) — unlocking just requires its
    // prerequisite (the previous tier in its chain, if any) to already be unlocked.
    public static class LiteSkillTreeState
    {
        private static readonly HashSet<string> _unlockedAbilities = new HashSet<string>();

        // Fired for the ability that was just unlocked — both the Lite page's tree nodes and the
        // Abilities page's icons listen to this to refresh their own visuals independently.
        public static event Action<string> AbilityUnlocked;

        public static bool IsUnlocked(string abilityName)
        {
            return !string.IsNullOrEmpty(abilityName) && _unlockedAbilities.Contains(abilityName);
        }

        public static bool CanUnlock(string abilityName, string prerequisiteAbilityName)
        {
            if (string.IsNullOrEmpty(abilityName) || IsUnlocked(abilityName))
            {
                return false;
            }

            return string.IsNullOrEmpty(prerequisiteAbilityName) || IsUnlocked(prerequisiteAbilityName);
        }

        public static bool TryUnlock(string abilityName, string prerequisiteAbilityName)
        {
            if (!CanUnlock(abilityName, prerequisiteAbilityName))
            {
                return false;
            }

            _unlockedAbilities.Add(abilityName);
            AbilityUnlocked?.Invoke(abilityName);
            return true;
        }
    }
}
