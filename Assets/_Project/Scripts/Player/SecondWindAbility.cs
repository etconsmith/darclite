using System.Collections;
using Darclite.Combat;
using Darclite.Core;
using UnityEngine;
using UnityEngine.VFX;

namespace Darclite.Player
{
    // Second Wind: an instant, no-animation heal-for-a-price — pressing the hotbar key immediately
    // plays the VFX at the player's feet and the audio, heals a flat percentage of max health, and
    // forces every other equipped ability onto its own cooldown at the same time. Fully repeatable
    // (gated only by its own cooldown), not a once-per-life panic button.
    [AddComponentMenu("Darclite/Second Wind Ability")]
    public class SecondWindAbility : MonoBehaviour
    {
        private const string AbilityName = "Second Wind";
        private const float HealFraction = 0.2f;

        [SerializeField] private Combatant combatant;
        [SerializeField] private VisualEffect windVfx;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip windClip;

        // Long enough for the wind VFX to fully finish spawning, but well under the interval a
        // misconfigured Spawn context might re-trigger itself on — guards against the effect
        // looping indefinitely off a single Play() call regardless of what's actually inside the
        // graph (the same class of bug fixed on every other VFX Graph effect in this project).
        private const float WindVfxAutoStopDelay = 1.5f;

        private Coroutine _windVfxStopRoutine;

        private void Awake()
        {
            // VisualEffect assets auto-fire their "OnPlay" event once as soon as they're enabled —
            // stop it immediately so it doesn't visibly go off the moment the scene loads.
            if (windVfx != null)
            {
                windVfx.Stop();
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

            if (windVfx != null)
            {
                // Parented to the player root at local zero (see SceneBootstrapper), so the emitter
                // already tracks the player automatically as a child transform — re-asserting the
                // position here just guards against anything having nudged it since setup.
                windVfx.transform.localPosition = Vector3.zero;
                windVfx.Play();

                if (_windVfxStopRoutine != null)
                {
                    StopCoroutine(_windVfxStopRoutine);
                }
                _windVfxStopRoutine = StartCoroutine(StopWindVfxAfterDelay());
            }

            if (audioSource != null && windClip != null)
            {
                audioSource.PlayOneShot(windClip);
            }

            if (combatant != null)
            {
                combatant.Heal(Mathf.RoundToInt(combatant.MaxHealth * HealFraction));
            }

            AbilityHotbarHudUI.Instance?.StartCooldownForAllSlots();
        }

        private IEnumerator StopWindVfxAfterDelay()
        {
            yield return new WaitForSeconds(WindVfxAutoStopDelay);
            if (windVfx != null)
            {
                windVfx.Stop();
            }
            _windVfxStopRoutine = null;
        }
    }
}
