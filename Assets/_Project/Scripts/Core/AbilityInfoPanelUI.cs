using UnityEngine;
using UnityEngine.UI;

namespace Darclite.Core
{
    // One shared info panel per page — each AbilityNodeUI holds a direct reference to the
    // specific panel it should drive (a page can have its own, so a hover-tooltip on one page
    // and a persistent click-to-pin panel on another never fight over the same instance).
    [AddComponentMenu("Darclite/Ability Info Panel UI")]
    public class AbilityInfoPanelUI : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text titleText;
        [SerializeField] private Text descriptionText;
        [SerializeField] private Text treeLabelText;
        [SerializeField] private Text costText;
        // Optional — only wired on pages that also show a big "currently selected ability" icon
        // elsewhere on screen (e.g. the Abilities page); left unset, this is simply skipped.
        [SerializeField] private Image selectedIconImage;
        // Optional — only wired on the Abilities page's panel. Shown only when the currently
        // displayed ability is a toggle type (Power Sense 1, Attack Sensing I, ...), since those
        // can't be equipped and this button is their only way to switch on/off.
        [SerializeField] private GameObject toggleButtonRoot;
        [SerializeField] private Button toggleButton;
        [SerializeField] private Text toggleButtonText;

        private string _currentAbilityName;

        private void Awake()
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }

            if (toggleButton != null)
            {
                toggleButton.onClick.AddListener(HandleToggleButtonClicked);
            }
        }

        private void OnEnable()
        {
            AbilityLoadout.ToggleChanged += HandleToggleChanged;
        }

        private void OnDisable()
        {
            AbilityLoadout.ToggleChanged -= HandleToggleChanged;
        }

        public void Show(string abilityName, string description, string treeTitle, int cost, Sprite icon = null)
        {
            _currentAbilityName = abilityName;

            if (titleText != null)
            {
                titleText.text = abilityName;
            }
            if (descriptionText != null)
            {
                descriptionText.text = description;
            }
            if (treeLabelText != null)
            {
                treeLabelText.text = $"{treeTitle.ToUpperInvariant()} TREE";
            }
            if (costText != null)
            {
                costText.text = cost == 1 ? "Cost: 1 Point" : $"Cost: {cost} Points";
            }
            if (selectedIconImage != null)
            {
                selectedIconImage.sprite = icon;
                selectedIconImage.enabled = icon != null;
            }

            if (toggleButtonRoot != null)
            {
                bool isToggle = AbilityLoadout.IsToggleAbility(abilityName);
                toggleButtonRoot.SetActive(isToggle);
                if (isToggle)
                {
                    RefreshToggleButtonText();
                }
            }

            if (panelRoot != null)
            {
                panelRoot.SetActive(true);
            }
        }

        public void Hide()
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }
        }

        private void HandleToggleButtonClicked()
        {
            if (_currentAbilityName == null)
            {
                return;
            }

            AbilityLoadout.ToggleAbility(_currentAbilityName);
        }

        private void HandleToggleChanged(string abilityName, bool isOn)
        {
            if (abilityName != _currentAbilityName)
            {
                return;
            }

            RefreshToggleButtonText();
        }

        private void RefreshToggleButtonText()
        {
            if (toggleButtonText == null || _currentAbilityName == null)
            {
                return;
            }

            toggleButtonText.text = AbilityLoadout.GetToggleState(_currentAbilityName) ? "Toggle Off" : "Toggle On";
        }
    }
}
