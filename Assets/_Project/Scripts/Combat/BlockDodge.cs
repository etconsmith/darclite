using System.Collections;
using Darclite.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Darclite.Combat
{
    public enum GuardState
    {
        None,
        Guarding,
        Vulnerable
    }

    [RequireComponent(typeof(Combatant))]
    [RequireComponent(typeof(AttackCombo))]
    [AddComponentMenu("Darclite/Block Dodge")]
    public class BlockDodge : MonoBehaviour
    {
        private static readonly int GuardParam = Animator.StringToHash("Guard");
        private static readonly int GuardIndexParam = Animator.StringToHash("GuardIndex");

        // Matches the Guard blend tree order in AnimatorControllerBuilder.
        private const int DodgeRightIndex = 0;
        private const int DodgeLeftIndex = 1;
        private const int Block1Index = 2;
        private const int Block2Index = 3;

        [Header("References")]
        [SerializeField] private Animator animator;
        [SerializeField] private Transform opponent;
        [SerializeField] private bool respondToKeyboardInput = true;

        [Header("Timing")]
        [SerializeField] private float guardDuration = 0.23f;
        [SerializeField] private float vulnerableDuration = 0.17f;

        public GuardState CurrentGuardState { get; private set; } = GuardState.None;

        // True only while the guard animation itself is playing — this is what should lock out
        // attacking/dashing/jumping. The following Vulnerable window is a punish opportunity for
        // the opponent, not an action lockout on the defender.
        public bool IsLockedInGuardAnimation => CurrentGuardState == GuardState.Guarding;

        private Combatant _combatant;
        private AttackCombo _attackCombo;
        private CharacterAudio _characterAudio;
        private AttackCombo _opponentAttackCombo;
        private AttackSensingAbility _attackSensingAbility;
        private bool _lastGuardWasDodge;
        private Coroutine _guardCoroutine;

        private void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _attackCombo = GetComponent<AttackCombo>();
            _characterAudio = GetComponent<CharacterAudio>();
            // Null for anyone who can't use the ability (enemies) — guarded at every use below.
            _attackSensingAbility = GetComponent<AttackSensingAbility>();

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (opponent == null)
            {
                string otherName = gameObject.name == "Player" ? "Enemy" : "Player";
                GameObject other = GameObject.Find(otherName);
                if (other != null)
                {
                    opponent = other.transform;
                }
            }

            _opponentAttackCombo = opponent != null ? opponent.GetComponent<AttackCombo>() : null;
        }

        private void LateUpdate()
        {
            if (!respondToKeyboardInput)
            {
                return;
            }

            // Runs after every script's Update() this frame, so a hit that landed on us this
            // same frame is already reflected before we decide whether we're free to guard.
            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            {
                TryStartGuard();
            }
        }

        public void TryStartGuard()
        {
            if (CurrentGuardState != GuardState.None)
            {
                return;
            }

            if (_combatant.IsStunned || _combatant.IsBeingKnockedBack)
            {
                return;
            }

            bool attackSensingActive = _attackSensingAbility != null && _attackSensingAbility.IsActive;

            if (_attackCombo.IsAttacking)
            {
                if (!attackSensingActive)
                {
                    return;
                }

                // Attack Sensing I — cancel the swing outright rather than just ignoring the lockout,
                // so a canceled punch never lands and immediately reads as "not attacking."
                _attackCombo.CancelAttack();
            }

            // Already being actively combo'd — no guarding your way out of it until it resets.
            if (_opponentAttackCombo != null && _opponentAttackCombo.ComboCount > 0)
            {
                return;
            }

            int index = ChooseGuardIndex();
            _lastGuardWasDodge = index == DodgeRightIndex || index == DodgeLeftIndex;

            if (animator != null)
            {
                animator.SetFloat(GuardIndexParam, index);
                animator.SetTrigger(GuardParam);
            }

            if (_guardCoroutine != null)
            {
                StopCoroutine(_guardCoroutine);
            }

            _guardCoroutine = StartCoroutine(GuardSequence(attackSensingActive));
        }

        private int ChooseGuardIndex()
        {
            if (_opponentAttackCombo != null && _opponentAttackCombo.IsAttacking)
            {
                if (_opponentAttackCombo.CurrentAttackSide == PunchSide.Right)
                {
                    int[] options = { DodgeRightIndex, Block1Index, Block2Index };
                    return options[Random.Range(0, options.Length)];
                }

                if (_opponentAttackCombo.CurrentAttackSide == PunchSide.Left)
                {
                    int[] options = { DodgeLeftIndex, Block1Index, Block2Index };
                    return options[Random.Range(0, options.Length)];
                }
            }

            return Random.Range(0, 4);
        }

        private IEnumerator GuardSequence(bool extendedDuration)
        {
            // Set synchronously the instant this runs (before any yield) — invincibility starts
            // immediately on the same frame as the key press, whether or not it just canceled an
            // attack, never waiting on the guard/dodge animation to actually begin.
            CurrentGuardState = GuardState.Guarding;

            float duration = extendedDuration ? guardDuration * AttackSensingAbility.GuardDurationMultiplier : guardDuration;
            yield return new WaitForSeconds(duration);

            CurrentGuardState = GuardState.Vulnerable;

            yield return new WaitForSeconds(vulnerableDuration);

            CurrentGuardState = GuardState.None;
            _guardCoroutine = null;
        }

        public void OnAttackBlocked()
        {
            if (_lastGuardWasDodge)
            {
                _characterAudio?.PlayGuardDodgeHit();
            }
            else
            {
                _characterAudio?.PlayGuardBlockHit();
            }
        }

        public void OnBlockBroken()
        {
            _characterAudio?.PlayBlockBreak();

            if (_guardCoroutine != null)
            {
                StopCoroutine(_guardCoroutine);
                _guardCoroutine = null;
            }

            CurrentGuardState = GuardState.None;
        }
    }
}
