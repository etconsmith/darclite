using UnityEngine;
using UnityEngine.InputSystem;

namespace Darclite.Combat
{
    [RequireComponent(typeof(Combatant))]
    [RequireComponent(typeof(AttackCombo))]
    [AddComponentMenu("Darclite/Player Combat")]
    public class PlayerCombat : MonoBehaviour
    {
        [SerializeField] private Transform target;

        public bool IsAttacking => _attackCombo.IsAttacking;

        private Combatant _selfCombatant;
        private AttackCombo _attackCombo;

        private void Awake()
        {
            _selfCombatant = GetComponent<Combatant>();
            _attackCombo = GetComponent<AttackCombo>();

            if (target == null)
            {
                GameObject enemyObject = GameObject.Find("Enemy");
                if (enemyObject != null)
                {
                    target = enemyObject.transform;
                }
            }
        }

        private void LateUpdate()
        {
            // Runs after every script's Update() this frame, so if the enemy's attack landed
            // on us this same frame (EnemyController.Update), IsStunned is already up to date
            // here — otherwise a click could sneak through on the exact frame we get hit.
            if (_selfCombatant.IsStunned || _selfCombatant.IsBeingKnockedBack || _attackCombo.IsAttacking)
            {
                return;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                _attackCombo.TryAttack(target);
            }
        }
    }
}
