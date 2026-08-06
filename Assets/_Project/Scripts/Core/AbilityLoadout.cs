using System;
using System.Collections.Generic;
using UnityEngine;

namespace Darclite.Core
{
    // Plain data model for what's currently equipped in each of the 10 ability slots, decoupled
    // from any specific UI. AbilitiesPageUI writes to it whenever a drag-and-drop equip/unequip
    // happens; the always-visible in-game hotbar HUD only reads it, so the two can exist (or not
    // exist yet) independently of each other.
    public static class AbilityLoadout
    {
        public const int SlotCount = 10;

        private static readonly Sprite[] _slotIcons = new Sprite[SlotCount];
        private static readonly string[] _slotAbilityNames = new string[SlotCount];

        public static event Action<int, Sprite> SlotChanged;
        // Fired when a slot's ability is actually cast (key pressed, off cooldown) — not on equip.
        public static event Action<int> Activated;

        // Toggle-type abilities (Power Sense 1, Attack Sensing I, ...) skip the hotbar entirely —
        // they can't be equipped to a slot at all, and are switched on/off exclusively via the
        // toggle button on their info panel in the Abilities page. Their on/off state lives here,
        // keyed by ability name, so any number of listeners (an enemy's health bar, a player buff,
        // the info panel's own button label) can stay in sync without a slot index to key off of.
        private static readonly HashSet<string> ToggleAbilityNames = new HashSet<string>
        {
            "Power Sense 1",
            "Attack Sensing I",
            "Lite Spark",
            "Steady Focus",
            "Lite Skin",
            "Steady Stance",
            "Brace Reflex",
            "Lite Trickle",
        };

        private static readonly Dictionary<string, bool> _toggleStates = new Dictionary<string, bool>();

        public static event Action<string, bool> ToggleChanged;

        public static bool IsToggleAbility(string abilityName)
        {
            return abilityName != null && ToggleAbilityNames.Contains(abilityName);
        }

        public static bool GetToggleState(string abilityName)
        {
            return abilityName != null && _toggleStates.TryGetValue(abilityName, out bool isOn) && isOn;
        }

        public static void ToggleAbility(string abilityName)
        {
            if (abilityName == null)
            {
                return;
            }

            bool isOn = !GetToggleState(abilityName);
            _toggleStates[abilityName] = isOn;
            ToggleChanged?.Invoke(abilityName, isOn);
        }

        public static Sprite GetSlot(int index)
        {
            return index >= 0 && index < SlotCount ? _slotIcons[index] : null;
        }

        public static string GetAbilityName(int index)
        {
            return index >= 0 && index < SlotCount ? _slotAbilityNames[index] : null;
        }

        public static void SetSlot(int index, Sprite icon, string abilityName)
        {
            if (index < 0 || index >= SlotCount)
            {
                return;
            }

            _slotIcons[index] = icon;
            _slotAbilityNames[index] = abilityName;
            SlotChanged?.Invoke(index, icon);
        }

        public static void ClearSlot(int index)
        {
            SetSlot(index, null, null);
        }

        public static void NotifyActivated(int index)
        {
            if (index < 0 || index >= SlotCount)
            {
                return;
            }

            Activated?.Invoke(index);
        }
    }
}
