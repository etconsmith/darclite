using Darclite.Combat;
using UnityEngine;

namespace Darclite.Dialogue
{
    // Wires the Bandit Beater quest to an actual gameplay encounter: spawns a cluster of bandit
    // prefab instances when the quest is accepted, and reports one point of progress to QuestLog
    // each time one of them dies. Deliberately quest-specific rather than a generic "encounter"
    // system — there's only one quest type that needs this right now, and a shared abstraction
    // would just be guessing at what future quest types actually need.
    [AddComponentMenu("Darclite/Bandit Quest Spawner")]
    public class BanditQuestSpawner : MonoBehaviour
    {
        [SerializeField] private QuestDefinition quest;
        [SerializeField] private GameObject banditPrefab;
        [SerializeField] private Transform spawnAreaCenter;
        [SerializeField] private float spawnRadius = 4f;
        [SerializeField] private int banditCount = 5;

        private bool _hasSpawned;

        private void OnEnable()
        {
            if (QuestLog.Instance != null)
            {
                QuestLog.Instance.QuestAccepted += OnQuestAccepted;
            }
        }

        private void OnDisable()
        {
            if (QuestLog.Instance != null)
            {
                QuestLog.Instance.QuestAccepted -= OnQuestAccepted;
            }
        }

        private void OnQuestAccepted(QuestDefinition acceptedQuest)
        {
            if (acceptedQuest != quest || _hasSpawned)
            {
                return;
            }

            _hasSpawned = true;
            SpawnBandits();
        }

        private void SpawnBandits()
        {
            if (banditPrefab == null || spawnAreaCenter == null)
            {
                Debug.LogWarning("[BanditQuestSpawner] Missing bandit prefab or spawn area — can't spawn the encounter.");
                return;
            }

            for (int i = 0; i < banditCount; i++)
            {
                Vector2 offset = Random.insideUnitCircle * spawnRadius;
                Vector3 spawnPosition = spawnAreaCenter.position + new Vector3(offset.x, 0f, offset.y);
                GameObject bandit = Instantiate(banditPrefab, spawnPosition, Quaternion.identity);

                Combatant combatant = bandit.GetComponent<Combatant>();
                if (combatant != null)
                {
                    combatant.OnDeath += HandleBanditDeath;
                }
            }
        }

        private void HandleBanditDeath()
        {
            QuestLog.Instance?.AddProgress(quest, 1);
        }
    }
}
