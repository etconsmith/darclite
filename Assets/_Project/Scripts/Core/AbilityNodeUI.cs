using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Darclite.Core
{
    // Hover feedback for a single tree node: smoothly enlarges, fades in a glow halo + border
    // ring, plays the hover pop sound, and shows the shared info panel. Click doesn't do
    // anything yet (no unlock logic exists), but still plays the click sound so the node already
    // feels responsive once that logic is added.
    [AddComponentMenu("Darclite/Ability Node UI")]
    public class AbilityNodeUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        // The part that visually enlarges on hover (background + icon + border ring) — kept
        // separate from the node's glow halo and tree-title label, neither of which should scale.
        [SerializeField] private RectTransform visualRoot;
        [SerializeField] private Image hoverGlowImage;
        [SerializeField] private Image hoverBorderImage;
        [SerializeField] private AbilityInfoPanelUI infoPanel;
        // Lite page tree nodes preview on hover (and hide on exit) like a tooltip. Abilities-page
        // icons instead pin the (persistent, always-visible) detail panel only on click, so
        // hovering there just plays the glow/enlarge/pop feedback without touching it.
        [SerializeField] private bool showInfoOnHover = true;

        [SerializeField] private string abilityName;
        [TextArea(2, 4)]
        [SerializeField] private string abilityDescription;
        [SerializeField] private string treeTitle;
        [SerializeField] private int cost = 1;
        [SerializeField] private Sprite iconSprite;

        [SerializeField] private float hoverScale = 1.15f;
        [SerializeField] private float transitionSpeed = 10f;

        private bool _isHovered;
        private float _currentScale = 1f;
        private float _currentGlowAlpha;
        private Color _glowBaseColor;
        private Color _borderBaseColor;

        private void Awake()
        {
            _currentScale = 1f;

            if (hoverGlowImage != null)
            {
                _glowBaseColor = hoverGlowImage.color;
                SetImageAlpha(hoverGlowImage, _glowBaseColor, 0f);
            }
            if (hoverBorderImage != null)
            {
                _borderBaseColor = hoverBorderImage.color;
                SetImageAlpha(hoverBorderImage, _borderBaseColor, 0f);
            }
        }

        private void Update()
        {
            float targetScale = _isHovered ? hoverScale : 1f;
            _currentScale = Mathf.Lerp(_currentScale, targetScale, Time.deltaTime * transitionSpeed);
            if (visualRoot != null)
            {
                visualRoot.localScale = Vector3.one * _currentScale;
            }

            float targetGlow = _isHovered ? 1f : 0f;
            _currentGlowAlpha = Mathf.Lerp(_currentGlowAlpha, targetGlow, Time.deltaTime * transitionSpeed);
            SetImageAlpha(hoverGlowImage, _glowBaseColor, _currentGlowAlpha);
            SetImageAlpha(hoverBorderImage, _borderBaseColor, _currentGlowAlpha);
        }

        private static void SetImageAlpha(Image image, Color baseColor, float alphaMultiplier)
        {
            if (image == null)
            {
                return;
            }
            image.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alphaMultiplier);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered = true;
            UIAudioPlayer.PlayHover();
            if (showInfoOnHover)
            {
                infoPanel?.Show(abilityName, abilityDescription, treeTitle, cost, iconSprite);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;
            if (showInfoOnHover)
            {
                infoPanel?.Hide();
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            UIAudioPlayer.PlayClick();
            infoPanel?.Show(abilityName, abilityDescription, treeTitle, cost, iconSprite);
        }
    }
}
