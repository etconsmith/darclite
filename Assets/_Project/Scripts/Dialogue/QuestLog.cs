using System;
using System.Collections.Generic;
using UnityEngine;

namespace Darclite.Dialogue
{
    // Runtime-only for now (resets on a fresh Play session) — persisting accepted quests across
    // sessions is the same kind of save-system work as the rest of the player's progress, which
    // doesn't exist yet anywhere in this project. Revisit once that's built.
    [AddComponentMenu("Darclite/Quest Log")]
    public class QuestLog : MonoBehaviour
    {
        public static QuestLog Instance { get; private set; }

        public event Action<QuestDefinition> QuestAccepted;

        // Deliberately generic — different quests report progress for different reasons (bandits
        // defeated, items collected, a location reached, ...); QuestLog just tracks "how far
        // along," it doesn't care why. Whatever triggers progress for a given quest calls
        // AddProgress directly (see BanditQuestSpawner for the bandit-kill example).
        public event Action<QuestDefinition, int, int> QuestProgressChanged;

        // Fires once the objective itself is done (e.g. all 5 bandits dead) — the quest still
        // isn't over at this point, it just needs to be turned in.
        public event Action<QuestDefinition> QuestCompleted;

        // Fires once the player has actually returned to the quest giver and collected the
        // "thanks" — the real end of the quest. Kept distinct from QuestCompleted so the tracker
        // UI/turn-in dialogue can each react to the specific moment they care about.
        public event Action<QuestDefinition> QuestTurnedIn;

        private readonly HashSet<string> _acceptedQuestIds = new HashSet<string>();
        private readonly HashSet<string> _completedQuestIds = new HashSet<string>();
        private readonly HashSet<string> _turnedInQuestIds = new HashSet<string>();
        private readonly Dictionary<string, int> _progressByQuestId = new Dictionary<string, int>();

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

        public bool HasAccepted(QuestDefinition quest)
        {
            return quest != null && _acceptedQuestIds.Contains(quest.QuestId);
        }

        public bool HasCompleted(QuestDefinition quest)
        {
            return quest != null && _completedQuestIds.Contains(quest.QuestId);
        }

        public bool HasTurnedIn(QuestDefinition quest)
        {
            return quest != null && _turnedInQuestIds.Contains(quest.QuestId);
        }

        public bool IsReadyToTurnIn(QuestDefinition quest)
        {
            return HasCompleted(quest) && !HasTurnedIn(quest);
        }

        public int GetProgress(QuestDefinition quest)
        {
            if (quest == null)
            {
                return 0;
            }
            return _progressByQuestId.TryGetValue(quest.QuestId, out int progress) ? progress : 0;
        }

        public bool AcceptQuest(QuestDefinition quest)
        {
            if (quest == null || _acceptedQuestIds.Contains(quest.QuestId))
            {
                return false;
            }

            _acceptedQuestIds.Add(quest.QuestId);
            _progressByQuestId[quest.QuestId] = 0;
            Debug.Log($"[QuestLog] Accepted quest '{quest.Title}' ({quest.QuestId}).");
            QuestAccepted?.Invoke(quest);
            return true;
        }

        public void AddProgress(QuestDefinition quest, int amount = 1)
        {
            if (quest == null || !_acceptedQuestIds.Contains(quest.QuestId) || _completedQuestIds.Contains(quest.QuestId))
            {
                return;
            }

            int current = Mathf.Min(GetProgress(quest) + amount, quest.TargetProgress);
            _progressByQuestId[quest.QuestId] = current;
            QuestProgressChanged?.Invoke(quest, current, quest.TargetProgress);

            if (current >= quest.TargetProgress)
            {
                _completedQuestIds.Add(quest.QuestId);
                Debug.Log($"[QuestLog] Completed quest '{quest.Title}' ({quest.QuestId}).");
                QuestCompleted?.Invoke(quest);
            }
        }

        public bool TurnInQuest(QuestDefinition quest)
        {
            if (!IsReadyToTurnIn(quest))
            {
                return false;
            }

            _turnedInQuestIds.Add(quest.QuestId);
            Debug.Log($"[QuestLog] Turned in quest '{quest.Title}' ({quest.QuestId}).");
            QuestTurnedIn?.Invoke(quest);
            return true;
        }
    }
}
