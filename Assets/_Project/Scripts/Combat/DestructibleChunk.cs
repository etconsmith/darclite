using System.Collections;
using UnityEngine;

namespace Darclite.Combat
{
    // A single piece of a destructible structure (a wall panel, roof section, etc.) — starts
    // kinematic (rigid, part of the standing structure, unaffected by gravity/forces) and only
    // becomes a real physics object once it's taken enough Lite damage or a hard enough knockback
    // impact. Once broken it falls/tumbles like normal debris and despawns after a delay so
    // broken pieces don't accumulate over a long play session.
    [RequireComponent(typeof(Rigidbody))]
    [AddComponentMenu("Darclite/Destructible Chunk")]
    public class DestructibleChunk : MonoBehaviour
    {
        [SerializeField] private int maxHealth = 10;
        [SerializeField] private float impulseStrength = 6f;
        [SerializeField] private float torqueStrength = 4f;
        // Blended into every impulse so broken chunks pop up and off rather than just skidding
        // flat along whatever direction they were pushed.
        [SerializeField, Range(0f, 1f)] private float upwardBias = 0.3f;
        [SerializeField] private float despawnDelay = 20f;
        // Knockback speed (units/sec) a crashing character needs to break this chunk outright,
        // regardless of remaining health — a light bump shouldn't punch a hole in a wall.
        [SerializeField] private float impactBreakSpeed = 10f;

        private Rigidbody _rigidbody;
        private int _currentHealth;
        private bool _isBroken;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.isKinematic = true;
            _currentHealth = maxHealth;
        }

        // Called by AOE Lite abilities (Lite Burst, Lite Release) — origin is the effect's own
        // origin point, used to push the chunk outward from the blast once it breaks.
        public void ApplyDamage(int amount, Vector3 origin)
        {
            if (_isBroken)
            {
                return;
            }

            _currentHealth -= amount;
            if (_currentHealth <= 0)
            {
                Vector3 outward = transform.position - origin;
                outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : Vector3.up;
                Break(outward);
            }
        }

        // Called by Combatant.OnControllerColliderHit while a character is mid-knockback-slide —
        // direction is the character's current travel direction, so a hard enough hit sends the
        // chunk flying onward in the same direction it was crashed into, rather than outward from
        // a point.
        public void ApplyImpact(float speed, Vector3 direction)
        {
            if (_isBroken || speed < impactBreakSpeed)
            {
                return;
            }

            Break(direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up);
        }

        private void Break(Vector3 impulseDirection)
        {
            _isBroken = true;
            _rigidbody.isKinematic = false;

            Vector3 finalDirection = (impulseDirection + Vector3.up * upwardBias).normalized;
            _rigidbody.AddForce(finalDirection * impulseStrength, ForceMode.VelocityChange);
            _rigidbody.AddTorque(Random.insideUnitSphere * torqueStrength, ForceMode.VelocityChange);

            StartCoroutine(DespawnAfterDelay());
        }

        private IEnumerator DespawnAfterDelay()
        {
            yield return new WaitForSeconds(despawnDelay);
            Destroy(gameObject);
        }
    }
}
