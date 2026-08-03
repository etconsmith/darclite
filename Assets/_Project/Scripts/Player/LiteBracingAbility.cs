using System.Collections;
using Darclite.Core;
using UnityEngine;
using UnityEngine.VFX;

namespace Darclite.Player
{
    // Lite Bracing: a short defensive channel that reduces incoming damage for its duration
    // (read by Combatant.ApplyDamage on whichever character has this active). Its whole visual is
    // the Lite Aura VFX Graph effect, parented to the player root so it rises up around their feet
    // and rides along automatically as they move — and being its own independent Play()/Stop()
    // target (not shared with LiteConcentrationAura/LiteRecoveryAbility) means it can run
    // concurrently with either without cutting either one's timer short.
    [AddComponentMenu("Darclite/Lite Bracing Ability")]
    public class LiteBracingAbility : MonoBehaviour
    {
        private const string AbilityName = "Lite Bracing";
        private const float ActiveDuration = 5f;

        // Read by Combatant to scale down incoming damage while this is active.
        public const float DamageReductionFraction = 0.2f;

        [SerializeField] private VisualEffect auraVfx;

        public bool IsActive { get; private set; }

        private Coroutine _activeRoutine;

        private void Awake()
        {
            // VisualEffect assets auto-fire their "OnPlay" event once as soon as they're enabled —
            // stop it immediately so it visibly starts off instead of already running the moment
            // the scene loads.
            if (auraVfx != null)
            {
                auraVfx.Stop();
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

            if (_activeRoutine != null)
            {
                StopCoroutine(_activeRoutine);
            }
            _activeRoutine = StartCoroutine(PlayBracing());
        }

        private IEnumerator PlayBracing()
        {
            IsActive = true;
            if (auraVfx != null)
            {
                auraVfx.Play();
            }

            yield return new WaitForSeconds(ActiveDuration);

            if (auraVfx != null)
            {
                auraVfx.Stop();
            }
            IsActive = false;
            _activeRoutine = null;
        }
    }
}
