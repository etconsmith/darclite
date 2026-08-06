using System.Collections;
using Darclite.Combat;
using Darclite.Core;
using UnityEngine;
using UnityEngine.VFX;

namespace Darclite.Player
{
    // Lite Flicker: Lite Burst's much weaker little sibling — a quick, narrow poke of Lite energy
    // in front of the caster with no knockback at all. Pressing the hotbar key immediately plays
    // the cast animation; once the animation has wound up to vfxStartFraction, the flicker VFX
    // fires — oriented so its local +X axis (the direction the graph's own particles travel)
    // points along the caster's current forward — and the single nearest Combatant inside a
    // narrow forward-facing cone takes a plain hit reaction (no knockback) with useLiteHit: true,
    // so it flashes the target's Lite Hit VFX instead of the normal one.
    //
    // If the caster gets stunned or knocked back any time during the windup, the whole cast
    // cancels — no VFX, no audio, no damage. The cooldown already started the instant the key was
    // pressed (AbilityHotbarHudUI.TryActivate starts it before the Activated event even fires), so
    // cancelling never gives a free reset.
    [AddComponentMenu("Darclite/Lite Flicker Ability")]
    public class LiteFlickerAbility : MonoBehaviour
    {
        private const string AbilityName = "Lite Flicker";

        private static readonly int LiteFlickerParam = Animator.StringToHash("LiteFlicker");

        // Arbitrary fixed choice — Lite Flicker isn't a punch with a "side", so there's no
        // meaningful left/right to pick; this just plays a plain body-hit stagger.
        private const int HitReactionIndex = 0;

        [Header("Flicker")]
        [SerializeField] private int flickerDamage = 5;
        [SerializeField] private float flickerRange = 4.2f;
        // Half-angle either side of forward — this doubled is the full cone width. Narrower than
        // Lite Burst's, since this is meant to read as a quick, precise poke, but not so tight
        // that a small facing/position difference between casts makes an otherwise-lined-up hit
        // miss the strict math cone even though the VFX still visually looks aimed correctly.
        [SerializeField] private float flickerHalfAngle = 20f;
        // How far in front of castAnchor (the hand) the flicker actually originates — keeps the
        // VFX from spawning inside the hand model itself.
        [SerializeField] private float castForwardOffset = 0.3f;

        [Header("Timing")]
        // Real length of the Lite Flicker cast clip, in seconds — populated by SceneBootstrapper
        // from the actual imported clip so the windup always tracks the real animation instead of
        // a guessed constant.
        [SerializeField] private float castDuration = 1f;
        [SerializeField, Range(0f, 1f)] private float vfxStartFraction = 0.4f;

        [Header("References")]
        [SerializeField] private Animator animator;
        [SerializeField] private VisualEffect flickerVfx;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip flickerClip;
        // The hand bone the flicker originates from — falls back to the player root if unset.
        [SerializeField] private Transform castAnchor;

        // Long enough for the flicker to fully finish spawning, but well under the interval a
        // misconfigured Spawn context might re-trigger itself on — guards against the effect
        // looping indefinitely off a single Play() call regardless of what's actually inside the
        // graph (the same class of bug fixed on Combatant's hit effect, Lite Release, Forceful
        // Strike's impact VFX, and Lite Burst).
        private const float FlickerVfxAutoStopDelay = 1.5f;

        private Combatant _combatant;
        private Coroutine _activeRoutine;
        private Coroutine _flickerVfxStopRoutine;

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            _combatant = GetComponent<Combatant>();

            // VisualEffect assets auto-fire their "OnPlay" event once as soon as they're enabled —
            // stop it immediately so it doesn't visibly go off the moment the scene loads.
            if (flickerVfx != null)
            {
                flickerVfx.Stop();
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
            _activeRoutine = StartCoroutine(PerformFlicker());
        }

        private IEnumerator PerformFlicker()
        {
            if (animator != null)
            {
                animator.SetTrigger(LiteFlickerParam);
            }

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

            Vector3 forward = transform.forward;
            Vector3 anchorPosition = castAnchor != null ? castAnchor.position : transform.position;
            Vector3 origin = anchorPosition + forward * castForwardOffset;

            if (flickerVfx != null)
            {
                flickerVfx.transform.position = origin;
                // Unlike Lite Burst's graph, this one's particles travel along local -X, so point
                // -X at our forward instead (confirmed by testing — Lite Burst's own +X assumption
                // fired this one backwards).
                flickerVfx.transform.rotation = Quaternion.FromToRotation(Vector3.left, forward);
                flickerVfx.Play();

                if (_flickerVfxStopRoutine != null)
                {
                    StopCoroutine(_flickerVfxStopRoutine);
                }
                _flickerVfxStopRoutine = StartCoroutine(StopFlickerVfxAfterDelay());
            }

            PlayOneShot(flickerClip);
            DealFlickerDamage(origin, forward);

            _activeRoutine = null;
        }

        private bool WasInterrupted()
        {
            return _combatant != null && (_combatant.IsStunned || _combatant.IsBeingKnockedBack);
        }

        private IEnumerator StopFlickerVfxAfterDelay()
        {
            yield return new WaitForSeconds(FlickerVfxAutoStopDelay);
            if (flickerVfx != null)
            {
                flickerVfx.Stop();
            }
            _flickerVfxStopRoutine = null;
        }

        private void PlayOneShot(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        private void DealFlickerDamage(Vector3 origin, Vector3 forward)
        {
            Combatant[] combatants = FindObjectsByType<Combatant>(FindObjectsInactive.Exclude);
            foreach (Combatant target in combatants)
            {
                if (target == null || target == _combatant || target.IsDead)
                {
                    continue;
                }

                Vector3 toTarget = target.transform.position - origin;
                toTarget.y = 0f;
                float distance = toTarget.magnitude;
                if (distance > flickerRange || distance < 0.0001f)
                {
                    continue;
                }

                if (Vector3.Angle(forward, toTarget) > flickerHalfAngle)
                {
                    continue;
                }

                // TakeHit rather than TakeKnockback — no knockback at all, just a plain hit
                // reaction and damage. useLiteHit: true still flashes the target's Lite Hit VFX
                // instead of the normal one, since this is Lite energy hitting them, not a punch.
                target.TakeHit(HitReactionIndex, flickerDamage, useLiteHit: true);
            }
        }
    }
}
