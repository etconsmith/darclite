using Darclite.Core;
using Darclite.Dialogue;
using Darclite.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Darclite.Combat
{
    [RequireComponent(typeof(Combatant))]
    [RequireComponent(typeof(AttackCombo))]
    [AddComponentMenu("Darclite/Player Combat")]
    public class PlayerCombat : MonoBehaviour
    {
        public bool IsAttacking => _attackCombo.IsAttacking;

        private Combatant _selfCombatant;
        private AttackCombo _attackCombo;
        private BlockDodge _blockDodge;
        private PlayerController _playerController;

        private void Awake()
        {
            _selfCombatant = GetComponent<Combatant>();
            _attackCombo = GetComponent<AttackCombo>();
            _blockDodge = GetComponent<BlockDodge>();
            _playerController = GetComponent<PlayerController>();
        }

        // Punches always went straight at a single fixed "Enemy" reference before — now that the
        // Quest NPC is also a Combatant, swings need to land on whichever one is actually nearby
        // instead of always checking distance to a hardcoded target.
        private Transform FindNearestCombatant()
        {
            Combatant[] combatants = FindObjectsByType<Combatant>(FindObjectsInactive.Exclude);
            Transform nearest = null;
            float nearestSqrDistance = float.MaxValue;

            foreach (Combatant candidate in combatants)
            {
                if (candidate == _selfCombatant || candidate.IsDead)
                {
                    continue;
                }

                float sqrDistance = (candidate.transform.position - transform.position).sqrMagnitude;
                if (sqrDistance < nearestSqrDistance)
                {
                    nearestSqrDistance = sqrDistance;
                    nearest = candidate.transform;
                }
            }

            return nearest;
        }

        private void LateUpdate()
        {
            // Runs after every script's Update() this frame, so if the enemy's attack landed
            // on us this same frame (EnemyController.Update), IsStunned is already up to date
            // here — otherwise a click could sneak through on the exact frame we get hit.
            bool isGuarding = _blockDodge != null && _blockDodge.IsLockedInGuardAnimation;
            bool canAttack = _playerController == null || _playerController.CanAttack;
            bool chatOpen = NPCChatUI.Instance != null && NPCChatUI.Instance.IsOpen;
            bool statMenuOpen = StatMenuUI.Instance != null && StatMenuUI.Instance.IsOpen;
            if (_selfCombatant.IsStunned || _selfCombatant.IsBeingKnockedBack || _attackCombo.IsAttacking || isGuarding || !canAttack || chatOpen || statMenuOpen)
            {
                return;
            }

            // The click that closes the chat panel (clicking outside it) fires this same frame —
            // without this check it would also register as a punch the instant the chat closes.
            if (NPCChatUI.ConsumedClickThisFrame)
            {
                return;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                _attackCombo.TryAttack(FindNearestCombatant());
            }
        }
    }
}
