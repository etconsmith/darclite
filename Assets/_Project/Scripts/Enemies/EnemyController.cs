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

        [Header("Combat")]
        [SerializeField] private float attackRange = 2f;
        [SerializeField, Range(0f, 1f)] private float hitStunSpeedMultiplier = 0.15f;

        [Header("Movement")]
        [SerializeField] private float rotationSpeed = 10f;
        [SerializeField] private float runBlendMultiplier = 2f;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private float animatorSpeedDamping = 0.15f;

        [Header("Off-Mesh Link Jump")]
        [SerializeField] private float linkJumpDuration = 0.5f;
        [SerializeField] private float linkJumpHeight = 1f;

        private NavMeshAgent _agent;
        private Combatant _combatant;
        private AttackCombo _attackCombo;
        private float _baseAgentSpeed;
        private bool _isTraversingLink;
        private float _linkTraversalTimer;
        private Vector3 _linkStartPosition;
        private Vector3 _linkEndPosition;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _agent.updateRotation = false;
            _agent.autoTraverseOffMeshLink = false;
            _baseAgentSpeed = _agent.speed;

            _combatant = GetComponent<Combatant>();
            _attackCombo = GetComponent<AttackCombo>();

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
        }

        private void Update()
        {
            if (player == null)
            {
                return;
            }

            if (_combatant.IsBeingKnockedBack)
            {
                _agent.isStopped = true;
                UpdateAnimator();
                return;
            }

            if (_combatant.IsStunned)
            {
                // Can't attack while reeling from a hit, but still creep toward the player
                // at a fraction of normal speed instead of being fully frozen in place.
                _agent.speed = _baseAgentSpeed * hitStunSpeedMultiplier;
                _agent.isStopped = false;
                _agent.SetDestination(player.position);
                RotateTowardsMovement();
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
                ChaseAndAttack();
            }

            UpdateAnimator();
        }

        private void ChaseAndAttack()
        {
            float distance = Vector3.Distance(transform.position, player.position);

            if (distance > attackRange)
            {
                _agent.isStopped = false;
                _agent.SetDestination(player.position);
                RotateTowardsMovement();
            }
            else
            {
                _agent.isStopped = true;
                FaceTarget(player.position);

                if (!_attackCombo.IsAttacking)
                {
                    _attackCombo.TryAttack(player);
                }
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
