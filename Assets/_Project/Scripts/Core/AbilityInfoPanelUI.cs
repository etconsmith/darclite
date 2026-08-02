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

        private void Awake()
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }
        }

        public void Show(string abilityName, string description, string treeTitle, int cost, Sprite icon = null)
        {
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
    }
}
