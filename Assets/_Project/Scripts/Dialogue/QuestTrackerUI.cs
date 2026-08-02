using UnityEngine;
using UnityEngine.UI;

namespace Darclite.Dialogue
{
    // Single-quest tracker for now — the most recently accepted quest takes over the display.
    // A list-based UI for multiple simultaneously tracked quests isn't built since there's only
    // one quest in the game to track at the moment.
    [AddComponentMenu("Darclite/Quest Tracker UI")]
    public class QuestTrackerUI : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text titleText;
        [SerializeField] private Text objectiveText;
        [SerializeField] private Text progressText;
        [SerializeField] private Image progressFillImage;

        private QuestDefinition _trackedQuest;

        private void Awake()
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (QuestLog.Instance != null)
            {
                QuestLog.Instance.QuestAccepted += OnQuestAccepted;
                QuestLog.Instance.QuestProgressChanged += OnQuestProgressChanged;
                QuestLog.Instance.QuestCompleted += OnQuestCompleted;
                QuestLog.Instance.QuestTurnedIn += OnQuestTurnedIn;
            }
        }

        private void OnDisable()
        {
            if (QuestLog.Instance != null)
            {
                QuestLog.Instance.QuestAccepted -= OnQuestAccepted;
                QuestLog.Instance.QuestProgressChanged -= OnQuestProgressChanged;
                QuestLog.Instance.QuestCompleted -= OnQuestCompleted;
                QuestLog.Instance.QuestTurnedIn -= OnQuestTurnedIn;
            }
        }

        private void OnQuestAccepted(QuestDefinition quest)
        {
            _trackedQuest = quest;

            if (titleText != null)
            {
                titleText.text = quest.Title;
            }
            if (objectiveText != null)
            {
                objectiveText.text = quest.Objective;
            }

            UpdateProgressDisplay(0, quest.TargetProgress);

            if (panelRoot != null)
            {
                panelRoot.SetActive(true);
            }
        }

        private void OnQuestProgressChanged(QuestDefinition quest, int current, int target)
        {
            if (quest != _trackedQuest)
            {
                return;
            }
            UpdateProgressDisplay(current, target);
        }

        private void OnQuestCompleted(QuestDefinition quest)
        {
            if (quest != _trackedQuest || objectiveText == null)
            {
                return;
            }

            objectiveText.text = !string.IsNullOrEmpty(quest.QuestGiverName)
                ? $"Return to {quest.QuestGiverName}"
                : "Complete!";
        }

        private void OnQuestTurnedIn(QuestDefinition quest)
        {
            if (quest != _trackedQuest)
            {
                return;
            }

            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }
        }

        private void UpdateProgressDisplay(int current, int target)
        {
            if (progressText != null)
            {
                string label = _trackedQuest != null ? _trackedQuest.ProgressLabel : "completed";
                progressText.text = $"{current}/{target} {label}";
            }
            if (progressFillImage != null)
            {
                progressFillImage.fillAmount = target > 0 ? (float)current / target : 0f;
            }
        }
    }
}
