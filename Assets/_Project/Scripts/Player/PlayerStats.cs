using System;
using UnityEngine;

namespace Darclite.Player
{
    public enum StatCategory
    {
        Lite,
        Strength,
        Vitality,
        Dexterity
    }

    // Snapshot of an XP grant, before and after leveling is resolved — the XP bar UI replays this
    // exact transition (via PlayerStats.GetXpRequiredForLevel, the same formula used below) to
    // animate every level-up crossed in a single grant, one after another.
    public readonly struct XpGainInfo
    {
        public readonly int startLevel;
        public readonly int startXp;
        public readonly int amountGained;
        public readonly int endLevel;
        public readonly int endXp;

        public XpGainInfo(int startLevel, int startXp, int amountGained, int endLevel, int endXp)
        {
            this.startLevel = startLevel;
            this.startXp = startXp;
            this.amountGained = amountGained;
            this.endLevel = endLevel;
            this.endXp = endXp;
        }
    }

    // Minimal data model for the stat allocation screen. Earning ability points (from quests, XP,
    // and leveling up) isn't built yet — this just holds the numbers and the spend mechanism so
    // the menu has real state to display and interact with ahead of that system existing.
    [AddComponentMenu("Darclite/Player Stats")]
    public class PlayerStats : MonoBehaviour
    {
        // Lets systems that don't already hold a reference to the player (e.g. an enemy granting
        // XP on death) reach this without a scene search, the same pattern as QuestLog.Instance.
        public static PlayerStats Instance { get; private set; }

        [SerializeField] private int availableAbilityPoints;
        [SerializeField] private int litePoints;
        [SerializeField] private int strengthPoints;
        [SerializeField] private int vitalityPoints;
        [SerializeField] private int dexterityPoints;

        [SerializeField] private int level = 1;
        [SerializeField] private int currentXp;

        public int AvailableAbilityPoints => availableAbilityPoints;
        public int Level => level;
        public int CurrentXp => currentXp;
        public int XpToNextLevel => GetXpRequiredForLevel(level);

        public event Action AbilityPointsChanged;
        public event Action<StatCategory, int> StatPointsChanged;
        public event Action<XpGainInfo> XpGained;

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

        // Level 1->2 costs 100 XP; every pair of levels after that costs 5 more than the previous
        // pair (2->3 and 3->4 cost 105 and 110, 4->5 and 5->6 cost 120 and 130, and so on), so the
        // curve ramps up in noticeable steps rather than a smooth continuous exponential.
        public static int GetXpRequiredForLevel(int level)
        {
            int required = 100;
            for (int i = 2; i <= level; i++)
            {
                required += 5 * (i / 2);
            }
            return required;
        }

        public void GrantXp(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            int startLevel = level;
            int startXp = currentXp;

            currentXp += amount;
            while (currentXp >= GetXpRequiredForLevel(level))
            {
                currentXp -= GetXpRequiredForLevel(level);
                level++;
            }

            XpGained?.Invoke(new XpGainInfo(startLevel, startXp, amount, level, currentXp));
        }

        public int GetInvestedPoints(StatCategory category)
        {
            switch (category)
            {
                case StatCategory.Lite: return litePoints;
                case StatCategory.Strength: return strengthPoints;
                case StatCategory.Vitality: return vitalityPoints;
                case StatCategory.Dexterity: return dexterityPoints;
                default: return 0;
            }
        }

        public void GrantAbilityPoints(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            availableAbilityPoints += amount;
            AbilityPointsChanged?.Invoke();
        }

        public bool TrySpendPoint(StatCategory category)
        {
            if (availableAbilityPoints <= 0)
            {
                return false;
            }

            availableAbilityPoints--;

            switch (category)
            {
                case StatCategory.Lite:
                    litePoints++;
                    break;
                case StatCategory.Strength:
                    strengthPoints++;
                    break;
                case StatCategory.Vitality:
                    vitalityPoints++;
                    break;
                case StatCategory.Dexterity:
                    dexterityPoints++;
                    break;
            }

            AbilityPointsChanged?.Invoke();
            StatPointsChanged?.Invoke(category, GetInvestedPoints(category));
            return true;
        }
    }
}
