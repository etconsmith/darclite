using System.Collections;
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
        private bool _lastGuardWasDodge;
        private Coroutine _guardCoroutine;

        private void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _attackCombo = GetComponent<AttackCombo>();
            _characterAudio = GetComponent<CharacterAudio>();

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

            if (_combatant.IsStunned || _combatant.IsBeingKnockedBack || _attackCombo.IsAttacking)
            {
                return;
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

            _guardCoroutine = StartCoroutine(GuardSequence());
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

        private IEnumerator GuardSequence()
        {
            CurrentGuardState = GuardState.Guarding;

            yield return new WaitForSeconds(guardDuration);

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
