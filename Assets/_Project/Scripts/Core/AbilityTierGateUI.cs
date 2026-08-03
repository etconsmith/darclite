using UnityEngine;

namespace Darclite.Core
{
    // Sits alongside an AbilityIconUI in the Abilities page's category boxes. Hides the icon
    // (via CanvasGroup, not SetActive — this component needs to keep listening for unlock events
    // even while hidden) until its own ability is actually unlocked in the Lite skill tree, and
    // stays hidden forever once whatever supersedes it (the next tier in the same chain) is
    // unlocked. Visibility is recomputed fresh from LiteSkillTreeState every time ANY ability
    // unlocks — not set imperatively once — since this exact GameObject's own gate script is also
    // subscribed to that same broadcast and would otherwise stomp a one-time "hide" call the
    // superseding tier just made, in whichever order the two subscribers happen to run.
    //
    // If THIS tier's own unlock supersedes an earlier one (previousTier != null), it also hands
    // that earlier tier's icon to AbilitiesPageUI to auto-migrate whichever hotbar slot held it
    // over to this icon instead — a one-time action, not something Refresh() needs to redo.
    [RequireComponent(typeof(CanvasGroup))]
    [AddComponentMenu("Darclite/Ability Tier Gate UI")]
    public class AbilityTierGateUI : MonoBehaviour
    {
        [SerializeField] private AbilityIconUI icon;
        [SerializeField] private AbilityTierGateUI previousTier;
        // Ability name of the tier that supersedes this one in the same chain — empty for the
        // last (currently highest-owned) tier.
        [SerializeField] private string supersededByAbilityName;

        private CanvasGroup _canvasGroup;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        private void OnEnable()
        {
            LiteSkillTreeState.AbilityUnlocked += HandleAbilityUnlocked;
            Refresh();
        }

        private void OnDisable()
        {
            LiteSkillTreeState.AbilityUnlocked -= HandleAbilityUnlocked;
        }

        private void HandleAbilityUnlocked(string unlockedAbilityName)
        {
            if (icon != null && unlockedAbilityName == icon.AbilityName && previousTier != null)
            {
                AbilitiesPageUI.Instance?.ReplaceEquippedAbility(previousTier.icon.AbilityName, icon);
            }

            Refresh();
        }

        private void Refresh()
        {
            if (icon == null)
            {
                SetVisible(false);
                return;
            }

            bool unlocked = LiteSkillTreeState.IsUnlocked(icon.AbilityName);
            bool superseded = !string.IsNullOrEmpty(supersededByAbilityName) && LiteSkillTreeState.IsUnlocked(supersededByAbilityName);
            SetVisible(unlocked && !superseded);
        }

        private void SetVisible(bool visible)
        {
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.interactable = visible;
            _canvasGroup.blocksRaycasts = visible;
        }
    }
}
