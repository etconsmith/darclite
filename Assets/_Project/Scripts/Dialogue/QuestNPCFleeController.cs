using Darclite.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Darclite.Dialogue
{
    // Mirrors EnemyController's movement/animator plumbing, but she never attacks — she just
    // stands still until hit, then sprints directly away from the player for a few seconds.
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Combatant))]
    [AddComponentMenu("Darclite/Quest NPC Flee Controller")]
    public class QuestNPCFleeController : MonoBehaviour
    {
        private static readonly int MoveXParam = Animator.StringToHash("MoveX");
        private static readonly int MoveYParam = Animator.StringToHash("MoveY");
        private static readonly int GroundedParam = Animator.StringToHash("Grounded");

        [Header("Targeting")]
        [SerializeField] private Transform player;

        [Header("Flee")]
        [SerializeField] private float fleeDuration = 5f;
        [SerializeField] private float fleeDistance = 8f;
        [SerializeField] private float fleeSpeed = 4.5f;

        [Header("Movement")]
        [SerializeField] private float rotationSpeed = 10f;
        [SerializeField] private float runBlendMultiplier = 2f;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private float animatorSpeedDamping = 0.15f;

        private NavMeshAgent _agent;
        private Combatant _combatant;
        private float _baseAgentSpeed;
        private float _fleeTimer;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _agent.updateRotation = false;
            _baseAgentSpeed = _agent.speed;

            _combatant = GetComponent<Combatant>();

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

        private void OnEnable()
        {
            if (_combatant != null)
            {
                _combatant.HealthChanged += OnHealthChanged;
            }
        }

        private void OnDisable()
        {
            if (_combatant != null)
            {
                _combatant.HealthChanged -= OnHealthChanged;
            }
        }

        private void OnHealthChanged(int currentHealth)
        {
            // Any hit — including each hit within a combo — refreshes the window instead of
            // stacking, so she stays fleeing for fleeDuration after the LAST hit she took.
            _fleeTimer = fleeDuration;
        }

        private void Update()
        {
            if (_combatant.IsDead)
            {
                SetStopped(true);
                return;
            }

            if (_combatant.IsBeingKnockedBack)
            {
                SetStopped(true);
                UpdateAnimator();
                return;
            }

            if (_combatant.IsStunned)
            {
                // Reeling from the hit reaction — she starts actually running once the stun ends.
                SetStopped(true);
                UpdateAnimator();
                return;
            }

            if (_fleeTimer > 0f)
            {
                _fleeTimer -= Time.deltaTime;
                Flee();
            }
            else
            {
                _agent.speed = _baseAgentSpeed;
                SetStopped(true);
            }

            UpdateAnimator();
        }

        // NavMeshAgent throws (not just logs) if you touch isStopped/SetDestination while it
        // isn't currently placed on a valid NavMesh — e.g. she's standing somewhere the baked
        // NavMesh doesn't cover. Guarding here means a coverage gap degrades to "stands still"
        // instead of spamming errors every frame.
        private void SetStopped(bool stopped)
        {
            if (_agent.isOnNavMesh)
            {
                _agent.isStopped = stopped;
            }
        }

        private void Flee()
        {
            if (player == null || !_agent.isOnNavMesh)
            {
                SetStopped(true);
                return;
            }

            _agent.speed = fleeSpeed;
            SetStopped(false);

            Vector3 awayDirection = transform.position - player.position;
            awayDirection.y = 0f;
            if (awayDirection.sqrMagnitude < 0.0001f)
            {
                awayDirection = -transform.forward;
            }
            awayDirection.Normalize();

            Vector3 fleeTarget = transform.position + awayDirection * fleeDistance;

            if (NavMesh.SamplePosition(fleeTarget, out NavMeshHit hit, fleeDistance, NavMesh.AllAreas))
            {
                _agent.SetDestination(hit.position);
            }

            RotateTowardsMovement();
        }

        private void RotateTowardsMovement()
        {
            Vector3 direction = _agent.desiredVelocity;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
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
            animator.SetBool(GroundedParam, true);
        }
    }
}
