using System;
using UnityEngine;

namespace BumperCars
{
    public sealed class BumperCarHealth : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 100f;

        private float currentHealth;
        private bool defeated;

        public event Action<BumperCarHealth, float, float> HealthChanged;
        public event Action<BumperCarHealth> Defeated;

        public float MaxHealth => maxHealth;
        public float CurrentHealth => currentHealth;
        public float NormalizedHealth => maxHealth <= 0f ? 0f : currentHealth / maxHealth;
        public bool IsDefeated => defeated;

        private void Awake()
        {
            ResetHealth();
        }

        public void ResetHealth()
        {
            defeated = false;
            currentHealth = maxHealth;
            HealthChanged?.Invoke(this, currentHealth, maxHealth);
        }

        public void TakeDamage(float damage)
        {
            if (defeated || damage <= 0f)
            {
                return;
            }

            currentHealth = Mathf.Max(0f, currentHealth - damage);
            HealthChanged?.Invoke(this, currentHealth, maxHealth);

            if (currentHealth <= 0f)
            {
                defeated = true;
                Defeated?.Invoke(this);
            }
        }
    }
}
