using UnityEngine;

namespace BumperCars
{
    public enum CarHitboxSection
    {
        Front,
        Body,
        Rear
    }

    [RequireComponent(typeof(Collider))]
    public sealed class CarHitbox : MonoBehaviour
    {
        [SerializeField] private CarHitboxSection section = CarHitboxSection.Body;
        [SerializeField] private BumperCarCollisionDamage owner;

        private Collider hitboxCollider;

        public CarHitboxSection Section => section;
        public BumperCarCollisionDamage Owner => owner;

        private void Awake()
        {
            hitboxCollider = GetComponent<Collider>();
            hitboxCollider.isTrigger = true;

            if (owner == null)
            {
                owner = GetComponentInParent<BumperCarCollisionDamage>();
            }
        }

        private void Reset()
        {
            Collider collider = GetComponent<Collider>();
            collider.isTrigger = true;
            owner = GetComponentInParent<BumperCarCollisionDamage>();
        }

        private void OnTriggerEnter(Collider other)
        {
            RegisterContact(other);
        }

        private void OnTriggerStay(Collider other)
        {
            RegisterContact(other);
        }

        private void RegisterContact(Collider other)
        {
            if (owner == null)
            {
                return;
            }

            BumperCarCollisionDamage attacker = other.GetComponentInParent<BumperCarCollisionDamage>();
            if (attacker == null || attacker == owner)
            {
                return;
            }

            owner.RegisterHitboxContact(attacker, section);
        }
    }
}
