using System.Collections;
using Darclite.Combat;
using Darclite.Core;
using UnityEngine;
using UnityEngine.VFX;

namespace Darclite.Player
{
    // Lite Sip: an instant, no-animation heal — pressing the hotbar key immediately plays the VFX
    // at the player's feet and the audio, and heals a flat percentage of max health, all in one beat.
    [AddComponentMenu("Darclite/Lite Sip Ability")]
    public class LiteSipAbility : MonoBehaviour
    {
        private const string AbilityName = "Lite Sip";
        private const float HealFraction = 0.05f;

        [SerializeField] private Combatant combatant;
        [SerializeField] private VisualEffect sipVfx;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip sipClip;

        // Long enough for the sip VFX to fully finish spawning, but well under the interval a
        // misconfigured Spawn context might re-trigger itself on — guards against the effect
        // looping indefinitely off a single Play() call regardless of what's actually inside the
        // graph (the same class of bug fixed on every other VFX Graph effect in this project).
        private const float SipVfxAutoStopDelay = 1.5f;

        private Coroutine _sipVfxStopRoutine;

        private void Awake()
        {
            // VisualEffect assets auto-fire their "OnPlay" event once as soon as they're enabled —
            // stop it immediately so it doesn't visibly go off the moment the scene loads.
            if (sipVfx != null)
            {
                sipVfx.Stop();
            }
        }

        private void OnEnable()
        {
            AbilityLoadout.Activated += HandleActivated;
        }

        private void OnDisable()
        {
            AbilityLoadout.Activated -= HandleActivated;
        }

        private void HandleActivated(int slotIndex)
        {
            if (AbilityLoadout.GetAbilityName(slotIndex) != AbilityName)
            {
                return;
            }

            if (sipVfx != null)
            {
                sipVfx.Play();

                if (_sipVfxStopRoutine != null)
                {
                    StopCoroutine(_sipVfxStopRoutine);
                }
                _sipVfxStopRoutine = StartCoroutine(StopSipVfxAfterDelay());
            }

            if (audioSource != null && sipClip != null)
            {
                audioSource.PlayOneShot(sipClip);
            }

            if (combatant != null)
            {
                combatant.Heal(Mathf.RoundToInt(combatant.MaxHealth * HealFraction));
            }
        }

        private IEnumerator StopSipVfxAfterDelay()
        {
            yield return new WaitForSeconds(SipVfxAutoStopDelay);
            if (sipVfx != null)
            {
                sipVfx.Stop();
            }
            _sipVfxStopRoutine = null;
        }
    }
}
