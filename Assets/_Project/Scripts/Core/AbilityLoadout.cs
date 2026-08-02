using System;
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
