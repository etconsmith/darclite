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
        [SerializeField] private float knockbackDuration = 0.4f;
        [SerializeField, Range(0.05f, 0.9f)] private float knockbackAccelerationFraction = 0.2f;

        [Header("Animation")]
        [SerializeField] private Animator animator;

        public int CurrentHealth { get; private set; }
        public int MaxHealth => maxHealth;
        public bool IsStunned { get; private set; }
        public bool IsBeingKnockedBack { get; private set; }

        public event Action<int> HealthChanged;

        private CharacterController _characterController;
        private NavMeshAgent _navMeshAgent;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _propertyBlock;
        private Coroutine _flashCoroutine;
        private Coroutine _hitReactionCoroutine;

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
        }

        public void TakeHit(int hitIndex, int damage)
        {
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
            if (IsBeingKnockedBack)
            {
                return;
            }

            if (_hitReactionCoroutine != null)
            {
                StopCoroutine(_hitReactionCoroutine);
            }

            _hitReactionCoroutine = StartCoroutine(ApplyKnockbackAfterDelay(damage, attackerPosition));
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

            yield return new WaitForSeconds(stunDuration);
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
            while (timer < knockbackDuration)
            {
                timer += Time.deltaTime;
                float t = Mathf.Clamp01(timer / knockbackDuration);
                float speedMultiplier = EvaluateKnockbackCurve(t);
                Vector3 delta = direction * ((knockbackDistance / knockbackDuration) * speedMultiplier * Time.deltaTime);

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

            float decayT = (t - knockbackAccelerationFraction) / (1f - knockbackAccelerationFraction);
            return Mathf.SmoothStep(1f, 0f, decayT);
        }

        private void ApplyDamage(int damage)
        {
            CurrentHealth = Mathf.Max(CurrentHealth - damage, 0);
            HealthChanged?.Invoke(CurrentHealth);

            if (_flashCoroutine != null)
            {
                StopCoroutine(_flashCoroutine);
            }
            _flashCoroutine = StartCoroutine(FlashRedCoroutine());
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
