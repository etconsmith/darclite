using UnityEngine;
using UnityEngine.EventSystems;

namespace Darclite.Core
{
    // A single hotbar slot's drop target — Unity's EventSystem walks up from whatever Graphic
    // was actually hit (the slot's Border/Background image) to find this on the slot root, so it
    // doesn't need its own raycastable Graphic as long as one of its children has one.
    [AddComponentMenu("Darclite/Ability Hotbar Slot UI")]
    public class AbilityHotbarSlotUI : MonoBehaviour, IDropHandler
    {
        [SerializeField] private RectTransform slotRect;
        // 0-based index into AbilityLoadout — lets AbilitiesPageUI report equip/unequip changes
        // against the shared loadout data without needing to search the slot array for "which one".
        [SerializeField] private int slotIndex;

        public RectTransform SlotRect => slotRect;
        public int SlotIndex => slotIndex;
        public AbilityIconUI EquippedIcon { get; private set; }

        public void SetEquipped(AbilityIconUI icon)
        {
            EquippedIcon = icon;
        }

        public void Clear()
        {
            EquippedIcon = null;
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (eventData.pointerDrag == null)
            {
                return;
            }

            AbilityIconUI dragged = eventData.pointerDrag.GetComponent<AbilityIconUI>();
            if (dragged == null)
            {
                return;
            }

            // AbilityIconUI's own drag handlers already no-op for toggle abilities (the icon never
            // visually moves), but Unity's EventSystem still treats it as "the dragged object" and
            // delivers this drop regardless — so it has to be rejected here too, not just at the
            // source.
            if (AbilityLoadout.IsToggleAbility(dragged.AbilityName))
            {
                return;
            }

            dragged.NotifyHandledByDrop();
            AbilitiesPageUI.Instance?.HandleDrop(dragged, this);
        }
    }
}
