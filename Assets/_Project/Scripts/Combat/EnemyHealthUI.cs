using System.Collections;
using Darclite.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Darclite.Combat
{
    // Same bar visual/feedback as PlayerHealthUI (fill + delayed damage trail + hit punch), but
    // world-space above the enemy's head and hidden by default — each press of the player's Power
    // Sense hotbar slot toggles it on/off, matching the ability's "revealing their health above
    // their heads" description. Health tracking itself keeps running underneath while hidden, so
    // the bar is already showing the correct value whenever it's revealed.
    [AddComponentMenu("Darclite/Enemy Health UI")]
    public class EnemyHealthUI : MonoBehaviour
    {
        private const string AbilityName = "Power Sense 1";

        [SerializeField] private Combatant combatant;
        [SerializeField] private Image healthFillImage;
        [SerializeField] private Image damageTrailImage;
        [SerializeField] private RectTransform punchTarget;

        [Header("Colors")]
        [SerializeField] private Color healthColor = new Color(0.25f, 0.85f, 0.25f);
        [SerializeField] private Color damageTrailColor = new Color(0.95f, 0.75f, 0.15f);

        [Header("Damage Trail")]
        [SerializeField] private float trailCatchUpDelay = 0.35f;
        [SerializeField] private float trailCatchUpDuration = 0.45f;

        [Header("Punch Feedback")]
        [SerializeField] private float punchScale = 1.08f;
        [SerializeField] private float punchDuration = 0.15f;

        private UnityEngine.Camera _mainCamera;
        private Canvas _canvas;
        private float _currentFraction = 1f;
        private Coroutine _trailCoroutine;
        private Coroutine _punchCoroutine;
        private bool _revealed;
        private Vector3 _punchBaseScale = Vector3.one;

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();
            if (_canvas != null)
            {
                _canvas.enabled = false;
            }

            if (punchTarget != null)
            {
                _punchBaseScale = punchTarget.localScale;
            }

            if (healthFillImage != null)
            {
                healthFillImage.color = healthColor;
            }

            if (damageTrailImage != null)
            {
                damageTrailImage.color = damageTrailColor;
            }
        }

        private void OnEnable()
        {
            if (combatant != null)
            {
                combatant.HealthChanged += UpdateHealth;
            }
            AbilityLoadout.Activated += HandleActivated;
        }

        private void Start()
        {
            // Combatant lives on a different GameObject (the enemy, not this HUD canvas), so Unity
            // doesn't guarantee its Awake() ran before ours — reading CurrentHealth there could see
            // the pre-Awake default of 0. Start() is guaranteed to run after every object's Awake()
            // has finished, so it's safe to read the real value here.
            if (combatant != null)
            {
                _currentFraction = combatant.MaxHealth > 0 ? (float)combatant.CurrentHealth / combatant.MaxHealth : 1f;
                SetImmediate(_currentFraction);
            }
        }

        private void OnDisable()
        {
            if (combatant != null)
            {
                combatant.HealthChanged -= UpdateHealth;
            }
            AbilityLoadout.Activated -= HandleActivated;
        }

        private void LateUpdate()
        {
            if (_mainCamera == null)
            {
                _mainCamera = UnityEngine.Camera.main;
            }

            if (_mainCamera != null)
            {
                transform.rotation = _mainCamera.transform.rotation;
            }
        }

        private void HandleActivated(int slotIndex)
        {
            if (AbilityLoadout.GetAbilityName(slotIndex) != AbilityName)
            {
                return;
            }

            _revealed = !_revealed;
            if (_canvas != null)
            {
                _canvas.enabled = _revealed;
            }
        }

        private void UpdateHealth(int health)
        {
            if (combatant == null || combatant.MaxHealth <= 0)
            {
                return;
            }

            float previousFraction = _currentFraction;
            float newFraction = Mathf.Clamp01((float)health / combatant.MaxHealth);
            _currentFraction = newFraction;

            if (healthFillImage != null)
            {
                healthFillImage.fillAmount = newFraction;
            }

            if (newFraction < previousFraction)
            {
                // Lost health — let the trail catch down to the new value over time instead of
                // snapping instantly, so it's visible how much was just lost, and punch the bar
                // for a bit of hit feedback.
                if (_trailCoroutine != null)
                {
                    StopCoroutine(_trailCoroutine);
                }
                _trailCoroutine = StartCoroutine(CatchUpTrail(previousFraction, newFraction));

                if (_punchCoroutine != null)
                {
                    StopCoroutine(_punchCoroutine);
                }
                _punchCoroutine = StartCoroutine(PunchScale());
            }
            else if (damageTrailImage != null)
            {
                // Health went up (or was reset) — no reason to leave a stale trail behind.
                damageTrailImage.fillAmount = newFraction;
            }
        }

        private void SetImmediate(float fraction)
        {
            if (healthFillImage != null)
            {
                healthFillImage.fillAmount = fraction;
            }

            if (damageTrailImage != null)
            {
                damageTrailImage.fillAmount = fraction;
            }
        }

        private IEnumerator CatchUpTrail(float fromFraction, float toFraction)
        {
            if (damageTrailImage == null)
            {
                yield break;
            }

            damageTrailImage.fillAmount = fromFraction;

            yield return new WaitForSeconds(trailCatchUpDelay);

            float timer = 0f;
            while (timer < trailCatchUpDuration)
            {
                timer += Time.deltaTime;
                float t = Mathf.Clamp01(timer / trailCatchUpDuration);
                damageTrailImage.fillAmount = Mathf.Lerp(fromFraction, toFraction, t);
                yield return null;
            }

            damageTrailImage.fillAmount = toFraction;
            _trailCoroutine = null;
        }

        private IEnumerator PunchScale()
        {
            if (punchTarget == null)
            {
                yield break;
            }

            punchTarget.localScale = _punchBaseScale;

            float halfDuration = punchDuration * 0.5f;
            float timer = 0f;
            while (timer < halfDuration)
            {
                timer += Time.deltaTime;
                punchTarget.localScale = Vector3.Lerp(_punchBaseScale, _punchBaseScale * punchScale, timer / halfDuration);
                yield return null;
            }

            timer = 0f;
            while (timer < halfDuration)
            {
                timer += Time.deltaTime;
                punchTarget.localScale = Vector3.Lerp(_punchBaseScale * punchScale, _punchBaseScale, timer / halfDuration);
                yield return null;
            }

            punchTarget.localScale = _punchBaseScale;
            _punchCoroutine = null;
        }
    }
}
