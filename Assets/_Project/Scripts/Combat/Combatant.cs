using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace Darclite.Combat
{
    [AddComponentMenu("Darclite/Combatant")]
    public class Combatant : MonoBehaviour
    {
        private static readonly int HitParam = Animator.StringToHash("Hit");
        private static readonly int HitIndexParam = Animator.StringToHash("HitIndex");
        private static readonly int KnockbackParam = Animator.StringToHash("Knockback");
        private static readonly int AttackParam = Animator.StringToHash("Attack");
        private static readonly int JumpParam = Animator.StringToHash("Jump");
        private static readonly int FlashColorParam = Shader.PropertyToID("_FlashColor");
        private static readonly int FlashAmountParam = Shader.PropertyToID("_FlashAmount");

        [Header("Health")]
        [SerializeField] private int maxHealth = 100;

        [Header("Hit Flash")]
        [SerializeField] private Color flashColor = Color.red;
        [SerializeField] private float flashDuration = 0.15f;

        [Header("Reaction Timing")]
        [SerializeField] private float hitReactionDelay = 0.2f;
        [SerializeField] private float stunDuration = 0.5f;

        [Header("Knockback")]
        [SerializeField] private float knockbackDistance = 16f;
        [SerializeField] private float knockbackDuration = 1.4f;
        [SerializeField, Range(0.05f, 0.5f)] private float knockbackAccelerationFraction = 0.15f;
        [SerializeField, Range(0.1f, 0.85f)] private float knockbackStopFraction = 0.65f;
        [SerializeField] private float slideAudioDelay = 0.65f;
        [SerializeField] private float knockbackAirHeight = 1.5f;
        // Fraction of the slide (which is kept in lockstep with the Knockback clip's own real
        // playback time via its animator state speed) at which the character's pose is meant to
        // reach the ground. Reset to a neutral 0.5 pending fresh calibration against the new clip
        // — the old value (and the correction curve below) were tuned to the previous animation.
        [SerializeField] private float knockbackGroundedFraction = 0.5f;

        // Cancels the clip's own pose wobble once grounded. Left empty (no correction) until
        // recalibrated against the new Knockback clip.
        [SerializeField]
        private AnimationCurve knockbackGroundedCorrectionCurve = new AnimationCurve();

        [Header("Animation")]
        [SerializeField] private Animator animator;

        public int CurrentHealth { get; private set; }
        public int MaxHealth => maxHealth;
        public bool IsStunned { get; private set; }
        public bool IsBeingKnockedBack { get; private set; }
        public bool IsDead { get; private set; }

        public event Action<int> HealthChanged;
        public event Action OnDeath;

        private CharacterController _characterController;
        private NavMeshAgent _navMeshAgent;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _propertyBlock;
        private Coroutine _flashCoroutine;
        private Coroutine _hitReactionCoroutine;
        private CharacterAudio _characterAudio;

        private void Awake()
        {
            CurrentHealth = maxHealth;

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            _characterController = GetComponent<CharacterController>();
            _navMeshAgent = GetComponent<NavMeshAgent>();
            _renderers = GetComponentsInChildren<Renderer>();
            _propertyBlock = new MaterialPropertyBlock();
            _characterAudio = GetComponent<CharacterAudio>();
        }

        public void TakeHit(int hitIndex, int damage)
        {
            if (IsDead)
            {
                return;
            }

            // Already flying away from a knockback — don't let a stray hit interrupt the
            // slide (it would strand the NavMeshAgent/updatePosition state mid-flight).
            if (IsBeingKnockedBack)
            {
                return;
            }

            // A new hit lands mid-combo before the previous hit reaction finished playing out —
            // cancel it so the new one takes over immediately instead of the two overlapping.
            if (_hitReactionCoroutine != null)
            {
                StopCoroutine(_hitReactionCoroutine);
            }

            _hitReactionCoroutine = StartCoroutine(ApplyHitAfterDelay(hitIndex, damage));
        }

        public void TakeKnockback(int damage, Vector3 attackerPosition)
        {
            if (IsDead)
            {
                return;
            }

            if (IsBeingKnockedBack)
            {
                return;
            }

            if (_hitReactionCoroutine != null)
            {
                StopCoroutine(_hitReactionCoroutine);
            }

            _hitReactionCoroutine = StartCoroutine(ApplyKnockbackAfterDelay(damage, attackerPosition));
            StartCoroutine(PlaySlideAudioAfterDelay());
        }

        private IEnumerator PlaySlideAudioAfterDelay()
        {
            // The knockback clip shows the character airborne before they actually touch down
            // and slide, so the slide sound waits a beat rather than firing the instant they're hit.
            yield return new WaitForSeconds(slideAudioDelay);
            _characterAudio?.PlaySlide();
        }

        private IEnumerator ApplyHitAfterDelay(int hitIndex, int damage)
        {
            // Lock out actions the instant a hit registers, not once the delay finishes — otherwise
            // both fighters could land a "trade" during the attacker's wind-up.
            IsStunned = true;

            // Clear any attack we had queued up so it can't fire once we're back in Locomotion —
            // Unity keeps a trigger armed until a transition consumes it, so a swing queued right
            // before getting hit would otherwise go off later, looking like a phantom attack.
            if (animator != null)
            {
                animator.ResetTrigger(AttackParam);
                animator.ResetTrigger(JumpParam);
            }

            yield return new WaitForSeconds(hitReactionDelay);

            ApplyDamage(damage);

            // Lethal — let the death sequence take over instead of also playing a hit reaction.
            if (IsDead)
            {
                yield break;
            }

            if (animator != null)
            {
                animator.SetFloat(HitIndexParam, hitIndex);
                animator.SetTrigger(HitParam);
            }

            yield return new WaitForSeconds(stunDuration);
            IsStunned = false;
            _hitReactionCoroutine = null;
        }

        private IEnumerator ApplyKnockbackAfterDelay(int damage, Vector3 attackerPosition)
        {
            IsStunned = true;

            if (animator != null)
            {
                animator.ResetTrigger(AttackParam);
                animator.ResetTrigger(JumpParam);
            }

            yield return new WaitForSeconds(hitReactionDelay);

            ApplyDamage(damage);

            // Lethal — let the death sequence take over instead of also playing knockback/sliding.
            if (IsDead)
            {
                yield break;
            }

            if (animator != null)
            {
                animator.SetTrigger(KnockbackParam);
            }

            Vector3 direction = transform.position - attackerPosition;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = -transform.forward;
            }
            direction.Normalize();

            yield return StartCoroutine(KnockbackSlide(direction));

            // No extra stun wait here (unlike a plain hit) — by the time the slide coroutine
            // finishes, the Knockback state has already played all the way through to standing
            // back up (its speed is synced to knockbackDuration), so holding control any longer
            // just reads as still being stunned after visibly being back on your feet.
            IsStunned = false;
            _hitReactionCoroutine = null;
        }

        private IEnumerator KnockbackSlide(Vector3 direction)
        {
            IsBeingKnockedBack = true;

            bool usingAgent = _navMeshAgent != null && _navMeshAgent.enabled;
            if (usingAgent)
            {
                _navMeshAgent.isStopped = true;
                _navMeshAgent.updatePosition = false;
            }

            float timer = 0f;
            float previousAirHeight = 0f;

            while (timer < knockbackDuration)
            {
                timer += Mathf.Min(Time.deltaTime, 1f / 30f);
                float t = Mathf.Clamp01(timer / knockbackDuration);
                float speedMultiplier = EvaluateKnockbackCurve(t);
                Vector3 delta = direction * ((knockbackDistance / knockbackDuration) * speedMultiplier * Time.deltaTime);

                // The Knockback animator state's speed is set (in AnimatorControllerBuilder) so the
                // clip's full length exactly matches knockbackDuration — so t here IS the clip's own
                // normalized time, letting this arc land exactly when the clip's pose actually
                // reaches the ground instead of guessing a shape independent of the real animation.
                float airHeight = EvaluateKnockbackAirArc(t);
                delta.y = airHeight - previousAirHeight;
                previousAirHeight = airHeight;

                if (_characterController != null && _characterController.enabled)
                {
                    _characterController.Move(delta);
                }
                else
                {
                    transform.position += delta;
                }

                yield return null;
            }

            if (usingAgent)
            {
                _navMeshAgent.Warp(transform.position);
                _navMeshAgent.updatePosition = true;
                _navMeshAgent.isStopped = false;
            }

            IsBeingKnockedBack = false;
        }

        private float EvaluateKnockbackCurve(float t)
        {
            if (t < knockbackAccelerationFraction)
            {
                float rampT = t / knockbackAccelerationFraction;
                return Mathf.SmoothStep(0f, 1f, rampT);
            }

            float stopStart = 1f - knockbackStopFraction;
            if (t < stopStart)
            {
                // Cruise at full speed instead of decaying across the whole rest of the slide.
                return 1f;
            }

            // Smooth, gradual ease into the stop (zero velocity change at both ends) across most
            // of the slide, instead of cruising at full speed and cutting off sharply.
            float stopT = (t - stopStart) / knockbackStopFraction;
            return Mathf.SmoothStep(1f, 0f, stopT);
        }

        private float EvaluateKnockbackAirArc(float t)
        {
            if (t >= knockbackGroundedFraction)
            {
                // Cancels the clip's own pose wobble for the rest of the slide instead of
                // guessing a flat offset — see knockbackGroundedCorrectionCurve.
                return knockbackGroundedCorrectionCurve.Evaluate(t);
            }

            float riseT = t / knockbackGroundedFraction;
            return Mathf.Sin(riseT * Mathf.PI) * knockbackAirHeight;
        }

        private void ApplyDamage(int damage)
        {
            if (IsDead)
            {
                return;
            }

            CurrentHealth = Mathf.Max(CurrentHealth - damage, 0);
            HealthChanged?.Invoke(CurrentHealth);

            if (_flashCoroutine != null)
            {
                StopCoroutine(_flashCoroutine);
            }
            _flashCoroutine = StartCoroutine(FlashRedCoroutine());

            if (CurrentHealth <= 0)
            {
                IsDead = true;
                OnDeath?.Invoke();
            }
        }

        private IEnumerator FlashRedCoroutine()
        {
            float timer = 0f;
            while (timer < flashDuration)
            {
                timer += Time.deltaTime;
                float amount = 1f - Mathf.Clamp01(timer / flashDuration);
                SetFlashAmount(amount);
                yield return null;
            }

            SetFlashAmount(0f);
            _flashCoroutine = null;
        }

        private void SetFlashAmount(float amount)
        {
            foreach (Renderer renderer in _renderers)
            {
                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(FlashColorParam, flashColor);
                _propertyBlock.SetFloat(FlashAmountParam, amount);
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }
    }
}
