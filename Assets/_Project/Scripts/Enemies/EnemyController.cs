using Darclite.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Darclite.Enemies
{
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Combatant))]
    [RequireComponent(typeof(AttackCombo))]
    [AddComponentMenu("Darclite/Enemy Controller")]
    public class EnemyController : MonoBehaviour
    {
        private static readonly int MoveXParam = Animator.StringToHash("MoveX");
        private static readonly int MoveYParam = Animator.StringToHash("MoveY");
        private static readonly int GroundedParam = Animator.StringToHash("Grounded");
        private static readonly int JumpParam = Animator.StringToHash("Jump");

        [Header("Targeting")]
        [SerializeField] private Transform player;

        [Header("Vision & Detection")]
        [SerializeField] private float visionRange = 30f;
        [SerializeField, Range(0f, 90f)] private float visionHalfAngleDegrees = 45f;
        [SerializeField] private float loseTargetAfterSeconds = 10f;

        [Header("Wander (while player undetected)")]
        // Stays close to wherever it started — spawn position for a runtime-spawned bandit, or
        // wherever it was manually placed in the Editor.
        [SerializeField] private float wanderRadius = 3.5f;
        [SerializeField] private float wanderMinPause = 2f;
        [SerializeField] private float wanderMaxPause = 5f;
        [SerializeField, Range(0f, 1f)] private float wanderMoveChance = 0.5f;
        [SerializeField] private float wanderSpeedMultiplier = 0.5f;
        [SerializeField] private float idleTurnMaxAngle = 60f;
        [SerializeField] private float idleTurnSpeed = 60f;

        [Header("Combat")]
        [SerializeField] private float attackRange = 2f;
        [SerializeField, Range(0f, 1f)] private float hitStunSpeedMultiplier = 0.15f;

        [Header("Guard Strategy")]
        [SerializeField, Range(0f, 1f)] private float guardOnApproachChance = 0.4f;
        [SerializeField, Range(0f, 1f)] private float switchToGuardChanceAfterBlocked = 0.5f;
        // Once guarding, most of the time it waits to actually see the player's punch and
        // guarantees the block; the rest of the time it gambles blindly, guarding the instant
        // the player's own block (the one that stuffed our punch) ends, betting they swing right away.
        [SerializeField, Range(0f, 1f)] private float reactiveGuardChance = 0.7f;
        [SerializeField] private float guardAnticipationWindow = 3f;

        // A brief beat of nothing after successfully guarding before counter-attacking. Short
        // enough that mindlessly throwing a second punch right after getting guarded still loses
        // the race to our counter, but long enough that a player who reacts to seeing our swing
        // start has just enough time to guard it themselves.
        [SerializeField] private float postGuardAttackDelay = 0.15f;

        [Header("Movement")]
        [SerializeField] private float rotationSpeed = 10f;
        [SerializeField] private float runBlendMultiplier = 2f;
        [SerializeField] private float footstepInterval = 0.3f;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private float animatorSpeedDamping = 0.15f;

        [Header("Off-Mesh Link Jump")]
        [SerializeField] private float linkJumpDuration = 0.5f;
        [SerializeField] private float linkJumpHeight = 1f;

        private enum AiStrategy
        {
            Attack,
            Guard
        }

        private NavMeshAgent _agent;
        private Combatant _combatant;
        private AttackCombo _attackCombo;
        private CharacterAudio _characterAudio;
        private BlockDodge _blockDodge;
        private AttackCombo _playerAttackCombo;
        private BlockDodge _playerBlockDodge;
        private float _baseAgentSpeed;
        private float _footstepTimer;
        private bool _isTraversingLink;
        private float _linkTraversalTimer;
        private Vector3 _linkStartPosition;
        private Vector3 _linkEndPosition;

        private AiStrategy _strategy = AiStrategy.Attack;
        private float _guardWaitTimer;
        private bool _wasAttackingLastFrame;
        private bool _isReactingToAnticipatedPunch;
        private bool _useBlindGuardTiming;
        private bool _isInPostGuardDelay;
        private float _postGuardDelayTimer;
        private bool _wasInStrikeRange;

        private bool _hasDetectedPlayer;
        private float _timeSincePlayerLastSeen;
        private Vector3 _homePosition;
        private bool _isWalkingToWanderPoint;
        private float _wanderTimer;
        private Quaternion _wanderTargetRotation;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _agent.updateRotation = false;
            _agent.autoTraverseOffMeshLink = false;
            _baseAgentSpeed = _agent.speed;

            _combatant = GetComponent<Combatant>();
            _attackCombo = GetComponent<AttackCombo>();
            _characterAudio = GetComponent<CharacterAudio>();
            _blockDodge = GetComponent<BlockDodge>();

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (player == null)
            {
                GameObject playerObject = GameObject.Find("Player");
                if (playerObject != null)
                {
                    player = playerObject.transform;
                }
            }

            if (player != null)
            {
                _playerAttackCombo = player.GetComponent<AttackCombo>();
                _playerBlockDodge = player.GetComponent<BlockDodge>();
            }

            _homePosition = transform.position;
            _wanderTargetRotation = transform.rotation;
            _wanderTimer = Random.Range(wanderMinPause, wanderMaxPause);
        }

        private void Update()
        {
            if (player == null)
            {
                return;
            }

            // Detect the moment our own swing's cooldown just ended, regardless of what branch
            // we take below, so we don't miss the transition if we got interrupted mid-attack.
            bool isAttackingNow = _attackCombo.IsAttacking;
            if (_wasAttackingLastFrame && !isAttackingNow)
            {
                OnOwnAttackResolved();
            }
            _wasAttackingLastFrame = isAttackingNow;

            if (_combatant.IsBeingKnockedBack)
            {
                // Getting knocked around obviously reveals where the attacker is, even if it
                // happened outside the vision cone (e.g. a hit from behind).
                _hasDetectedPlayer = true;
                _timeSincePlayerLastSeen = 0f;

                _agent.isStopped = true;
                UpdateAnimator();
                return;
            }

            if (_combatant.IsStunned)
            {
                _hasDetectedPlayer = true;
                _timeSincePlayerLastSeen = 0f;

                // Can't attack while reeling from a hit, but still creep toward the player
                // at a fraction of normal speed instead of being fully frozen in place.
                // Stop at attackRange rather than the player's exact position — otherwise,
                // across a multi-hit combo, it'll keep closing the gap to zero and merge
                // into the player's space.
                _agent.speed = _baseAgentSpeed * hitStunSpeedMultiplier;

                if (Vector3.Distance(transform.position, player.position) > attackRange)
                {
                    _agent.isStopped = false;
                    _agent.SetDestination(player.position);
                    RotateTowardsMovement();
                }
                else
                {
                    _agent.isStopped = true;
                    FaceTarget(player.position);
                }

                UpdateAnimator();
                return;
            }

            _agent.speed = _baseAgentSpeed;

            if (_agent.isOnOffMeshLink)
            {
                TraverseOffMeshLink();
            }
            else
            {
                UpdateVisionDetection();

                if (_hasDetectedPlayer)
                {
                    ChaseAndAttack();
                }
                else
                {
                    Wander();
                }
            }

            UpdateAnimator();
        }

        private void UpdateVisionDetection()
        {
            if (CanSeePlayer())
            {
                _hasDetectedPlayer = true;
                _timeSincePlayerLastSeen = 0f;
                return;
            }

            if (!_hasDetectedPlayer)
            {
                return;
            }

            _timeSincePlayerLastSeen += Time.deltaTime;
            if (_timeSincePlayerLastSeen >= loseTargetAfterSeconds)
            {
                _hasDetectedPlayer = false;
                ResetCombatState();
            }
        }

        private bool CanSeePlayer()
        {
            Vector3 toPlayer = player.position - transform.position;
            float distance = toPlayer.magnitude;
            if (distance > visionRange)
            {
                return false;
            }

            float angle = Vector3.Angle(transform.forward, toPlayer);
            return angle <= visionHalfAngleDegrees;
        }

        // Clears mid-fight state (guard sequencing, strike-range tracking) so re-engaging later
        // starts clean instead of possibly resuming a stale guard/attack sequence from before.
        private void ResetCombatState()
        {
            _strategy = AiStrategy.Attack;
            _wasInStrikeRange = false;
            _isReactingToAnticipatedPunch = false;
            _isInPostGuardDelay = false;
            _guardWaitTimer = 0f;
        }

        private void Wander()
        {
            if (_isWalkingToWanderPoint)
            {
                _agent.speed = _baseAgentSpeed * wanderSpeedMultiplier;

                if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.1f)
                {
                    _isWalkingToWanderPoint = false;
                    _agent.isStopped = true;
                    _wanderTargetRotation = transform.rotation;
                    _wanderTimer = Random.Range(wanderMinPause, wanderMaxPause);
                }
                else
                {
                    RotateTowardsMovement();
                }

                UpdateFootsteps();
                return;
            }

            _agent.isStopped = true;
            _footstepTimer = 0f;

            _wanderTimer -= Time.deltaTime;
            if (_wanderTimer <= 0f)
            {
                if (Random.value < wanderMoveChance && TryPickWanderPoint(out Vector3 wanderPoint))
                {
                    _agent.speed = _baseAgentSpeed * wanderSpeedMultiplier;
                    _agent.isStopped = false;
                    _agent.SetDestination(wanderPoint);
                    _isWalkingToWanderPoint = true;
                }
                else
                {
                    // Just turn and look around a little rather than walk anywhere.
                    float turnAngle = Random.Range(-idleTurnMaxAngle, idleTurnMaxAngle);
                    _wanderTargetRotation = transform.rotation * Quaternion.Euler(0f, turnAngle, 0f);
                    _wanderTimer = Random.Range(wanderMinPause, wanderMaxPause);
                }
            }

            transform.rotation = Quaternion.RotateTowards(transform.rotation, _wanderTargetRotation, idleTurnSpeed * Time.deltaTime);
        }

        private bool TryPickWanderPoint(out Vector3 point)
        {
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = _homePosition + new Vector3(offset.x, 0f, offset.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }

            point = Vector3.zero;
            return false;
        }

        private void ChaseAndAttack()
        {
            float distance = Vector3.Distance(transform.position, player.position);
            bool inStrikeRange = distance <= attackRange;

            if (!inStrikeRange)
            {
                _wasInStrikeRange = false;
                _agent.isStopped = false;
                _agent.SetDestination(player.position);
                RotateTowardsMovement();
                UpdateFootsteps();
                return;
            }

            _agent.isStopped = true;
            FaceTarget(player.position);
            _footstepTimer = 0f;

            if (!_wasInStrikeRange)
            {
                _wasInStrikeRange = true;

                // Right as we cross into striking range — gamble on guarding immediately instead
                // of attacking, in case the player is already swinging as we close the gap.
                if (_strategy == AiStrategy.Attack && Random.value < guardOnApproachChance)
                {
                    _strategy = AiStrategy.Guard;
                    ReactToAnticipatedPunch();
                }
            }

            if (_strategy == AiStrategy.Guard)
            {
                UpdateGuardStrategy();
            }
            else if (!_attackCombo.IsAttacking)
            {
                _attackCombo.TryAttack(player);
            }
        }

        private void OnOwnAttackResolved()
        {
            if (_strategy != AiStrategy.Attack || !_attackCombo.WasLastAttackBlocked)
            {
                return;
            }

            // Got blocked/dodged — coin flip whether to keep pressing the attack or hang back
            // and try to anticipate the player's next punch instead.
            if (Random.value < switchToGuardChanceAfterBlocked)
            {
                _strategy = AiStrategy.Guard;
                _guardWaitTimer = 0f;
                _isReactingToAnticipatedPunch = false;
                _useBlindGuardTiming = Random.value >= reactiveGuardChance;
            }
        }

        private void UpdateGuardStrategy()
        {
            if (_isReactingToAnticipatedPunch)
            {
                // Hold off attacking until our own guard sequence (if one actually started)
                // has fully played out, so we don't immediately throw a punch mid-guard.
                if (_blockDodge == null || _blockDodge.CurrentGuardState == GuardState.None)
                {
                    _isReactingToAnticipatedPunch = false;
                    _isInPostGuardDelay = true;
                    _postGuardDelayTimer = 0f;
                }

                return;
            }

            if (_isInPostGuardDelay)
            {
                _postGuardDelayTimer += Time.deltaTime;
                if (_postGuardDelayTimer >= postGuardAttackDelay)
                {
                    _isInPostGuardDelay = false;
                    _strategy = AiStrategy.Attack;
                }

                return;
            }

            if (_useBlindGuardTiming)
            {
                // Blind gamble: guard the instant the player's own block (the one that stuffed
                // our punch) ends, betting they immediately swing again — rather than actually
                // waiting to see a punch first.
                bool playerBlockEnded = _playerBlockDodge == null || _playerBlockDodge.CurrentGuardState == GuardState.None;
                if (playerBlockEnded)
                {
                    ReactToAnticipatedPunch();
                }

                return;
            }

            _guardWaitTimer += Time.deltaTime;

            bool playerIsAttacking = _playerAttackCombo != null && _playerAttackCombo.IsAttacking;
            if (playerIsAttacking)
            {
                // Actually saw the punch coming — guaranteed block (subject to BlockDodge's own
                // gates, e.g. can't guard while stunned or already being actively combo'd).
                ReactToAnticipatedPunch();
                return;
            }

            if (_guardWaitTimer >= guardAnticipationWindow)
            {
                _strategy = AiStrategy.Attack;
            }
        }

        private void ReactToAnticipatedPunch()
        {
            _blockDodge?.TryStartGuard();
            bool guarded = _blockDodge != null && _blockDodge.CurrentGuardState != GuardState.None;

            if (guarded)
            {
                _isReactingToAnticipatedPunch = true;
            }
            else
            {
                // Didn't actually guard — this was the "next hit" we were waiting/gambling for,
                // so go back to attacking immediately.
                _strategy = AiStrategy.Attack;
            }
        }

        private void UpdateFootsteps()
        {
            _footstepTimer -= Time.deltaTime;
            if (_footstepTimer <= 0f)
            {
                _footstepTimer = footstepInterval;
                _characterAudio?.PlayFootstep(true);
            }
        }

        private void RotateTowardsMovement()
        {
            FaceDirection(_agent.desiredVelocity);
        }

        private void FaceTarget(Vector3 targetPosition)
        {
            FaceDirection(targetPosition - transform.position);
        }

        private void FaceDirection(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        private void TraverseOffMeshLink()
        {
            if (!_isTraversingLink)
            {
                OffMeshLinkData linkData = _agent.currentOffMeshLinkData;
                _linkStartPosition = linkData.startPos;
                _linkEndPosition = linkData.endPos;
                _linkTraversalTimer = 0f;
                _isTraversingLink = true;

                FaceDirection(_linkEndPosition - _linkStartPosition);

                if (animator != null)
                {
                    animator.SetTrigger(JumpParam);
                }

                _characterAudio?.PlayJumpTakeoff();
            }

            _linkTraversalTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_linkTraversalTimer / linkJumpDuration);

            Vector3 position = Vector3.Lerp(_linkStartPosition, _linkEndPosition, t);
            position.y += Mathf.Sin(t * Mathf.PI) * linkJumpHeight;
            transform.position = position;

            if (t >= 1f)
            {
                _isTraversingLink = false;
                _agent.CompleteOffMeshLink();
                _characterAudio?.PlayJumpLand();
            }
        }

        private void UpdateAnimator()
        {
            if (animator == null)
            {
                return;
            }

            Vector3 localVelocity = transform.InverseTransformDirection(_agent.velocity);
            float speedNormalizer = _agent.speed > 0f ? _agent.speed : 1f;

            float moveX = (localVelocity.x / speedNormalizer) * runBlendMultiplier;
            float moveY = (localVelocity.z / speedNormalizer) * runBlendMultiplier;

            animator.SetFloat(MoveXParam, moveX, animatorSpeedDamping, Time.deltaTime);
            animator.SetFloat(MoveYParam, moveY, animatorSpeedDamping, Time.deltaTime);
            animator.SetBool(GroundedParam, !_isTraversingLink);
        }
    }
}
