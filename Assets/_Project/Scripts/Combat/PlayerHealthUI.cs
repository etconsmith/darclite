using UnityEngine;
using UnityEngine.UI;

namespace Darclite.Combat
{
    [AddComponentMenu("Darclite/Player Health UI")]
    public class PlayerHealthUI : MonoBehaviour
    {
        [SerializeField] private Combatant combatant;
        [SerializeField] private Text healthText;

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

        private void UpdateText(int health)
        {
            if (healthText != null)
            {
                healthText.text = health.ToString();
            }
        }
    }
}
