using System.Collections;
using Darclite.CameraSystem;
using Darclite.Combat;
using Darclite.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;

namespace Darclite.Player
{
    // Lite Release: an AoE burst. Pressing the hotbar key immediately plays the cast animation and
    // the background audio; once the animation has wound up about halfway, the explosion VFX and
    // its own audio fire, and a beat later the actual damage/knockback goes out to every other
    // Combatant in range — the hit intentionally lands a hair after the VFX rather than exactly on
    // it, so the burst reads as the cause and the damage as its (near-instant) effect.
    //
    // Deals a flat amount of damage straight through Combatant.TakeKnockback rather than through
    // AttackCombo, so it never reads LiteConcentrationAura's damage multiplier — this is raw
    // released Lite, not a punch.
    //
    // If the caster gets stunned or knocked back any time during the windup, the whole cast
    // cancels — no VFX, no audio, no damage. The cooldown already started the instant the key was
    // pressed (AbilityHotbarHudUI.TryActivate starts it before the Activated event even fires), so
    // cancelling never gives a free reset.
    [AddComponentMenu("Darclite/Lite Release Ability")]
    public class LiteReleaseAbility : MonoBehaviour
    {
        private const string AbilityName = "Lite Release";

        private static readonly int LiteReleaseParam = Animator.StringToHash("LiteRelease");

        [Header("Explosion")]
        [SerializeField] private int explosionDamage = 20;
        [SerializeField] private float explosionRadius = 6f;

        [Header("Timing")]
        // Real length of the Lite Release cast clip, in seconds — populated by SceneBootstrapper
        // from the actual imported clip so the windup always tracks the real animation instead of
        // a guessed constant.
        [SerializeField] private float castDuration = 2f;
        [SerializeField, Range(0f, 1f)] private float vfxStartFraction = 0.5f;
        [SerializeField] private float attackDelayAfterVfx = 0.08f;

        [Header("Impact Feedback")]
        [SerializeField] private float cameraShakeDuration = 0.25f;
        [SerializeField] private float cameraShakeMagnitude = 0.3f;
        [SerializeField] private float blurPeakRadius = 4f;
        [SerializeField] private float blurRampDuration = 0.12f;
        [SerializeField] private float blurHoldDuration = 0.1f;
        [SerializeField] private float blurFadeDuration = 0.35f;

        [Header("References")]
        [SerializeField] private Animator animator;
        [SerializeField] private VisualEffect releaseVfx;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip backgroundClip;
        [SerializeField] private AudioClip explosionClip;
        [SerializeField] private Volume gameplayVolume;

        // Long enough for the burst to fully finish spawning, but well under the ~2s interval the
        // VFX graph's own (misconfigured) Spawn context was found to re-trigger itself on — guards
        // against the effect looping indefinitely off a single Play() call regardless of what's
        // actually inside the graph's Spawn context (same fix as Combatant's hit-effect auto-stop).
        private const float ReleaseVfxAutoStopDelay = 1.5f;

        private Combatant _combatant;
        private Coroutine _activeRoutine;
        private Coroutine _releaseVfxStopRoutine;
        private Coroutine _blurRoutine;
        private ThirdPersonOrbitCamera _orbitCamera;
        private DepthOfField _depthOfField;

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            _combatant = GetComponent<Combatant>();

            if (gameplayVolume != null && gameplayVolume.profile != null)
            {
                gameplayVolume.profile.TryGet(out _depthOfField);
            }

            // VisualEffect assets auto-fire their "OnPlay" event once as soon as they're enabled —
            // stop it immediately so it doesn't visibly go off the moment the scene loads.
            if (releaseVfx != null)
            {
                releaseVfx.Stop();
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
            _activeRoutine = StartCoroutine(PerformRelease());
        }

