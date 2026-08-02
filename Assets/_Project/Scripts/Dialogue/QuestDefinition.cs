using UnityEngine;

namespace Darclite.Dialogue
{
    // Pure data — the LLM only ever decides *when* to bring this up, never what's in it. That way
    // a persona's system prompt can reference a quest by name/description without ever being the
    // source of truth for the reward or objective the player actually receives.
    [CreateAssetMenu(fileName = "NewQuest", menuName = "Darclite/Quest")]
    public class QuestDefinition : ScriptableObject
    {
        [Tooltip("Stable unique id — used to track acceptance, not shown to the player.")]
        [SerializeField] private string questId = "new_quest";

        [SerializeField] private string title = "New Quest";

        [TextArea(2, 5)]
        [SerializeField] private string description = "";

        [TextArea(1, 3)]
        [SerializeField] private string objective = "";

        [TextArea(1, 3)]
        [SerializeField] private string rewardDescription = "";

        [Tooltip("How many of whatever this quest counts (bandits defeated, items collected, etc.) are needed to complete it.")]
        [SerializeField] private int targetProgress = 1;

        [Tooltip("Noun phrase for the progress readout, e.g. 'bandits defeated' or 'herbs collected' — different quests track different things.")]
        [SerializeField] private string progressLabel = "completed";

        [Tooltip("Display name of the NPC to return to once the objective is complete — shown in the tracker as 'Return to X'.")]
        [SerializeField] private string questGiverName = "";

        public string QuestId => questId;
        public string Title => title;
        public string Description => description;
        public string Objective => objective;
        public string RewardDescription => rewardDescription;
        public int TargetProgress => targetProgress;
        public string ProgressLabel => progressLabel;
        public string QuestGiverName => questGiverName;
    }
}
