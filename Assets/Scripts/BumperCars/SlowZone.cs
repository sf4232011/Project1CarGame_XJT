using UnityEngine;

namespace BumperCars
{
    [RequireComponent(typeof(Collider))]
    public sealed class SlowZone : MonoBehaviour
    {
        [SerializeField] private float lifetime = 8f;
        [SerializeField, Range(0.05f, 1f)] private float speedMultiplier = 0.5f;
        [SerializeField] private float slowDuration = 1f;
        [SerializeField] private bool ignoreOwner = true;

        private BumperCarController owner;
        private float lifeTimer;
        private Collider triggerCollider;

        private void Awake()
        {
            triggerCollider = GetComponent<Collider>();
            triggerCollider.isTrigger = true;
        }

        private void OnEnable()
        {
            lifeTimer = lifetime;
        }

        private void Update()
        {
            lifeTimer -= Time.deltaTime;
            if (lifeTimer <= 0f)
            {
                Destroy(gameObject);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            TryApplySlow(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryApplySlow(other);
        }

        public void Configure(float newLifetime, float newSpeedMultiplier, float newSlowDuration, BumperCarController newOwner)
        {
            lifetime = Mathf.Max(0.1f, newLifetime);
            speedMultiplier = Mathf.Clamp(newSpeedMultiplier, 0.05f, 1f);
            slowDuration = Mathf.Max(0.05f, newSlowDuration);
            owner = newOwner;
            lifeTimer = lifetime;
        }

        private void TryApplySlow(Collider other)
        {
            BumperCarController target = other.GetComponentInParent<BumperCarController>();
            if (target == null)
            {
                return;
            }

            if (ignoreOwner && target == owner)
            {
                return;
            }

            target.ApplySpeedModifier(speedMultiplier, slowDuration);
        }
    }
}
