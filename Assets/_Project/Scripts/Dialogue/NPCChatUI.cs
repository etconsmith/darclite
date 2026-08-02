using Darclite.CameraSystem;
using LLMUnity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Darclite.Dialogue
{
    [AddComponentMenu("Darclite/NPC Chat UI")]
    public class NPCChatUI : MonoBehaviour
    {
        public static NPCChatUI Instance { get; private set; }

        [SerializeField] private GameObject panelRoot;
        [SerializeField] private RectTransform panelRect;
        [SerializeField] private Text nameText;
        [SerializeField] private Text dialogueText;
        [SerializeField] private InputField messageInputField;
        [SerializeField] private Button acceptQuestButton;
        [SerializeField] private Text acceptQuestButtonText;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        // Player movement reads this to stop WASD from typing into the world instead of the
        // textbox — kept static so PlayerController doesn't need an Instance null-check.
        public static bool IsTypingInInput { get; private set; }

        // The same left-click that closes the panel (clicking outside it) would otherwise also
        // reach PlayerCombat/etc. that same frame and fire an attack — any click-triggered action
        // should check this before responding to a click on the frame the chat just closed.
        public static bool ConsumedClickThisFrame { get; private set; }

        private NPCInteractable _activeNpc;
        private LLMAgent _activeAgent;
        private QuestDefinition _pendingQuestOffer;
        private bool _isWaitingForResponse;

        private void Awake()
        {
            Instance = this;

            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }

            if (messageInputField != null)
            {
                messageInputField.onEndEdit.AddListener(OnInputFieldEndEdit);
            }

            if (acceptQuestButton != null)
            {
                acceptQuestButton.onClick.AddListener(OnAcceptQuestClicked);
                acceptQuestButton.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (messageInputField != null)
            {
                messageInputField.onEndEdit.RemoveListener(OnInputFieldEndEdit);
            }

            if (acceptQuestButton != null)
            {
                acceptQuestButton.onClick.RemoveListener(OnAcceptQuestClicked);
            }

            IsTypingInInput = false;
        }

        public void OpenChat(NPCInteractable npc)
        {
            if (npc == null || IsOpen)
            {
                return;
            }

            _activeNpc = npc;
            _activeAgent = npc.LlmAgent;
            _activeNpc.SetChatting(true);

            if (nameText != null)
            {
                nameText.text = npc.NpcName;
            }

            if (messageInputField != null)
            {
                messageInputField.text = string.Empty;
            }

            HideQuestOffer();

            if (panelRoot != null)
            {
                panelRoot.SetActive(true);
            }

            ThirdPersonOrbitCamera.CursorUnlockRequested = true;
            messageInputField?.ActivateInputField();

            if (_activeAgent == null)
            {
                if (dialogueText != null)
                {
                    dialogueText.text = "(This NPC has no AI configured.)";
                }
                return;
            }

            NPCPersonaDefinition persona = _activeNpc.Persona;
            QuestDefinition questToTurnIn = persona != null ? persona.OfferableQuest : null;

            if (questToTurnIn != null && QuestLog.Instance != null && QuestLog.Instance.IsReadyToTurnIn(questToTurnIn) &&
                !string.IsNullOrEmpty(persona.QuestTurnInDialogue))
            {
                ShowQuestTurnInDialogue(_activeAgent, persona.QuestTurnInDialogue, questToTurnIn);
                return;
            }

            string preloadedGreeting = persona != null ? persona.FirstEncounterGreeting : null;
            if (!string.IsNullOrEmpty(preloadedGreeting) && !HasExistingHistory(_activeAgent))
            {
                ShowPreloadedGreeting(_activeAgent, preloadedGreeting);
            }
            else
            {
                RequestGreeting(_activeAgent);
            }
        }

        private async void ShowQuestTurnInDialogue(LLMAgent agent, string dialogue, QuestDefinition quest)
        {
            if (dialogueText != null)
            {
                dialogueText.text = dialogue;
            }

            QuestLog.Instance?.TurnInQuest(quest);

            // Same durable-memory treatment as every other scripted line — recorded as a real
            // assistant turn so later conversation naturally reflects that the quest is done.
            await agent.AddAssistantMessage(dialogue);
            await agent.SaveHistory();
            _ = agent.Warmup();
        }

        // A save file only ever gets written after a real greeting/exchange, so its absence is an
        // unambiguous, synchronous "never met before" signal — unlike checking agent.chat.Count,
        // which can read as empty just because the agent's own async history-load hasn't finished
        // yet, not because history is actually empty.
        private static bool HasExistingHistory(LLMAgent agent)
        {
            return !string.IsNullOrEmpty(agent.save) && System.IO.File.Exists(agent.GetSavePath());
        }

        private async void ShowPreloadedGreeting(LLMAgent agent, string greeting)
        {
            if (dialogueText != null)
            {
                dialogueText.text = greeting;
            }

            // Record it as a real assistant turn, exactly like an AI-generated greeting would be,
            // so follow-up messages have it as context. Warmup afterward (not awaited — runs in
            // the background) processes the system prompt + this greeting now, so the player's
            // first *typed* reply — which still needs a real generation — isn't paying the full
            // cold-start cost too.
            await agent.AddAssistantMessage(greeting);
            await agent.SaveHistory();
            _ = agent.Warmup();
        }

        // Hidden scene-setting cue, not a real player message — never added to the saved
        // conversation history itself, only the greeting it produces is (see RequestGreeting).
        private const string GreetingTriggerPrompt = "*The player approaches you and waits for you to speak.*";

        private async void RequestGreeting(LLMAgent agent)
        {
            _isWaitingForResponse = true;

            if (dialogueText != null)
            {
                dialogueText.text = "...";
            }

            string response = await agent.Chat(GreetingTriggerPrompt, partial =>
            {
                if (IsOpen && dialogueText != null)
                {
                    dialogueText.text = StripQuestOfferMarker(partial);
                }
            }, null, addToHistory: false);

            _isWaitingForResponse = false;

            // The chat may have been closed (or a different NPC's chat opened) while this was
            // generating, or this greeting may no longer be for the currently active agent.
            if (!IsOpen || _activeAgent != agent)
            {
                return;
            }

            bool hasQuestOffer = response != null && response.Contains(NPCPersonaDefinition.QuestOfferMarker);
            string displayText = StripQuestOfferMarker(response);

            if (dialogueText != null)
            {
                dialogueText.text = string.IsNullOrEmpty(displayText) ? "..." : displayText;
            }

            // Record only the greeting itself (not the hidden trigger line) so future turns have
            // real context without a synthetic stage direction cluttering the saved history.
            if (!string.IsNullOrEmpty(displayText))
            {
                await agent.AddAssistantMessage(displayText);
                await agent.SaveHistory();
            }

            if (hasQuestOffer)
            {
                TryShowQuestOffer(_activeNpc != null ? _activeNpc.Persona?.OfferableQuest : null);
            }
        }

        public void CloseChat()
        {
            if (!IsOpen)
            {
                return;
            }

            // Abort any in-flight request rather than let a response arrive after the panel's
            // gone — SendPlayerMessage's post-await code already no-ops if IsOpen is false by
            // then, but there's no reason to keep the model working on a reply nobody will see.
            _activeAgent?.CancelRequests();
            _isWaitingForResponse = false;

            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }

            if (_activeNpc != null)
            {
                _activeNpc.SetChatting(false);
                _activeNpc = null;
            }

            _activeAgent = null;
            HideQuestOffer();
            IsTypingInInput = false;
            ThirdPersonOrbitCamera.CursorUnlockRequested = false;
        }

        private void HideQuestOffer()
        {
            _pendingQuestOffer = null;
            if (acceptQuestButton != null)
            {
                acceptQuestButton.gameObject.SetActive(false);
            }
        }

        private void TryShowQuestOffer(QuestDefinition quest)
        {
            if (quest == null)
            {
                return;
            }

            // The persona's system prompt was baked in at edit time and can't know at runtime
            // whether this quest was already accepted (possibly in an earlier session, since
            // acceptance itself isn't saved yet) — so this check is the real gate, not the model's
            // own "don't offer it again" instruction.
            if (QuestLog.Instance != null && QuestLog.Instance.HasAccepted(quest))
            {
                return;
            }

            _pendingQuestOffer = quest;
            if (acceptQuestButtonText != null)
            {
                acceptQuestButtonText.text = $"Accept: {quest.Title}";
            }
            if (acceptQuestButton != null)
            {
                acceptQuestButton.gameObject.SetActive(true);
            }
        }

        private async void OnAcceptQuestClicked()
        {
            if (_pendingQuestOffer == null)
            {
                return;
            }

            QuestDefinition quest = _pendingQuestOffer;
            bool accepted = QuestLog.Instance != null && QuestLog.Instance.AcceptQuest(quest);

            HideQuestOffer();

            if (accepted && dialogueText != null)
            {
                dialogueText.text += $"\n\n(Quest accepted: {quest.Title})";
            }

            // Give the model a durable memory of the acceptance, independent of QuestLog (which
            // isn't persisted yet) — this is what keeps it from re-offering the same quest later
            // in the same conversation/save file.
            if (accepted && _activeAgent != null)
            {
                await _activeAgent.AddAssistantMessage($"(You gave the player the quest \"{quest.Title}\". They have accepted it — don't offer it again.)");
                await _activeAgent.SaveHistory();
            }
        }

        private void OnInputFieldEndEdit(string text)
        {
            // onEndEdit also fires when the field loses focus by clicking away (including the
            // click that closes this panel) — only actually send on a real Enter/Return submit.
            bool submittedViaEnter = Keyboard.current != null &&
                (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);
            if (submittedViaEnter)
            {
                SendPlayerMessage(text);
            }
        }

        private async void SendPlayerMessage(string message)
        {
            if (_activeAgent == null || messageInputField == null || _isWaitingForResponse)
            {
                return;
            }

            message = message?.Trim();
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            _isWaitingForResponse = true;
            messageInputField.text = string.Empty;
            HideQuestOffer();

            if (dialogueText != null)
            {
                dialogueText.text = "...";
            }

            string response = await _activeAgent.Chat(message, partial =>
            {
                // The chat may have been closed while this response was streaming in. Strip the
                // offer marker as it streams too, so it never briefly flashes on screen even for
                // the tail end of a response.
                if (IsOpen && dialogueText != null)
                {
                    dialogueText.text = StripQuestOfferMarker(partial);
                }
            });

            _isWaitingForResponse = false;

            if (!IsOpen)
            {
                return;
            }

            bool hasQuestOffer = response != null && response.Contains(NPCPersonaDefinition.QuestOfferMarker);
            string displayText = StripQuestOfferMarker(response);

            if (dialogueText != null)
            {
                dialogueText.text = string.IsNullOrEmpty(displayText) ? "..." : displayText;
            }

            if (hasQuestOffer)
            {
                TryShowQuestOffer(_activeNpc != null ? _activeNpc.Persona?.OfferableQuest : null);
            }

            messageInputField.ActivateInputField();
        }

        private static string StripQuestOfferMarker(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return text.Replace(NPCPersonaDefinition.QuestOfferMarker, string.Empty).TrimEnd();
        }

        private void Update()
        {
            // Reset unconditionally (not just while open) — otherwise the frame CloseChat() runs
            // below flips IsOpen false, and next frame's early-return would never clear this back.
            ConsumedClickThisFrame = false;

            if (!IsOpen)
            {
                return;
            }

            IsTypingInInput = !_isWaitingForResponse && messageInputField != null && messageInputField.isFocused;

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                Vector2 screenPos = Mouse.current.position.ReadValue();
                bool insidePanel = panelRect != null &&
                    RectTransformUtility.RectangleContainsScreenPoint(panelRect, screenPos, null);

                if (!insidePanel)
                {
                    ConsumedClickThisFrame = true;
                    CloseChat();
                }
            }
        }
    }
}
