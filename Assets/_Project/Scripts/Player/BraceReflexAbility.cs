using System.Collections;
using Darclite.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.VFX;

namespace Darclite.Player
{
    // Brace Reflex: while toggled on, pressing F triggers a very short damage-reduction window on
    // its own short cooldown — entirely independent of BlockDodge's guard system, so it still
    // fires in situations normal guard refuses to start (mid-combo, mid-attack, already guarding).
    // No animation; the VFX plays immediately and the audio is deliberately quiet, since this is
    // meant to read as a small reflexive flinch rather than a real defensive stance.
    [AddComponentMenu("Darclite/Brace Reflex Ability")]
    public class BraceReflexAbility : MonoBehaviour
    {
        private const string AbilityName = "Brace Reflex";
        private const float BraceDuration = 0.2f;
        private const float CooldownDuration = 0.5f;

        // Read by Combatant.ApplyDamage while IsBracing is true.
        public const float DamageReductionFraction = 0.1f;

        [SerializeField] private VisualEffect braceVfx;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip braceClip;
        [SerializeField, Range(0f, 1f)] private float audioVolume = 0.4f;

        // Long enough for the brace VFX to fully finish spawning, but well under the interval a
        // misconfigured Spawn context might re-trigger itself on — guards against the effect
        // looping indefinitely off a single Play() call regardless of what's actually inside the
        // graph (the same class of bug fixed on every other VFX Graph effect in this project).
        private const float BraceVfxAutoStopDelay = 1.5f;

        private float _cooldownRemaining;
        private float _braceTimer;
        private Coroutine _braceVfxStopRoutine;

        public bool IsActive => AbilityLoadout.GetToggleState(AbilityName);

        // Read by Combatant.ApplyDamage — true for BraceDuration seconds after a successful press.
        public bool IsBracing => _braceTimer > 0f;

        private void Awake()
        {
            if (braceVfx != null)
            {
                braceVfx.Stop();
            }
            if (audioSource != null)
            {
                audioSource.volume = audioVolume;
            }
        }

        private void Update()
        {
            if (_cooldownRemaining > 0f)
            {
                _cooldownRemaining -= Time.deltaTime;
            }
            if (_braceTimer > 0f)
            {
                _braceTimer -= Time.deltaTime;
            }

            if (!IsActive || _cooldownRemaining > 0f)
            {
                return;
            }

            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            {
                TriggerBrace();
            }
        }

        private void TriggerBrace()
        {
            _cooldownRemaining = CooldownDuration;
            _braceTimer = BraceDuration;

            if (braceVfx != null)
            {
                braceVfx.Play();

                if (_braceVfxStopRoutine != null)
                {
                    StopCoroutine(_braceVfxStopRoutine);
                }
                _braceVfxStopRoutine = StartCoroutine(StopBraceVfxAfterDelay());
            }

            if (audioSource != null && braceClip != null)
            {
                audioSource.PlayOneShot(braceClip);
            }
        }

        private IEnumerator StopBraceVfxAfterDelay()
        {
            yield return new WaitForSeconds(BraceVfxAutoStopDelay);
            if (braceVfx != null)
            {
                braceVfx.Stop();
            }
            _braceVfxStopRoutine = null;
        }
    }
}
