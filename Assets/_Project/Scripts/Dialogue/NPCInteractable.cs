using LLMUnity;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Darclite.Dialogue
{
    [AddComponentMenu("Darclite/NPC Interactable")]
    public class NPCInteractable : MonoBehaviour
    {
        private static readonly int OutlineColorParam = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthParam = Shader.PropertyToID("_OutlineWidth");

        [SerializeField] private string npcName = "Quest Giver";
        [SerializeField] private Transform player;
        [SerializeField] private Transform lookPoint;
        [SerializeField] private GameObject promptRoot;
        [SerializeField] private Renderer[] highlightRenderers;
        [SerializeField] private LLMAgent llmAgent;
        [SerializeField] private NPCPersonaDefinition persona;

        [Header("Interaction")]
        [SerializeField] private float interactionRange = 3.5f;
        [SerializeField] private float lookAngleThreshold = 30f;

        [Header("Highlight")]
        [SerializeField] private Color highlightOutlineColor = new Color(1f, 0.85f, 0.3f, 1f);
        // Multiplier on the material's own authored outline width, not an absolute value — this
        // rig bakes a large uniform scale into its hierarchy, so the outline shader's object-space
        // width has to stay proportional to that scale (see ApplyCharacterMaterial) or a fixed
        // number either does nothing or, as before, balloons into a sphere around the character.
        [SerializeField] private float highlightOutlineWidthMultiplier = 5f;

        public string NpcName => npcName;
        public LLMAgent LlmAgent => llmAgent;
        public NPCPersonaDefinition Persona => persona;

        private UnityEngine.Camera _mainCamera;
        private MaterialPropertyBlock _propertyBlock;
        private Color _baseOutlineColor;
        private float _baseOutlineWidth;
        private float _highlightOutlineWidth;
        private bool _isHighlighted;
        private bool _isChatting;

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();

            if (player == null)
            {
                GameObject playerObject = GameObject.Find("Player");
                if (playerObject != null)
                {
                    player = playerObject.transform;
                }
            }

            if (lookPoint == null)
            {
                lookPoint = transform;
            }

            if (llmAgent == null)
            {
                llmAgent = GetComponent<LLMAgent>();
            }

            if (promptRoot != null)
            {
                promptRoot.SetActive(false);
            }

            if (highlightRenderers != null && highlightRenderers.Length > 0 && highlightRenderers[0] != null)
            {
                Material sharedMaterial = highlightRenderers[0].sharedMaterial;
                if (sharedMaterial != null)
                {
                    _baseOutlineColor = sharedMaterial.GetColor(OutlineColorParam);
                    _baseOutlineWidth = sharedMaterial.GetFloat(OutlineWidthParam);
                }
            }

            _highlightOutlineWidth = _baseOutlineWidth * highlightOutlineWidthMultiplier;
        }

        private void Update()
        {
            if (_mainCamera == null)
            {
                _mainCamera = UnityEngine.Camera.main;
            }

            if (player == null || _mainCamera == null)
            {
                return;
            }

            if (_isChatting)
            {
                // Only distance matters once talking — the player shouldn't need to keep
                // staring at the NPC's face while reading/typing.
                float chatDistance = Vector3.Distance(player.position, transform.position);
                if (chatDistance > interactionRange)
                {
                    NPCChatUI.Instance?.CloseChat();
                }
                return;
            }

            bool canInteract = EvaluateCanInteract();
            SetHighlighted(canInteract);

            if (promptRoot != null)
            {
                promptRoot.SetActive(canInteract);
            }

            if (canInteract && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                NPCChatUI.Instance?.OpenChat(this);
            }
        }

        private void LateUpdate()
        {
            // Billboard the prompt after everything else has moved this frame (same pattern as
            // EnemyHealthUI) so it always reads face-on instead of showing its side/back edge.
            if (promptRoot != null && promptRoot.activeSelf && _mainCamera != null)
            {
                promptRoot.transform.rotation = _mainCamera.transform.rotation;
            }
        }

        private bool EvaluateCanInteract()
        {
            // Distance must be measured from the player, not the camera — an orbit camera
            // trails behind/above the player, so a camera-based distance could stay outside
            // interactionRange even while the player is standing right next to the NPC.
            float distance = Vector3.Distance(player.position, lookPoint.position);
            if (distance > interactionRange)
            {
                return false;
            }

            Vector3 toNpc = lookPoint.position - _mainCamera.transform.position;
            float angle = Vector3.Angle(_mainCamera.transform.forward, toNpc);
            return angle <= lookAngleThreshold;
        }

        private void SetHighlighted(bool highlighted)
        {
            if (highlighted == _isHighlighted || highlightRenderers == null)
            {
                return;
            }

            _isHighlighted = highlighted;

            foreach (Renderer targetRenderer in highlightRenderers)
            {
                if (targetRenderer == null)
                {
                    continue;
                }

                targetRenderer.GetPropertyBlock(_propertyBlock);
                if (highlighted)
                {
                    _propertyBlock.SetColor(OutlineColorParam, highlightOutlineColor);
                    _propertyBlock.SetFloat(OutlineWidthParam, _highlightOutlineWidth);
                }
                else
                {
                    _propertyBlock.SetColor(OutlineColorParam, _baseOutlineColor);
                    _propertyBlock.SetFloat(OutlineWidthParam, _baseOutlineWidth);
                }
                targetRenderer.SetPropertyBlock(_propertyBlock);
            }
        }

        public void SetChatting(bool chatting)
        {
            _isChatting = chatting;

            if (chatting)
            {
                SetHighlighted(false);
                if (promptRoot != null)
                {
                    promptRoot.SetActive(false);
                }
            }
        }
    }
}
