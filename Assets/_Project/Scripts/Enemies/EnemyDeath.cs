using System.Collections;
using Darclite.Combat;
using Darclite.Player;
using UnityEngine;
using UnityEngine.AI;

namespace Darclite.Enemies
{
    [RequireComponent(typeof(Combatant))]
    [AddComponentMenu("Darclite/Enemy Death")]
    public class EnemyDeath : MonoBehaviour
    {
        private static readonly int DeathParam = Animator.StringToHash("Death");
        private static readonly int DeathIndexParam = Animator.StringToHash("DeathIndex");

        [Header("References")]
        [SerializeField] private Animator animator;

        [Header("Rewards")]
        [SerializeField] private int xpReward = 25;

        // Real lengths of Death/Death2/Death3, populated by SceneBootstrapper.PopulateDeathDurations
        // so we know exactly how long to wait before freezing the pose.
        [SerializeField] private float[] deathClipDurations = new float[3];

        private Combatant _combatant;
        private NavMeshAgent _agent;
        private EnemyController _enemyController;
        private AttackCombo _attackCombo;
        private BlockDodge _blockDodge;

        private void Awake()
        {
            _combatant = GetComponent<Combatant>();
            _agent = GetComponent<NavMeshAgent>();
            _enemyController = GetComponent<EnemyController>();
            _attackCombo = GetComponent<AttackCombo>();
            _blockDodge = GetComponent<BlockDodge>();

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            _combatant.OnDeath += HandleDeath;
        }

        private void OnDestroy()
        {
            _combatant.OnDeath -= HandleDeath;
        }

        private void HandleDeath()
        {
            PlayerStats.Instance?.GrantXp(xpReward);
            StartCoroutine(DeathSequence());
        }

        private IEnumerator DeathSequence()
        {
            // Shut off everything that would otherwise fight for control of this GameObject.
            if (_enemyController != null)
            {
                _enemyController.enabled = false;
            }
            if (_attackCombo != null)
            {
                _attackCombo.enabled = false;
            }
            if (_blockDodge != null)
            {
                _blockDodge.enabled = false;
            }
            if (_agent != null)
            {
                _agent.isStopped = true;
            }

            int index = Random.Range(0, 3);
            float duration = deathClipDurations[index] > 0f ? deathClipDurations[index] : 2f;

            if (animator != null)
            {
                animator.SetFloat(DeathIndexParam, index);
                animator.SetTrigger(DeathParam);
            }

            yield return new WaitForSeconds(duration);

            if (_agent != null)
            {
                _agent.enabled = false;
            }

            // Freeze exactly on the death clip's last frame — with nothing else driving the
            // Animator forward and no outgoing transition out of the Death state, disabling it
            // here just locks in whatever pose was last evaluated.
            if (animator != null)
            {
                animator.enabled = false;
            }

            DisableAllColliders();
        }

        private void DisableAllColliders()
        {
            // Non-collidable entirely — this also catches any collider baked into the imported
            // model itself (e.g. a default capsule/box from the rig), not just ones we added.
            foreach (Collider col in GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }
        }
    }
}
