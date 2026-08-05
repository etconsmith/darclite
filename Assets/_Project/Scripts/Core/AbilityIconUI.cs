using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Darclite.Core
{
    // A draggable, selectable ability icon used in the Abilities page's category boxes (and,
    // once dragged there, in a hotbar slot too — it's the same GameObject that relocates, not a
    // copy). Reuses AbilityNodeUI's hover glow/enlarge look, but adds a persistent selection
    // state (routed through AbilitiesPageUI.Instance, since only one icon is selected at a time
    // across the whole page) and drag-and-drop into AbilityHotbarSlotUI targets.
    [AddComponentMenu("Darclite/Ability Icon UI")]
    public class AbilityIconUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        // The whole draggable unit (background + icon + glow + border) — reparented and
        // scaled as one piece when equipping/unequipping/dragging.
        [SerializeField] private RectTransform nodeRoot;
        // The part that visually enlarges on hover — a child of nodeRoot, so its scale composes
        // with nodeRoot's own (e.g. equipped) scale rather than overriding it.
        [SerializeField] private RectTransform visualRoot;
        [SerializeField] private Image hoverGlowImage;
        [SerializeField] private Image hoverBorderImage;

        [SerializeField] private string abilityName;
        [TextArea(2, 4)]
        [SerializeField] private string abilityDescription;
        [SerializeField] private string treeTitle;
        [SerializeField] private int cost = 1;
        [SerializeField] private Sprite iconSprite;

        [SerializeField] private float hoverScale = 1.15f;
        [SerializeField] private float transitionSpeed = 10f;
        [SerializeField] private float equippedScale = 2.6f;

        public string AbilityName => abilityName;
        public string AbilityDescription => abilityDescription;
        public string TreeTitle => treeTitle;
        public int Cost => cost;
        public Sprite IconSprite => iconSprite;
        public RectTransform NodeRoot => nodeRoot;
        public float EquippedScale => equippedScale;

        // Where this icon lives when not equipped in a hotbar slot — captured once at Awake,
        // since its category-box position never changes (unequipping always returns it here).
        // Category boxes use top-anchoring (icons sit at a fixed offset from the box's top edge)
        // while a slot uses center-anchoring (the icon centers in the square regardless of its
        // scale), so the anchors/pivot have to be restored too, not just the position.
        public Transform HomeParent { get; private set; }
        public Vector2 HomeAnchorMin { get; private set; }
        public Vector2 HomeAnchorMax { get; private set; }
        public Vector2 HomePivot { get; private set; }
        public Vector2 HomeAnchoredPosition { get; private set; }
        public AbilityHotbarSlotUI CurrentSlot { get; set; }

        private Image _background;
        private bool _isHovered;
        private bool _isSelected;
        private bool _handledByDrop;
        private float _currentScale = 1f;
        private float _currentGlowAlpha;
        private Color _glowBaseColor;
        private Color _borderBaseColor;

        private void Awake()
        {
            _background = GetComponent<Image>();
        }

        // nodeRoot/hoverGlowImage/hoverBorderImage are wired by StatMenuBootstrapper via
        // SerializedObject right after AddComponent<AbilityIconUI>() — but Unity calls Awake()
        // synchronously as part of AddComponent() itself, before that wiring happens, so reading
        // them there always sees null. Start() runs later (editor tooling finishes wiring fields
        // long before Play mode ever reaches Start()), so it's safe here.
        private void Start()
        {
            HomeParent = nodeRoot.parent;
            HomeAnchorMin = nodeRoot.anchorMin;
            HomeAnchorMax = nodeRoot.anchorMax;
            HomePivot = nodeRoot.pivot;
            HomeAnchoredPosition = nodeRoot.anchoredPosition;

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

            // Selection shares the hover glow/border rather than needing its own separate
            // visual — a selected icon stays lit even when the mouse isn't over it.
            float targetGlow = (_isHovered || _isSelected) ? 1f : 0f;
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

        public void SetSelected(bool selected)
        {
            _isSelected = selected;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered = true;
            UIAudioPlayer.PlayHover();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            UIAudioPlayer.PlayClick();
            AbilitiesPageUI.Instance?.SelectIcon(this);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // Toggle abilities (Power Sense 1, Attack Sensing I, ...) can't be equipped at all —
            // they're switched on/off exclusively via the toggle button on their info panel, so
            // simply never start a drag for them. Bailing here (rather than in OnDrag/OnEndDrag)
            // means nodeRoot never reparents or moves, so the icon just doesn't budge at all.
            if (AbilityLoadout.IsToggleAbility(abilityName))
            {
                return;
            }

            _handledByDrop = false;

            if (_background != null)
            {
                // Let raycasts pass through to whatever's underneath (a slot, a box) instead of
                // continuing to hit the icon being carried.
                _background.raycastTarget = false;
            }

            RectTransform dragLayer = AbilitiesPageUI.Instance != null ? AbilitiesPageUI.Instance.DragLayer : null;
            if (dragLayer != null)
            {
                nodeRoot.SetParent(dragLayer, true);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (AbilityLoadout.IsToggleAbility(abilityName))
            {
                return;
            }

            // Screen Space Overlay canvas — screen point and world point coincide.
            nodeRoot.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (AbilityLoadout.IsToggleAbility(abilityName))
            {
                return;
            }

            if (_background != null)
            {
                _background.raycastTarget = true;
            }

            if (!_handledByDrop)
            {
                AbilitiesPageUI.Instance?.HandleDrop(this, null);
            }
        }

        // Called by AbilityHotbarSlotUI.OnDrop, which fires before OnEndDrag — marks this drag as
        // already resolved so OnEndDrag's fallback doesn't also treat it as an invalid drop.
        public void NotifyHandledByDrop()
        {
            _handledByDrop = true;
        }
    }
}
