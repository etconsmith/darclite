using System.Collections;
using Darclite.Combat;
using Darclite.Core;
using UnityEngine;
using UnityEngine.VFX;

namespace Darclite.Player
{
    // Lite Burst: a single directional cone blast in front of the caster. Pressing the hotbar key
    // immediately plays the cast animation; once the animation has wound up to vfxStartFraction,
    // the burst VFX fires — oriented so its local +X axis (the direction the graph's own particles
    // travel) points along the caster's current forward — and every Combatant inside a forward-
    // facing cone takes damage and knockback with useLiteHit: true, so the hit flashes each
    // target's Lite Hit VFX instead of the normal one.
    //
    // If the caster gets stunned or knocked back any time during the windup, the whole cast
    // cancels — no VFX, no audio, no damage. The cooldown already started the instant the key was
    // pressed (AbilityHotbarHudUI.TryActivate starts it before the Activated event even fires), so
    // cancelling never gives a free reset.
    [AddComponentMenu("Darclite/Lite Burst Ability")]
    public class LiteBurstAbility : MonoBehaviour
    {
        private const string AbilityName = "Lite Burst I";

        private static readonly int LiteBurstParam = Animator.StringToHash("LiteBurst");

        [Header("Burst")]
        [SerializeField] private int burstDamage = 15;
        [SerializeField] private float burstRange = 9f;
        // Half-angle either side of forward — this doubled is the full cone width.
        [SerializeField] private float burstHalfAngle = 50f;

        [Header("Timing")]
        // Real length of the Lite Burst cast clip, in seconds — populated by SceneBootstrapper
        // from the actual imported clip so the windup always tracks the real animation instead of
        // a guessed constant.
        [SerializeField] private float castDuration = 2f;
        [SerializeField, Range(0f, 1f)] private float vfxStartFraction = 0.55f;

        [Header("References")]
        [SerializeField] private Animator animator;
        [SerializeField] private VisualEffect burstVfx;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip burstClip;

        // Long enough for the burst to fully finish spawning, but well under the interval a
        // misconfigured Spawn context might re-trigger itself on — guards against the effect
        // looping indefinitely off a single Play() call regardless of what's actually inside the
        // graph (the same class of bug fixed on Combatant's hit effect, Lite Release, and
        // Forceful Strike's impact VFX).
        private const float BurstVfxAutoStopDelay = 1.5f;

        private Combatant _combatant;
        private Coroutine _activeRoutine;
        private Coroutine _burstVfxStopRoutine;

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            _combatant = GetComponent<Combatant>();

            // VisualEffect assets auto-fire their "OnPlay" event once as soon as they're enabled —
            // stop it immediately so it doesn't visibly go off the moment the scene loads.
            if (burstVfx != null)
            {
                burstVfx.Stop();
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
            _activeRoutine = StartCoroutine(PerformBurst());
        }

        private IEnumerator PerformBurst()
        {
            if (animator != null)
            {
                animator.SetTrigger(LiteBurstParam);
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

            Vector3 origin = transform.position;
            Vector3 forward = transform.forward;

            if (burstVfx != null)
            {
                burstVfx.transform.position = origin;
                // The graph's own particles travel along local +X, so point +X at our forward
                // rather than using the object's default forward (+Z) orientation.
                burstVfx.transform.rotation = Quaternion.FromToRotation(Vector3.right, forward);
                burstVfx.Play();

                if (_burstVfxStopRoutine != null)
                {
                    StopCoroutine(_burstVfxStopRoutine);
                }
                _burstVfxStopRoutine = StartCoroutine(StopBurstVfxAfterDelay());
            }

            PlayOneShot(burstClip);
            DealBurstDamage(origin, forward);

            _activeRoutine = null;
        }

        private bool WasInterrupted()
        {
            return _combatant != null && (_combatant.IsStunned || _combatant.IsBeingKnockedBack);
        }

        private IEnumerator StopBurstVfxAfterDelay()
        {
            yield return new WaitForSeconds(BurstVfxAutoStopDelay);
            if (burstVfx != null)
            {
                burstVfx.Stop();
            }
            _burstVfxStopRoutine = null;
        }

        private void PlayOneShot(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        private void DealBurstDamage(Vector3 origin, Vector3 forward)
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
                if (distance > burstRange || distance < 0.0001f)
                {
                    continue;
                }

                if (Vector3.Angle(forward, toTarget) > burstHalfAngle)
                {
                    continue;
                }

                // useLiteHit: true — this is Lite energy hitting them, not a plain punch, so their
                // Lite Hit flash VFX plays instead of the normal hit effect.
                target.TakeKnockback(burstDamage, origin, useLiteHit: true);
            }
        }
    }
}
