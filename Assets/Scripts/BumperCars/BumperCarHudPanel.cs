using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BumperCars
{
    public sealed class BumperCarHudPanel : MonoBehaviour
    {
        [SerializeField] private BumperCarHealth health;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text healthText;
        [SerializeField] private Image healthFill;
        [SerializeField] private string playerName = "P1";

        private void OnEnable()
        {
            if (health != null)
            {
                health.HealthChanged += OnHealthChanged;
                OnHealthChanged(health, health.CurrentHealth, health.MaxHealth);
            }

            if (nameText != null)
            {
                nameText.text = playerName;
            }
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.HealthChanged -= OnHealthChanged;
            }
        }

        public void Bind(BumperCarHealth newHealth, string newPlayerName)
        {
            if (health != null && isActiveAndEnabled)
            {
                health.HealthChanged -= OnHealthChanged;
            }

            health = newHealth;
            playerName = newPlayerName;

            if (nameText != null)
            {
                nameText.text = playerName;
            }

            if (health != null && isActiveAndEnabled)
            {
                health.HealthChanged += OnHealthChanged;
                OnHealthChanged(health, health.CurrentHealth, health.MaxHealth);
            }
        }

        private void OnHealthChanged(BumperCarHealth changedHealth, float currentHealth, float maxHealth)
        {
            if (healthFill != null)
            {
                healthFill.fillAmount = maxHealth <= 0f ? 0f : currentHealth / maxHealth;
                healthFill.color = Color.Lerp(new Color(0.95f, 0.18f, 0.14f), new Color(0.12f, 0.85f, 0.36f), healthFill.fillAmount);
            }

            if (healthText != null)
            {
                healthText.text = $"{Mathf.CeilToInt(currentHealth)} / {Mathf.CeilToInt(maxHealth)}";
            }
        }
    }
}
