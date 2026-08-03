using System.Collections;
using UnityEngine;

namespace Darclite.Core
{
    // Central manager for the Abilities page: tracks which single ability icon is currently
    // selected (highlighted + shown in the info panel) across the whole page, and resolves
    // drag-and-drop between category boxes and hotbar slots, including animating a displaced
    // ability back to its category box when another one takes its slot. Actually casting an
    // ability (number key -> cooldown) is handled by AbilityHotbarHudUI instead, since that bar
    // is always present in-game regardless of whether this menu page is even open.
    [AddComponentMenu("Darclite/Abilities Page UI")]
    public class AbilitiesPageUI : MonoBehaviour
    {
        public static AbilitiesPageUI Instance { get; private set; }

        [SerializeField] private AbilityHotbarSlotUI[] hotbarSlots = new AbilityHotbarSlotUI[10];
        [SerializeField] private AbilityInfoPanelUI infoPanel;
        // Shared overlay parent for actively-dragged icons and the shrink-and-return animation —
        // renders on top of everything else and is free of any layout that would fight a
        // world-position lerp across the reparent boundary.
        [SerializeField] private RectTransform dragLayer;

        [Header("Default Ability")]
        [SerializeField] private string defaultAbilityName;
        [TextArea(2, 4)]
        [SerializeField] private string defaultAbilityDescription;
        [SerializeField] private string defaultTreeTitle;
        [SerializeField] private int defaultCost = 1;
        [SerializeField] private Sprite defaultIconSprite;

        [Header("Return Animation")]
        [SerializeField] private float returnAnimationDuration = 0.3f;

        public RectTransform DragLayer => dragLayer;

        private AbilityIconUI _selectedIcon;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            infoPanel?.Show(defaultAbilityName, defaultAbilityDescription, defaultTreeTitle, defaultCost, defaultIconSprite);
        }

        // ==================== Selection ====================

        public void SelectIcon(AbilityIconUI icon)
        {
            if (icon == null)
            {
                return;
            }

            if (_selectedIcon == icon)
            {
                icon.SetSelected(false);
                _selectedIcon = null;
                infoPanel?.Show(defaultAbilityName, defaultAbilityDescription, defaultTreeTitle, defaultCost, defaultIconSprite);
                return;
            }

            if (_selectedIcon != null)
            {
                _selectedIcon.SetSelected(false);
            }

            _selectedIcon = icon;
            icon.SetSelected(true);
            infoPanel?.Show(icon.AbilityName, icon.AbilityDescription, icon.TreeTitle, icon.Cost, icon.IconSprite);
        }

        // ==================== Drag and drop ====================

        // targetSlot is null when the icon was dropped somewhere with no AbilityHotbarSlotUI —
        // empty page space, a category box, or anywhere else that isn't a valid equip target.
        public void HandleDrop(AbilityIconUI icon, AbilityHotbarSlotUI targetSlot)
        {
            if (icon == null)
            {
                return;
            }

            AbilityHotbarSlotUI previousSlot = icon.CurrentSlot;

            if (targetSlot == null)
            {
                if (previousSlot != null)
                {
                    previousSlot.Clear();
                    AbilityLoadout.ClearSlot(previousSlot.SlotIndex);
                    icon.CurrentSlot = null;
                }
                StartCoroutine(AnimateReturnHome(icon));
                return;
            }

            if (targetSlot == previousSlot)
            {
                // Dropped back into the slot it already occupied — settle back into place.
                SnapIntoSlot(icon, targetSlot);
                return;
            }

            AbilityIconUI displaced = targetSlot.EquippedIcon;
            if (displaced != null && displaced != icon)
            {
                displaced.CurrentSlot = null;
                targetSlot.Clear();
                StartCoroutine(AnimateReturnHome(displaced));
            }

            if (previousSlot != null)
            {
                previousSlot.Clear();
                AbilityLoadout.ClearSlot(previousSlot.SlotIndex);
            }

            targetSlot.SetEquipped(icon);
            icon.CurrentSlot = targetSlot;
            AbilityLoadout.SetSlot(targetSlot.SlotIndex, icon.IconSprite, icon.AbilityName);
            SnapIntoSlot(icon, targetSlot);
        }

        // Called by AbilityTierGateUI when unlocking a new tier supersedes an already-equipped
        // earlier one — moves whichever hotbar slot(s) held the old ability over to the new one's
        // icon automatically, the same way a manual drag-drop would, since the player never
        // dragged anything themselves for this to happen.
        public void ReplaceEquippedAbility(string oldAbilityName, AbilityIconUI newIcon)
        {
            for (int i = 0; i < hotbarSlots.Length; i++)
            {
                AbilityHotbarSlotUI slot = hotbarSlots[i];
                AbilityIconUI oldIcon = slot != null ? slot.EquippedIcon : null;
                if (oldIcon == null || oldIcon.AbilityName != oldAbilityName)
                {
                    continue;
                }

                oldIcon.CurrentSlot = null;
                slot.Clear();

                slot.SetEquipped(newIcon);
                newIcon.CurrentSlot = slot;
                AbilityLoadout.SetSlot(slot.SlotIndex, newIcon.IconSprite, newIcon.AbilityName);
                SnapIntoSlot(newIcon, slot);
            }
        }

        // Slots always center their occupant regardless of scale, unlike a category box's
        // top-anchored layout — so equipping switches the node to center anchoring/pivot.
        private static void SnapIntoSlot(AbilityIconUI icon, AbilityHotbarSlotUI slot)
        {
            RectTransform node = icon.NodeRoot;
            node.SetParent(slot.SlotRect, false);
            node.anchorMin = new Vector2(0.5f, 0.5f);
            node.anchorMax = new Vector2(0.5f, 0.5f);
            node.pivot = new Vector2(0.5f, 0.5f);
            node.anchoredPosition = Vector2.zero;
            node.localScale = Vector3.one * icon.EquippedScale;
        }

        private static void RestoreHome(AbilityIconUI icon)
        {
            RectTransform node = icon.NodeRoot;
            node.SetParent(icon.HomeParent, false);
            node.anchorMin = icon.HomeAnchorMin;
            node.anchorMax = icon.HomeAnchorMax;
            node.pivot = icon.HomePivot;
            node.anchoredPosition = icon.HomeAnchoredPosition;
            node.localScale = Vector3.one;
        }

        // Shrinks and moves the icon from wherever it currently is back to its home slot in its
        // category box. Uses the drag layer to lerp world position/scale freely, since anchored
        // position isn't directly comparable across different parents (or anchor conventions).
        private IEnumerator AnimateReturnHome(AbilityIconUI icon)
        {
            RectTransform node = icon.NodeRoot;
            Vector3 startWorldPos = node.position;
            Vector3 startScale = node.localScale;

            // Briefly place it at its real destination to read off the world position it will
            // end up with, then move it back to animate from its current spot.
            RestoreHome(icon);
            Canvas.ForceUpdateCanvases();
            Vector3 endWorldPos = node.position;

            if (dragLayer != null)
            {
                node.SetParent(dragLayer, true);
            }
            node.position = startWorldPos;
            node.localScale = startScale;

            float timer = 0f;
            while (timer < returnAnimationDuration)
            {
                timer += Time.deltaTime;
                float t = timer / returnAnimationDuration;
                node.position = Vector3.Lerp(startWorldPos, endWorldPos, t);
                node.localScale = Vector3.Lerp(startScale, Vector3.one, t);
                yield return null;
            }

            RestoreHome(icon);
        }
    }
}
