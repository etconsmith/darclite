using System.Collections;
using Darclite.CameraSystem;
using Darclite.Core;
using UnityEngine;
using UnityEngine.VFX;

namespace Darclite.Player
{
    // Forceful Strike: a one-shot damage buff for your very next connecting punch. Activating it
    // plays a VFX at both hands and a charge-up ambient loop for up to 10 seconds; if AttackCombo
    // lands any punch before that runs out, it calls ConsumeOnHit, which adds the flat damage bonus
    // (read by AttackCombo directly via IsActive/BonusDamage before calling this), forces that hit
    // onto the knockback path the same as a heavy/combo-ending punch, and fires the impact
    // VFX/audio/camera shake at the hit location. If the 10 seconds run out with nothing landed,
    // the charge just fizzles — no impact effects at all, and a blocked attempt doesn't consume it
    // either (AttackCombo never calls ConsumeOnHit for a blocked swing).
    //
    // Mutually exclusive with Lite Concentration — activating this cancels an in-progress
    // Concentration aura immediately, since only one damage powerup is meant to be active at a time.
    [AddComponentMenu("Darclite/Forceful Strike Ability")]
    public class ForcefulStrikeAbility : MonoBehaviour
    {
        private const string AbilityName = "Forceful Strike";

        public const int BonusDamage = 15;
        private const float ChargeDuration = 10f;

        // Same defensive auto-stop as Combatant's hit effect and Lite Release's explosion VFX —
        // guards against the impact graph's own Spawn context re-triggering itself on a timer
        // regardless of what a single Play() call does.
        private const float ImpactVfxAutoStopDelay = 1.5f;

        [Header("Impact Feedback")]
        [SerializeField] private float cameraShakeDuration = 0.2f;
        [SerializeField] private float cameraShakeMagnitude = 0.25f;

        [Header("References")]
        [SerializeField] private VisualEffect[] handVfx = new VisualEffect[2];
        [SerializeField] private VisualEffect impactVfx;
        [SerializeField] private AudioSource loopAudioSource;
        [SerializeField] private AudioClip chargeLoopClip;
        [SerializeField] private AudioSource impactAudioSource;
        [SerializeField] private AudioClip impactClip;

        // Read directly by AttackCombo to decide whether to add BonusDamage and force the
        // knockback path for the punch currently resolving.
        public bool IsActive { get; private set; }

        private LiteConcentrationAura _liteConcentrationAura;
        private Coroutine _chargeRoutine;
        private Coroutine _impactVfxStopRoutine;
        private ThirdPersonOrbitCamera _orbitCamera;

        private void Awake()
        {
            // Null on anyone who can't use the ability (enemies) — guarded at every use below.
            _liteConcentrationAura = GetComponent<LiteConcentrationAura>();

            // VisualEffect assets auto-fire their "OnPlay" event once as soon as they're enabled —
            // stop them immediately so nothing visibly goes off the moment the scene loads.
            foreach (VisualEffect vfx in handVfx)
            {
                vfx?.Stop();
            }
            impactVfx?.Stop();
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

            // Only one damage powerup active at a time — Forceful Strike immediately supersedes an
            // in-progress Lite Concentration aura.
            if (_liteConcentrationAura != null && _liteConcentrationAura.IsActive)
            {
                _liteConcentrationAura.Deactivate();
            }

            if (_chargeRoutine != null)
            {
                StopCoroutine(_chargeRoutine);
            }
            _chargeRoutine = StartCoroutine(ChargeRoutine());
        }

        private IEnumerator ChargeRoutine()
        {
            IsActive = true;
            SetHandVfxPlaying(true);
            PlayLoopAudio();

            yield return new WaitForSeconds(ChargeDuration);

            // Timed out without landing a hit — just fizzle, no impact effects.
            EndCharge();
            _chargeRoutine = null;
        }

        // Called by AttackCombo the instant a punch thrown while this was active actually connects
        // (never called for a blocked swing, so the charge survives being blocked).
        public void ConsumeOnHit(Vector3 hitPosition)
        {
            if (!IsActive)
            {
                return;
            }

            if (_chargeRoutine != null)
            {
                StopCoroutine(_chargeRoutine);
                _chargeRoutine = null;
            }
            EndCharge();

            if (impactVfx != null)
            {
                impactVfx.transform.position = hitPosition;
                impactVfx.Play();

                if (_impactVfxStopRoutine != null)
                {
                    StopCoroutine(_impactVfxStopRoutine);
                }
                _impactVfxStopRoutine = StartCoroutine(StopImpactVfxAfterDelay());
            }

            if (impactAudioSource != null && impactClip != null)
            {
                impactAudioSource.PlayOneShot(impactClip);
            }

            if (_orbitCamera == null)
            {
                Camera mainCamera = Camera.main;
                _orbitCamera = mainCamera != null ? mainCamera.GetComponent<ThirdPersonOrbitCamera>() : null;
            }
            _orbitCamera?.Shake(cameraShakeDuration, cameraShakeMagnitude);
        }

        private void EndCharge()
        {
            IsActive = false;
            SetHandVfxPlaying(false);
            StopLoopAudio();
        }

        private IEnumerator StopImpactVfxAfterDelay()
        {
            yield return new WaitForSeconds(ImpactVfxAutoStopDelay);
            if (impactVfx != null)
            {
                impactVfx.Stop();
            }
            _impactVfxStopRoutine = null;
        }

        private void SetHandVfxPlaying(bool playing)
        {
            foreach (VisualEffect vfx in handVfx)
            {
                if (vfx == null)
                {
                    continue;
                }

                if (playing)
                {
                    vfx.Play();
                }
                else
                {
                    vfx.Stop();
                }
            }
        }

        private void PlayLoopAudio()
        {
            if (loopAudioSource == null || chargeLoopClip == null)
            {
                return;
            }

            loopAudioSource.clip = chargeLoopClip;
            loopAudioSource.loop = true;
            loopAudioSource.volume = 1f;
            loopAudioSource.Play();
        }

        private void StopLoopAudio()
        {
            if (loopAudioSource != null)
            {
                loopAudioSource.Stop();
            }
        }
    }
}
