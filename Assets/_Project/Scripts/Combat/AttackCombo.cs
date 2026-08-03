using System.Collections;
using Darclite.Player;
using UnityEngine;

namespace Darclite.Combat
{
    public enum PunchSide
    {
        None,
        Left,
        Right
    }

    [AddComponentMenu("Darclite/Attack Combo")]
    public class AttackCombo : MonoBehaviour
    {
        private static readonly int AttackParam = Animator.StringToHash("Attack");
        private static readonly int AttackIndexParam = Animator.StringToHash("AttackIndex");

        // Index into the light attack blend tree; HitIndex is the matching directional
        // reaction the target should play (0=BodyHitLeft, 1=BodyHitRight, 2=HeadHitLeft, 3=HeadHitRight).
        private static readonly int[] LightAttackHitIndices = { 0, 1, 2, 3, 0, 1, 2, 3 };

        [Header("References")]
        [SerializeField] private Animator animator;

        [Header("Combat")]
        [SerializeField] private float attackRange = 2.2f;
        [SerializeField] private int lightDamage = 10;
        [SerializeField] private int heavyDamage = 25;
        [SerializeField] private float comboResetTime = 2f;

        [SerializeField] private float[] lightAttackDurations = new float[8];
        [SerializeField] private float[] heavyAttackDurations = new float[2];

        // Per-clip time (in the already speed-scaled, real-playback timeframe) from swing start
        // to the frame the punch actually connects. Populated by SceneBootstrapper.PopulateAttackDurations
        // from the real contact frame scrubbed in each clip, so it tracks the actual animation
        // instead of one guessed delay for every punch.
        [SerializeField] private float[] lightImpactDelays = new float[8];
        [SerializeField] private float[] heavyImpactDelays = new float[2];

        public bool IsAttacking => _attackCooldownTimer > 0f;

        // Which side the currently active light punch is coming from — None while not attacking
        // or mid-heavy-swing. Lets a defender's block/dodge react to the correct side.
        public PunchSide CurrentAttackSide { get; private set; } = PunchSide.None;

        // Whether the most recently thrown attack was absorbed by the target's block/dodge.
        // Used by AI to decide whether to change strategy after being blocked.
        public bool WasLastAttackBlocked { get; private set; }

        public int ComboCount => _comboCount;

        private float _attackCooldownTimer;
        private int _comboCount;
        private float _lastLandedTime = -999f;
        private Combatant _combatant;
        private CharacterAudio _characterAudio;
        private LiteConcentrationAura _liteConcentrationAura;

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            _combatant = GetComponent<Combatant>();
            _characterAudio = GetComponent<CharacterAudio>();
            // Null for anyone who can't use the ability (enemies) — guarded at every use below.
            _liteConcentrationAura = GetComponent<LiteConcentrationAura>();
        }

        private void Update()
        {
            if (_attackCooldownTimer > 0f)
            {
                _attackCooldownTimer -= Time.deltaTime;
            }
            else
            {
                CurrentAttackSide = PunchSide.None;
            }

            if (_comboCount > 0 && Time.time - _lastLandedTime > comboResetTime)
            {
                _comboCount = 0;
            }
        }

        public void TryAttack(Transform target)
        {
            if (IsAttacking)
            {
                return;
            }

            if (_comboCount >= 3)
            {
                PerformHeavyAttack(target);
            }
            else
            {
                PerformLightAttack(target);
            }
        }

        private void PerformLightAttack(Transform target)
        {
            int index = Random.Range(0, 8);
            int hitIndex = LightAttackHitIndices[index];
            float duration = lightAttackDurations[index] > 0f ? lightAttackDurations[index] : 0.6f;
            float impactDelay = lightImpactDelays[index] > 0f ? lightImpactDelays[index] : duration * 0.4f;

            PlayAttackAnimation(index);
            _attackCooldownTimer = duration;
            CurrentAttackSide = (hitIndex % 2 == 0) ? PunchSide.Left : PunchSide.Right;
            WasLastAttackBlocked = false;

            StartCoroutine(ResolveHitAfterDelay(impactDelay, target, hitIndex, lightDamage, false, isComboHit: true));
        }

        private void PerformHeavyAttack(Transform target)
        {
            int heavyIndex = Random.Range(0, 2);
            float duration = heavyAttackDurations[heavyIndex] > 0f ? heavyAttackDurations[heavyIndex] : 0.9f;
            float impactDelay = heavyImpactDelays[heavyIndex] > 0f ? heavyImpactDelays[heavyIndex] : duration * 0.4f;

            PlayAttackAnimation(8 + heavyIndex);
            _attackCooldownTimer = duration;
            CurrentAttackSide = PunchSide.None;
            WasLastAttackBlocked = false;

            StartCoroutine(ResolveHitAfterDelay(impactDelay, target, -1, heavyDamage, true, isComboHit: false));

            // The heavy swing consumes the combo whether or not it actually connects.
            _comboCount = 0;
        }

        private IEnumerator ResolveHitAfterDelay(float impactDelay, Transform target, int hitIndex, int damage, bool isHeavy, bool isComboHit)
        {
            // Let the windup play out before the hit actually lands, so impact lines up with
            // the punch animation's contact frame instead of firing the instant the swing starts.
            yield return new WaitForSeconds(impactDelay);

            // Got hit ourselves while winding up — our swing never lands, we just take the hit
            // reaction instead. Without this check both fighters could land a "trade" any time
            // their windups overlapped.
            if (_combatant != null && (_combatant.IsStunned || _combatant.IsBeingKnockedBack))
            {
                yield break;
            }

            if (TryHitTarget(transform.position, target, hitIndex, damage, isHeavy))
            {
                _lastLandedTime = Time.time;
                _characterAudio?.PlayPunchImpact(isHeavy);
                if (isComboHit)
                {
                    _comboCount++;
                }
            }
        }

        private void PlayAttackAnimation(int attackIndex)
        {
            if (animator == null)
            {
                return;
            }

            animator.SetFloat(AttackIndexParam, attackIndex);
            animator.SetTrigger(AttackParam);
        }

        private bool TryHitTarget(Vector3 selfPosition, Transform target, int hitIndex, int damage, bool isHeavy)
        {
            if (target == null)
            {
                return false;
            }

            float distance = Vector3.Distance(selfPosition, target.position);
            if (distance > attackRange)
            {
                return false;
            }

            Combatant targetCombatant = target.GetComponent<Combatant>();
            if (targetCombatant == null)
            {
                return false;
            }

            BlockDodge targetGuard = target.GetComponent<BlockDodge>();
            if (targetGuard != null && targetGuard.CurrentGuardState == GuardState.Guarding)
            {
                // Fully absorbed by the block/dodge — no damage, doesn't count as landing.
                targetGuard.OnAttackBlocked();
                WasLastAttackBlocked = true;
                return false;
            }

            bool isBlockBroken = targetGuard != null && targetGuard.CurrentGuardState == GuardState.Vulnerable;
            int finalDamage = isBlockBroken ? damage * 2 : damage;
            bool useLiteHit = _liteConcentrationAura != null && _liteConcentrationAura.IsActive;

            if (isHeavy || isBlockBroken)
            {
                targetCombatant.TakeKnockback(finalDamage, selfPosition, useLiteHit);
            }
            else
            {
                targetCombatant.TakeHit(hitIndex, damage, useLiteHit);
            }

            if (isBlockBroken)
            {
                targetGuard.OnBlockBroken();
            }

            return true;
        }
    }
}
