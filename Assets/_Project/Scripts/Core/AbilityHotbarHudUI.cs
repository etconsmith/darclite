using System.Collections;
using Darclite.Dialogue;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Darclite.Core
{
    // Always-visible in-game ability bar. Mirrors AbilityLoadout for its icons, and is the single
    // place that turns a number-key press into a real activation + cooldown — works the same
    // whether or not the Stat Menu happens to be open, since it just blocks input the same way
    // PlayerCombat already blocks attacks while the menu/chat is open.
    [AddComponentMenu("Darclite/Ability Hotbar HUD UI")]
    public class AbilityHotbarHudUI : MonoBehaviour
    {
        // Falls back to this for any ability not listed in AbilityCooldownOverrides below — most
        // abilities still share it since there's no full per-ability data model yet.
        private const float DefaultCooldownSeconds = 40f;

        // Named exceptions to DefaultCooldownSeconds. Add an entry here for any ability whose
        // cooldown needs to differ from the shared default.
        private static readonly (string abilityName, float cooldownSeconds)[] AbilityCooldownOverrides =
        {
            ("Lite Bracing", 25f),
        };

        private const float DeniedFlashPeakAlpha = 0.55f;
        private const float DeniedFlashDuration = 0.18f;
        private const float DeniedPunchScale = 1.25f;

        [SerializeField] private RectTransform[] slotRects = new RectTransform[AbilityLoadout.SlotCount];
        [SerializeField] private Image[] slotIcons = new Image[AbilityLoadout.SlotCount];
        [SerializeField] private Image[] slotRings = new Image[AbilityLoadout.SlotCount];
        [SerializeField] private Image[] cooldownOverlays = new Image[AbilityLoadout.SlotCount];
        [SerializeField] private Text[] cooldownTexts = new Text[AbilityLoadout.SlotCount];
        [SerializeField] private Image[] deniedFlashes = new Image[AbilityLoadout.SlotCount];

        private readonly float[] _cooldownRemaining = new float[AbilityLoadout.SlotCount];
        private readonly float[] _cooldownDuration = new float[AbilityLoadout.SlotCount];
        private Coroutine[] _deniedFeedbackCoroutines;

        private void Awake()
        {
            _deniedFeedbackCoroutines = new Coroutine[AbilityLoadout.SlotCount];
        }

        private void OnEnable()
        {
            AbilityLoadout.SlotChanged += HandleSlotChanged;
            for (int i = 0; i < slotIcons.Length; i++)
            {
                ApplyIcon(i, AbilityLoadout.GetSlot(i));
            }
        }

        private void OnDisable()
        {
            AbilityLoadout.SlotChanged -= HandleSlotChanged;
        }

        private void Update()
        {
            TickCooldowns();
            HandleActivationInput();
        }

        private void TickCooldowns()
        {
            for (int i = 0; i < _cooldownRemaining.Length; i++)
            {
                if (_cooldownRemaining[i] <= 0f)
                {
                    continue;
                }

                _cooldownRemaining[i] = Mathf.Max(0f, _cooldownRemaining[i] - Time.deltaTime);
                UpdateCooldownVisual(i);

                if (_cooldownRemaining[i] <= 0f)
                {
                    EndCooldownVisual(i);
                }
            }
        }

        private void HandleActivationInput()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            // Same guard PlayerCombat uses to block attacks while the chat/menu is open — casting
            // an ability while browsing a menu makes just as little sense as swinging a weapon.
            bool chatOpen = NPCChatUI.Instance != null && NPCChatUI.Instance.IsOpen;
            bool statMenuOpen = StatMenuUI.Instance != null && StatMenuUI.Instance.IsOpen;
            if (chatOpen || statMenuOpen)
            {
                return;
            }

            for (int i = 0; i < AbilityLoadout.SlotCount; i++)
            {
                if (WasSlotKeyPressed(i))
                {
                    TryActivate(i);
                }
            }
        }

        // Slots 1-9 map to Digit1-Digit9; the 10th slot maps to Digit0 (the physical key after 9).
        private static bool WasSlotKeyPressed(int slotIndex)
        {
            Key key = slotIndex < 9 ? (Key)((int)Key.Digit1 + slotIndex) : Key.Digit0;
            var control = Keyboard.current[key];
            return control != null && control.wasPressedThisFrame;
        }

        private void TryActivate(int index)
        {
            if (AbilityLoadout.GetSlot(index) == null)
            {
                return;
            }

            if (_cooldownRemaining[index] > 0f)
            {
                PlayDeniedFeedback(index);
                return;
            }

            StartCooldown(index, GetCooldownForSlot(index));
            AbilityLoadout.NotifyActivated(index);
        }

        private static float GetCooldownForSlot(int index)
        {
            string abilityName = AbilityLoadout.GetAbilityName(index);
            foreach ((string name, float cooldownSeconds) in AbilityCooldownOverrides)
            {
                if (name == abilityName)
                {
                    return cooldownSeconds;
                }
            }

            return DefaultCooldownSeconds;
        }

        private void StartCooldown(int index, float duration)
        {
            _cooldownDuration[index] = duration;
            _cooldownRemaining[index] = duration;
            SetGlowEnabled(index, false);

            if (cooldownOverlays[index] != null)
            {
                cooldownOverlays[index].enabled = true;
            }
            if (cooldownTexts[index] != null)
            {
                cooldownTexts[index].enabled = true;
            }

            UpdateCooldownVisual(index);
        }

        private void UpdateCooldownVisual(int index)
        {
            float fraction = _cooldownDuration[index] > 0f ? _cooldownRemaining[index] / _cooldownDuration[index] : 0f;
            if (cooldownOverlays[index] != null)
            {
                cooldownOverlays[index].fillAmount = fraction;
            }
            if (cooldownTexts[index] != null)
            {
                cooldownTexts[index].text = _cooldownRemaining[index].ToString("F1");
            }
        }

        private void EndCooldownVisual(int index)
        {
            if (cooldownOverlays[index] != null)
            {
                cooldownOverlays[index].fillAmount = 0f;
                cooldownOverlays[index].enabled = false;
            }
            if (cooldownTexts[index] != null)
            {
                cooldownTexts[index].enabled = false;
            }

            SetGlowEnabled(index, true);
        }

        private void SetGlowEnabled(int index, bool glowOn)
        {
            if (index < 0 || index >= slotRings.Length || slotRings[index] == null)
            {
                return;
            }

            Outline[] outlines = slotRings[index].GetComponents<Outline>();
            foreach (Outline outline in outlines)
            {
                outline.enabled = glowOn;
            }
        }

        private void PlayDeniedFeedback(int index)
        {
            if (index < 0 || index >= slotRects.Length || slotRects[index] == null)
            {
                return;
            }

            if (_deniedFeedbackCoroutines[index] != null)
            {
                StopCoroutine(_deniedFeedbackCoroutines[index]);
            }
            _deniedFeedbackCoroutines[index] = StartCoroutine(DeniedFeedbackRoutine(index));
        }

        private IEnumerator DeniedFeedbackRoutine(int index)
        {
            RectTransform slotRect = slotRects[index];
            Image flash = index < deniedFlashes.Length ? deniedFlashes[index] : null;
            Vector3 baseScale = Vector3.one;
            float half = DeniedFlashDuration * 0.5f;

            float timer = 0f;
            while (timer < half)
            {
                timer += Time.deltaTime;
                float t = timer / half;
                slotRect.localScale = Vector3.Lerp(baseScale, baseScale * DeniedPunchScale, t);
                SetFlashAlpha(flash, Mathf.Lerp(0f, DeniedFlashPeakAlpha, t));
                yield return null;
            }

            timer = 0f;
            while (timer < half)
            {
                timer += Time.deltaTime;
                float t = timer / half;
                slotRect.localScale = Vector3.Lerp(baseScale * DeniedPunchScale, baseScale, t);
                SetFlashAlpha(flash, Mathf.Lerp(DeniedFlashPeakAlpha, 0f, t));
                yield return null;
            }

            slotRect.localScale = baseScale;
            SetFlashAlpha(flash, 0f);
        }

        private static void SetFlashAlpha(Image image, float alpha)
        {
            if (image == null)
            {
                return;
            }
            Color color = image.color;
            image.color = new Color(color.r, color.g, color.b, alpha);
        }

        private void HandleSlotChanged(int index, Sprite icon)
        {
            ApplyIcon(index, icon);
        }

        private void ApplyIcon(int index, Sprite icon)
        {
            if (index < 0 || index >= slotIcons.Length || slotIcons[index] == null)
            {
                return;
            }

            slotIcons[index].sprite = icon;
            slotIcons[index].enabled = icon != null;
        }
    }
}
