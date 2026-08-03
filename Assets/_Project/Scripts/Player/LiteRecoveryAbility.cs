using System.Collections;
using Darclite.Combat;
using Darclite.Core;
using UnityEngine;
using UnityEngine.VFX;

namespace Darclite.Player
{
    // Recovery Lite: channels Lite to heal the player over time. Same activation pattern as
    // LiteConcentrationAura (listens for its own hotbar slot's Activated event, runs for the
    // shared ability duration), just without the multi-layered light/rim/bloom presentation —
    // its whole visual is the one hand-authored VFX Graph effect passed in.
    [AddComponentMenu("Darclite/Lite Recovery Ability")]
    public class LiteRecoveryAbility : MonoBehaviour
    {
        private const string AbilityName = "Recovery Lite";
        private const float ActiveDuration = 20f;
        private const float TickInterval = 1f;
        private const int HealPerTick = 2;

        [SerializeField] private Combatant combatant;
        [SerializeField] private VisualEffect healVfx;

        private Coroutine _activeRoutine;

        private void Awake()
        {
            // VisualEffect assets auto-fire their "OnPlay" event once as soon as they're enabled —
            // stop it immediately so the effect visibly starts off instead of already running the
            // moment the scene loads.
            if (healVfx != null)
            {
                healVfx.Stop();
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
            _activeRoutine = StartCoroutine(PlayRecovery());
        }

        private IEnumerator PlayRecovery()
        {
            if (healVfx != null)
            {
                healVfx.Play();
            }

            int tickCount = Mathf.RoundToInt(ActiveDuration / TickInterval);
            for (int i = 0; i < tickCount; i++)
            {
                yield return new WaitForSeconds(TickInterval);
                combatant?.Heal(HealPerTick);
            }

            if (healVfx != null)
            {
                healVfx.Stop();
            }
            _activeRoutine = null;
        }
    }
}
