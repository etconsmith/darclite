using System.Collections;
using Darclite.CameraSystem;
using Darclite.Combat;
using Darclite.Dialogue;
using Darclite.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Darclite.Core
{
    // Drives the Stats and Lite pages — Strength/Vitality/Dexterity tabs are built but stay
    // non-interactive until those pages have real content.
    [AddComponentMenu("Darclite/Stat Menu UI")]
    public class StatMenuUI : MonoBehaviour
    {
        public static StatMenuUI Instance { get; private set; }

        [System.Serializable]
        private class StatRowRefs
        {
            public Text pointsText;
            public Button plusButton;
        }

        [System.Serializable]
        private class TabRefs
        {
            public Text tabText;
            public GameObject underline;
            public GameObject pageContent;

            [System.NonSerialized] public CanvasGroup canvasGroup;
            [System.NonSerialized] public RectTransform rect;
        }

        private static readonly Color ActiveTabColor = Color.white;
        private static readonly Color InactiveTabColor = new Color(0.5f, 0.52f, 0.58f, 0.8f);

        // Page-switch transition — a short slide+fade exit for the outgoing page overlapping a
        // slightly longer slide+fade+overshoot entrance for the incoming one, so the new page
        // "pops" into place rather than just cross-fading flatly.
        private const float PageExitDuration = 0.14f;
        private const float PageEnterDuration = 0.26f;
        private const float PageSlideDistance = 120f;

        // Open snaps in with a small back-ease overshoot (feels like the menu "arrives" with a bit
        // of weight); close is a quick, plain ease-in so dismissing never feels laggy.
        private const float OpenDuration = 0.32f;
        private const float CloseDuration = 0.18f;
        private const float PanelOpenStartScale = 0.85f;

        [SerializeField] private GameObject panelRoot;
        [SerializeField] private PlayerStats playerStats;
        [SerializeField] private Combatant playerCombatant;
        [SerializeField] private Volume blurVolume;
        [SerializeField] private Text abilityPointsNumberText;
        [SerializeField] private Text hpText;
        [SerializeField] private Text xpLevelText;
        [SerializeField] private Image xpFillImage;
        [SerializeField] private Text xpFractionText;

        [SerializeField] private StatRowRefs liteRow;
        [SerializeField] private StatRowRefs strengthRow;
        [SerializeField] private StatRowRefs vitalityRow;
        [SerializeField] private StatRowRefs dexterityRow;

        [SerializeField] private Button statsTabButton;
        [SerializeField] private Button liteTabButton;
        [SerializeField] private Button abilitiesTabButton;
        [SerializeField] private TabRefs statsTab;
        [SerializeField] private TabRefs liteTab;
        [SerializeField] private TabRefs abilitiesTab;
        [SerializeField] private Text liteAvailablePointsText;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private RectTransform _panelRect;
        private TabRefs[] _tabOrder;
        private TabRefs _currentTab;
        private Coroutine _tabTransitionRoutine;
        private Coroutine _openCloseRoutine;

        private void Awake()
        {
            Instance = this;

            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
                _panelRect = panelRoot.GetComponent<RectTransform>();
            }

            CacheTabRuntimeRefs(statsTab);
            CacheTabRuntimeRefs(liteTab);
            CacheTabRuntimeRefs(abilitiesTab);
            _tabOrder = new[] { statsTab, liteTab, abilitiesTab };

            WireButton(liteRow, StatCategory.Lite);
            WireButton(strengthRow, StatCategory.Strength);
            WireButton(vitalityRow, StatCategory.Vitality);
            WireButton(dexterityRow, StatCategory.Dexterity);

            if (statsTabButton != null)
            {
                statsTabButton.onClick.AddListener(() =>
                {
                    UIAudioPlayer.PlayClick();
                    ShowStatsPage();
                });
            }
            if (liteTabButton != null)
            {
                liteTabButton.onClick.AddListener(() =>
                {
                    UIAudioPlayer.PlayClick();
                    ShowLitePage();
                });
            }
            if (abilitiesTabButton != null)
            {
                abilitiesTabButton.onClick.AddListener(() =>
                {
                    UIAudioPlayer.PlayClick();
                    ShowAbilitiesPage();
                });
            }

            if (playerStats != null)
            {
                playerStats.AbilityPointsChanged += RefreshAbilityPoints;
                playerStats.StatPointsChanged += OnStatPointsChanged;
                playerStats.XpGained += OnXpGained;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (playerStats != null)
            {
                playerStats.AbilityPointsChanged -= RefreshAbilityPoints;
                playerStats.StatPointsChanged -= OnStatPointsChanged;
                playerStats.XpGained -= OnXpGained;
            }
        }

        private void Update()
        {
            if (Keyboard.current == null || !Keyboard.current.qKey.wasPressedThisFrame)
            {
                return;
            }

            // Q is a raw keyboard read, not routed through UI focus — without this guard, typing
            // a message containing "q" into the NPC chat box would also toggle this menu.
            if (NPCChatUI.IsTypingInInput)
            {
                return;
            }
            if (NPCChatUI.Instance != null && NPCChatUI.Instance.IsOpen)
            {
                return;
            }

            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void Open()
        {
            if (IsOpen)
            {
                return;
            }

            if (panelRoot != null)
            {
                panelRoot.SetActive(true);
            }

            ThirdPersonOrbitCamera.CursorUnlockRequested = true;

            ShowStatsPage();

            RefreshAbilityPoints();
            RefreshStatRow(StatCategory.Lite);
            RefreshStatRow(StatCategory.Strength);
            RefreshStatRow(StatCategory.Vitality);
            RefreshStatRow(StatCategory.Dexterity);
            RefreshHp();
            RefreshXp();

            if (_openCloseRoutine != null)
            {
                StopCoroutine(_openCloseRoutine);
            }
            _openCloseRoutine = StartCoroutine(AnimateOpenClose(opening: true));
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            ThirdPersonOrbitCamera.CursorUnlockRequested = false;

            if (_tabTransitionRoutine != null)
            {
                StopCoroutine(_tabTransitionRoutine);
                _tabTransitionRoutine = null;
                SnapAllTabsInstant(_currentTab);
            }

            if (_openCloseRoutine != null)
            {
                StopCoroutine(_openCloseRoutine);
            }
            _openCloseRoutine = StartCoroutine(AnimateOpenClose(opening: false));
        }

        // Deliberately scale/blur only, never a CanvasGroup fade on panelRoot itself — the header
        // and tab bar are direct children of panelRoot, so a whole-panel CanvasGroup would fade
        // (and, mid-animation, un-interact) them along with everything else. The header stays
        // fully opaque and clickable at all times; only the currently visible page's own
        // CanvasGroup fades, which never ancestors the header.
        private IEnumerator AnimateOpenClose(bool opening)
        {
            float duration = opening ? OpenDuration : CloseDuration;
            float targetScale = opening ? 1f : PanelOpenStartScale;
            float targetBlurWeight = opening ? 1f : 0f;

            float startScale = _panelRect != null ? _panelRect.localScale.x : targetScale;
            float startBlurWeight = blurVolume != null ? blurVolume.weight : targetBlurWeight;

            // Only fade page content out on close — opening's fade-in is already handled by the
            // ShowStatsPage() tab-switch triggered just before this coroutine starts.
            CanvasGroup contentGroup = !opening ? _currentTab?.canvasGroup : null;
            float contentStartAlpha = contentGroup != null ? contentGroup.alpha : 1f;

            if (opening)
            {
                startScale = PanelOpenStartScale;
                if (_panelRect != null) _panelRect.localScale = Vector3.one * startScale;
            }

            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / duration;
                float clamped = Mathf.Clamp01(t);
                // Ease-out-back pop on open (the menu "arrives" with a touch of overshoot);
                // plain ease-in on close for a quick, clean dismiss.
                float scaleEase = opening ? EaseOutBack(clamped) : clamped * clamped;

                if (_panelRect != null)
                {
                    _panelRect.localScale = Vector3.one * Mathf.LerpUnclamped(startScale, targetScale, scaleEase);
                }
                if (blurVolume != null)
                {
                    blurVolume.weight = Mathf.Lerp(startBlurWeight, targetBlurWeight, clamped);
                }
                if (contentGroup != null)
                {
                    contentGroup.alpha = Mathf.Lerp(contentStartAlpha, 0f, clamped);
                }

                yield return null;
            }

            if (_panelRect != null) _panelRect.localScale = Vector3.one * targetScale;
            if (blurVolume != null) blurVolume.weight = targetBlurWeight;

            if (!opening)
            {
                // Reset to fully visible before actually hiding the panel — otherwise reopening on
                // the same tab (which skips ActivateTab's own fade-in, since nothing changed) would
                // stay invisible from this close.
                if (contentGroup != null) contentGroup.alpha = 1f;
                if (panelRoot != null) panelRoot.SetActive(false);
            }

            _openCloseRoutine = null;
        }

        public void ShowStatsPage()
        {
            ActivateTab(statsTab);
        }

        public void ShowLitePage()
        {
            ActivateTab(liteTab);
            RefreshLiteAvailablePoints();
        }

        public void ShowAbilitiesPage()
        {
            ActivateTab(abilitiesTab);
        }

        // Stats/Lite/Abilities are mutually exclusive — activating one deactivates the other two.
        private void ActivateTab(TabRefs target)
        {
            if (target == null || target == _currentTab)
            {
                return;
            }

            if (_tabTransitionRoutine != null)
            {
                StopCoroutine(_tabTransitionRoutine);
                _tabTransitionRoutine = null;
                // A rapid second click cancels the in-flight transition — snap everything to the
                // state it was heading away from before starting the new one, so there's always a
                // clean baseline to animate from instead of stacking half-finished transitions.
                SnapAllTabsInstant(_currentTab);
            }

            UIAudioPlayer.PlayFlip();

            TabRefs previous = _currentTab;
            _currentTab = target;
            _tabTransitionRoutine = StartCoroutine(AnimateTabSwitch(previous, target));
        }

        private IEnumerator AnimateTabSwitch(TabRefs from, TabRefs to)
        {
            int fromIndex = from != null ? System.Array.IndexOf(_tabOrder, from) : -1;
            int toIndex = System.Array.IndexOf(_tabOrder, to);
            float direction = (fromIndex >= 0 && toIndex >= 0 && toIndex < fromIndex) ? -1f : 1f;

            SetTabHeaderState(to, true);
            if (from != null)
            {
                SetTabHeaderState(from, false);
            }

            if (to.pageContent != null)
            {
                to.pageContent.SetActive(true);
            }
            if (to.rect != null)
            {
                to.rect.anchoredPosition = new Vector2(PageSlideDistance * direction, 0f);
            }
            if (to.canvasGroup != null)
            {
                to.canvasGroup.alpha = 0f;
            }

            float exitDuration = from != null ? PageExitDuration : 0f;
            float exitElapsed = 0f;
            float enterElapsed = 0f;

            while (enterElapsed < PageEnterDuration || exitElapsed < exitDuration)
            {
                float dt = Time.unscaledDeltaTime;
                exitElapsed += dt;
                enterElapsed += dt;

                if (from != null && exitDuration > 0f)
                {
                    float exitT = Mathf.Clamp01(exitElapsed / exitDuration);
                    float exitEase = exitT * exitT;
                    if (from.canvasGroup != null) from.canvasGroup.alpha = 1f - exitEase;
                    if (from.rect != null) from.rect.anchoredPosition = new Vector2(-PageSlideDistance * direction * exitEase, 0f);
                }

                float enterT = Mathf.Clamp01(enterElapsed / PageEnterDuration);
                float enterEase = EaseOutBack(enterT);
                if (to.canvasGroup != null) to.canvasGroup.alpha = Mathf.Clamp01(enterT * 1.4f);
                if (to.rect != null) to.rect.anchoredPosition = Vector2.LerpUnclamped(new Vector2(PageSlideDistance * direction, 0f), Vector2.zero, enterEase);

                yield return null;
            }

            if (from != null)
            {
                if (from.pageContent != null) from.pageContent.SetActive(false);
                if (from.rect != null) from.rect.anchoredPosition = Vector2.zero;
                if (from.canvasGroup != null) from.canvasGroup.alpha = 0f;
            }

            if (to.rect != null) to.rect.anchoredPosition = Vector2.zero;
            if (to.canvasGroup != null) to.canvasGroup.alpha = 1f;

            _tabTransitionRoutine = null;
        }

        private void SnapAllTabsInstant(TabRefs active)
        {
            SnapTabInstant(statsTab, statsTab == active);
            SnapTabInstant(liteTab, liteTab == active);
            SnapTabInstant(abilitiesTab, abilitiesTab == active);
        }

        private static void SnapTabInstant(TabRefs tab, bool active)
        {
            if (tab == null)
            {
                return;
            }
            if (tab.pageContent != null) tab.pageContent.SetActive(active);
            if (tab.canvasGroup != null) tab.canvasGroup.alpha = active ? 1f : 0f;
            if (tab.rect != null) tab.rect.anchoredPosition = Vector2.zero;
            SetTabHeaderState(tab, active);
        }

        private static void SetTabHeaderState(TabRefs tab, bool active)
        {
            if (tab?.tabText != null)
            {
                tab.tabText.color = active ? ActiveTabColor : InactiveTabColor;
            }
            if (tab?.underline != null)
            {
                tab.underline.SetActive(active);
            }
        }

        private static void CacheTabRuntimeRefs(TabRefs tab)
        {
            if (tab?.pageContent == null)
            {
                return;
            }
            tab.canvasGroup = EnsureCanvasGroup(tab.pageContent);
            tab.rect = tab.pageContent.GetComponent<RectTransform>();
        }

        private static CanvasGroup EnsureCanvasGroup(GameObject go)
        {
            if (go == null)
            {
                return null;
            }
            CanvasGroup group = go.GetComponent<CanvasGroup>();
            return group != null ? group : go.AddComponent<CanvasGroup>();
        }

        // Classic overshoot-then-settle easing (Robert Penner's easeOutBack) — gives page/panel
        // entrances a small satisfying "pop" instead of arriving dead flat.
        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        private void WireButton(StatRowRefs row, StatCategory category)
        {
            if (row?.plusButton != null)
            {
                row.plusButton.onClick.AddListener(() => OnPlusClicked(category));
            }
        }

        private void OnPlusClicked(StatCategory category)
        {
            UIAudioPlayer.PlayClick();
            playerStats?.TrySpendPoint(category);
        }

        private void OnStatPointsChanged(StatCategory category, int newValue)
        {
            RefreshStatRow(category);

            if (category == StatCategory.Lite)
            {
                RefreshLiteAvailablePoints();
            }
        }

        private void RefreshAbilityPoints()
        {
            if (abilityPointsNumberText != null && playerStats != null)
            {
                abilityPointsNumberText.text = playerStats.AvailableAbilityPoints.ToString();
            }
        }

        private void RefreshStatRow(StatCategory category)
        {
            StatRowRefs row = GetRow(category);
            if (row?.pointsText != null && playerStats != null)
            {
                row.pointsText.text = playerStats.GetInvestedPoints(category).ToString();
            }
        }

        private void RefreshLiteAvailablePoints()
        {
            if (liteAvailablePointsText != null && playerStats != null)
            {
                liteAvailablePointsText.text = playerStats.GetInvestedPoints(StatCategory.Lite).ToString();
            }
        }

        private void RefreshHp()
        {
            if (hpText != null && playerCombatant != null)
            {
                hpText.text = $"HP  {playerCombatant.CurrentHealth}/{playerCombatant.MaxHealth}";
            }
        }

        // No animation here (that's the top-of-screen XpBarUI's job) — this just always reflects
        // the current level/XP whenever the Stats page is refreshed.
        private void OnXpGained(XpGainInfo info)
        {
            RefreshXp();
        }

        private void RefreshXp()
        {
            if (playerStats == null)
            {
                return;
            }

            int required = PlayerStats.GetXpRequiredForLevel(playerStats.Level);

            if (xpLevelText != null)
            {
                xpLevelText.text = playerStats.Level.ToString();
            }
            if (xpFillImage != null)
            {
                xpFillImage.fillAmount = required > 0 ? (float)playerStats.CurrentXp / required : 0f;
            }
            if (xpFractionText != null)
            {
                xpFractionText.text = $"{playerStats.CurrentXp}/{required}";
            }
        }

        private StatRowRefs GetRow(StatCategory category)
        {
            switch (category)
            {
                case StatCategory.Lite: return liteRow;
                case StatCategory.Strength: return strengthRow;
                case StatCategory.Vitality: return vitalityRow;
                case StatCategory.Dexterity: return dexterityRow;
                default: return null;
            }
        }
    }
}
