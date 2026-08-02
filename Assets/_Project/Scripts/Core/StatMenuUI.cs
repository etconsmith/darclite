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
        }

        private static readonly Color ActiveTabColor = Color.white;
        private static readonly Color InactiveTabColor = new Color(0.5f, 0.52f, 0.58f, 0.8f);

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

        private void Awake()
        {
            Instance = this;

            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }

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

            if (blurVolume != null)
            {
                blurVolume.weight = 1f;
            }

            ShowStatsPage();

            RefreshAbilityPoints();
            RefreshStatRow(StatCategory.Lite);
            RefreshStatRow(StatCategory.Strength);
            RefreshStatRow(StatCategory.Vitality);
            RefreshStatRow(StatCategory.Dexterity);
            RefreshHp();
            RefreshXp();
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }

            ThirdPersonOrbitCamera.CursorUnlockRequested = false;

            if (blurVolume != null)
            {
                blurVolume.weight = 0f;
            }
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
            SetTabState(statsTab, statsTab == target);
            SetTabState(liteTab, liteTab == target);
            SetTabState(abilitiesTab, abilitiesTab == target);
        }

        private static void SetTabState(TabRefs tab, bool active)
        {
            if (tab?.pageContent != null)
            {
                tab.pageContent.SetActive(active);
            }
            if (tab?.tabText != null)
            {
                tab.tabText.color = active ? ActiveTabColor : InactiveTabColor;
            }
            if (tab?.underline != null)
            {
                tab.underline.SetActive(active);
            }
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
