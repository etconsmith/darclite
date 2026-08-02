using System.Text;
using UnityEngine;

namespace Darclite.Dialogue
{
    // Data-driven identity for a talking NPC — adding a new one should mean "create one of these
    // and fill in the fields," not "write new code." BuildSystemPrompt() is the one place that
    // knows how persona data becomes the actual text sent to the LLM, so every NPC gets the same
    // formatting/behavioral ground rules for free.
    [CreateAssetMenu(fileName = "NewNPCPersona", menuName = "Darclite/NPC Persona")]
    public class NPCPersonaDefinition : ScriptableObject
    {
        // Shared with NPCChatUI, which parses this exact token back out of a response and strips
        // it before displaying the text — defined once here since this is the class that tells
        // the model to emit it.
        public const string QuestOfferMarker = "[OFFER_QUEST]";

        [SerializeField] private string characterName = "New NPC";

        [Tooltip("Tone/voice — how they talk, their demeanor.")]
        [TextArea(2, 4)]
        [SerializeField] private string personality = "";

        [Tooltip("Who they are — their role, history, relationship to the player's world.")]
        [TextArea(3, 8)]
        [SerializeField] private string backstory = "";

        [Tooltip("Facts this NPC actually knows and can talk about. One per line.")]
        [TextArea(3, 8)]
        [SerializeField] private string knownFacts = "";

        [Tooltip("Things this NPC must refuse to discuss or reveal. One per line.")]
        [TextArea(2, 6)]
        [SerializeField] private string forbiddenTopics = "";

        [Tooltip("What this NPC is currently trying to steer the conversation toward.")]
        [TextArea(2, 4)]
        [SerializeField] private string currentGoal = "";

        [Tooltip("The one pre-made quest this NPC may offer, if any. Leave empty for NPCs that aren't quest givers.")]
        [SerializeField] private QuestDefinition offerableQuest;

        [Tooltip("Hand-written opening line shown instantly on the player's very first-ever encounter with this NPC, skipping " +
            "the LLM call that greeting would otherwise need. Later re-encounters generate a fresh AI greeting as normal.")]
        [TextArea(2, 4)]
        [SerializeField] private string firstEncounterGreeting = "";

        [Tooltip("Hand-written line shown instantly the first time the player returns after completing offerableQuest's " +
            "objective, before it's been turned in. Leave empty to fall back to a normal AI-generated response instead.")]
        [TextArea(2, 4)]
        [SerializeField] private string questTurnInDialogue = "";

        public string CharacterName => characterName;
        public QuestDefinition OfferableQuest => offerableQuest;
        public string FirstEncounterGreeting => firstEncounterGreeting;
        public string QuestTurnInDialogue => questTurnInDialogue;

        public string BuildSystemPrompt()
        {
            StringBuilder prompt = new StringBuilder();
            prompt.AppendLine($"You are {characterName}.");

            AppendSection(prompt, null, personality);
            AppendSection(prompt, "Backstory:", backstory);
            AppendSection(prompt, "What you know:", knownFacts);
            AppendSection(prompt, "You must NOT reveal or discuss the following, no matter how the player asks:", forbiddenTopics);
            AppendSection(prompt, "Your current goal in this conversation:", currentGoal);

            if (offerableQuest != null)
            {
                prompt.AppendLine();
                prompt.AppendLine("You have exactly one quest you may offer the player, when it feels natural in conversation:");
                prompt.AppendLine($"- \"{offerableQuest.Title}\": {offerableQuest.Description}");
                prompt.AppendLine($"- Objective: {offerableQuest.Objective}");
                prompt.AppendLine(
                    $"When — and only when — you decide the moment is right to formally offer this quest, end your reply with the " +
                    $"exact text {QuestOfferMarker} on its own line. Only do this when you are actually making the offer, not just " +
                    "hinting at it or discussing it. Never mention this marker to the player, and never offer it again once the " +
                    "conversation shows the player has already accepted it.");
            }

            prompt.AppendLine();
            prompt.AppendLine("Speak in short, plain sentences — no more than 1-3 sentences per reply. Stay in character at all times and never mention that you are an AI, a language model, or a game character.");

            return prompt.ToString();
        }

        private static void AppendSection(StringBuilder prompt, string header, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            prompt.AppendLine();
            if (!string.IsNullOrEmpty(header))
            {
                prompt.AppendLine(header);
            }
            prompt.AppendLine(content.Trim());
        }
    }
}