        private IEnumerator PerformRelease()
        {
            if (animator != null)
            {
                animator.SetTrigger(LiteReleaseParam);
            }
            PlayOneShot(backgroundClip);

            float windupDuration = castDuration * vfxStartFraction;
            float timer = 0f;
            while (timer < windupDuration)
            {
                if (WasInterrupted())
                {
                    _activeRoutine = null;
                    yield break;
                }
                timer += Time.deltaTime;
                yield return null;
            }

            if (WasInterrupted())
            {
                _activeRoutine = null;
                yield break;
            }

            Vector3 explosionOrigin = transform.position;
            if (releaseVfx != null)
            {
                releaseVfx.transform.position = explosionOrigin;
                releaseVfx.Play();

                if (_releaseVfxStopRoutine != null)
                {
                    StopCoroutine(_releaseVfxStopRoutine);
                }
                _releaseVfxStopRoutine = StartCoroutine(StopReleaseVfxAfterDelay());
            }
            PlayOneShot(explosionClip);
            TriggerImpactFeedback();

            yield return new WaitForSeconds(attackDelayAfterVfx);

            // Once the VFX has already fired, the burst is treated as unstoppable — a stray hit in
            // this last split-second wouldn't read as "the blast never happened" to the player
            // since it's already visibly underway.
            DealExplosionDamage(explosionOrigin);
            _activeRoutine = null;
        }

        private bool WasInterrupted()
        {
            return _combatant != null && (_combatant.IsStunned || _combatant.IsBeingKnockedBack);
        }

        private IEnumerator StopReleaseVfxAfterDelay()
        {
            yield return new WaitForSeconds(ReleaseVfxAutoStopDelay);
            if (releaseVfx != null)
            {
                releaseVfx.Stop();
            }
            _releaseVfxStopRoutine = null;
        }

        private void TriggerImpactFeedback()
        {
            if (_orbitCamera == null)
            {
                Camera mainCamera = Camera.main;
                _orbitCamera = mainCamera != null ? mainCamera.GetComponent<ThirdPersonOrbitCamera>() : null;
            }
            _orbitCamera?.Shake(cameraShakeDuration, cameraShakeMagnitude);

            if (_depthOfField != null)
            {
                if (_blurRoutine != null)
                {
                    StopCoroutine(_blurRoutine);
                }
                _blurRoutine = StartCoroutine(PlayBlurPulse());
            }
        }

        // Punch in, hold for a beat, then settle back out — same envelope shape as
        // LiteConcentrationAura's light/rim/bloom pulse, just much shorter since this is a single
        // instant of impact rather than a sustained ability window.
        private IEnumerator PlayBlurPulse()
        {
            _depthOfField.active = true;

            yield return DriveBlurRadius(0f, blurPeakRadius, blurRampDuration);
            yield return new WaitForSeconds(blurHoldDuration);
            yield return DriveBlurRadius(blurPeakRadius, 0f, blurFadeDuration);

            _depthOfField.active = false;
            _blurRoutine = null;
        }

        private IEnumerator DriveBlurRadius(float from, float to, float duration)
        {
            if (duration <= 0f)
            {
                _depthOfField.gaussianMaxRadius.value = to;
                yield break;
            }

            float timer = 0f;
            while (timer < duration)
            {
                timer += Time.deltaTime;
                _depthOfField.gaussianMaxRadius.value = Mathf.Lerp(from, to, timer / duration);
                yield return null;
            }
            _depthOfField.gaussianMaxRadius.value = to;
        }

        private void PlayOneShot(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        private void DealExplosionDamage(Vector3 origin)
        {
            Combatant[] combatants = FindObjectsByType<Combatant>(FindObjectsInactive.Exclude);
            foreach (Combatant target in combatants)
            {
                if (target == null || target == _combatant || target.IsDead)
                {
                    continue;
                }

                float sqrDistance = (target.transform.position - origin).sqrMagnitude;
                if (sqrDistance > explosionRadius * explosionRadius)
                {
                    continue;
                }

                // Fixed damage, never multiplied by Lite Concentration and never routed through
                // AttackCombo — this is a direct area effect, not a punch.
                target.TakeKnockback(explosionDamage, origin, useLiteHit: false);
            }

            DestructibleChunk[] chunks = FindObjectsByType<DestructibleChunk>(FindObjectsInactive.Exclude);
            foreach (DestructibleChunk chunk in chunks)
            {
                if (chunk == null)
                {
                    continue;
                }

                float chunkSqrDistance = (chunk.transform.position - origin).sqrMagnitude;
                if (chunkSqrDistance > explosionRadius * explosionRadius)
                {
                    continue;
                }

                chunk.ApplyDamage(explosionDamage, origin);
            }
        }
    }
}
