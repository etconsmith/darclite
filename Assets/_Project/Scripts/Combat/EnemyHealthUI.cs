using UnityEngine;
using UnityEngine.UI;

namespace Darclite.Combat
{
    [AddComponentMenu("Darclite/Enemy Health UI")]
    public class EnemyHealthUI : MonoBehaviour
    {
        [SerializeField] private Combatant combatant;
        [SerializeField] private Text healthText;

        private UnityEngine.Camera _mainCamera;

        private void OnEnable()
        {
            if (combatant != null)
            {
                combatant.HealthChanged += UpdateText;
                UpdateText(combatant.CurrentHealth);
            }
        }

        private void OnDisable()
        {
            if (combatant != null)
            {
                combatant.HealthChanged -= UpdateText;
            }
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

        private void UpdateText(int health)
        {
            if (healthText != null)
            {
                healthText.text = health.ToString();
            }
        }
    }
}
