using System.Collections.Generic;
using UnityEngine;

namespace BumperCars
{
    [RequireComponent(typeof(BumperCarController))]
    [RequireComponent(typeof(BumperCarHealth))]
    public sealed class BumperCarCollisionDamage : MonoBehaviour
    {
        [Header("Damage")]
        [SerializeField] private float minEffectiveSpeed = 3f;
        [SerializeField] private float damageMultiplier = 3f;
        [SerializeField] private float maxSingleHitDamage = 30f;
        [SerializeField] private float damageCooldown = 0.7f;
        [SerializeField] private float contactRecordLifetime = 0.25f;

        [Header("Hitbox Multipliers")]
        [SerializeField] private float frontDamageMultiplier = 0.6f;
        [SerializeField] private float bodyDamageMultiplier = 1f;
        [SerializeField] private float rearDamageMultiplier = 1.4f;
        [SerializeField] private float headOnDamageReduction = 0.7f;
        [SerializeField] private float frontAttackRecoilDamageMultiplier = 0.25f;

        [Header("Fallback Section Guess")]
        [SerializeField] private float fallbackFrontRearDistance = 0.75f;
        [SerializeField] private float frontAttackFacingDot = 0.25f;

        [Header("Knockback")]
        [SerializeField] private float knockbackBaseImpulse = 2.5f;
        [SerializeField] private float knockbackPerDamage = 0.22f;
        [SerializeField] private float selfRecoilMultiplier = 0.35f;

        [Header("Feedback")]
        [SerializeField] private BumperCarImpactFeedback impactFeedback;

        private readonly Dictionary<BumperCarCollisionDamage, float> lastDamageTimes = new Dictionary<BumperCarCollisionDamage, float>();
        private readonly Dictionary<BumperCarCollisionDamage, HitboxContact> recentHitboxContacts = new Dictionary<BumperCarCollisionDamage, HitboxContact>();

        private BumperCarController controller;
        private BumperCarHealth health;
        private BoxCollider chassisCollider;

        public BumperCarController Controller => controller;
        public BumperCarHealth Health => health;

        private void Awake()
        {
            controller = GetComponent<BumperCarController>();
            health = GetComponent<BumperCarHealth>();
            chassisCollider = GetComponent<BoxCollider>();

            if (impactFeedback == null)
            {
                impactFeedback = GetComponentInChildren<BumperCarImpactFeedback>();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            TryResolveCollision(collision);
        }

        public void RegisterHitboxContact(BumperCarCollisionDamage attacker, CarHitboxSection hitboxSection)
        {
            if (attacker == null || attacker == this)
            {
                return;
            }

            if (recentHitboxContacts.TryGetValue(attacker, out HitboxContact existing)
                && Time.time - existing.Time <= contactRecordLifetime
                && GetSectionPriority(existing.Section) > GetSectionPriority(hitboxSection))
            {
                return;
            }

            recentHitboxContacts[attacker] = new HitboxContact(hitboxSection, Time.time);
        }

        private void TryResolveCollision(Collision collision)
        {
            BumperCarCollisionDamage targetDealer = collision.collider.GetComponentInParent<BumperCarCollisionDamage>();
            if (targetDealer == null)
            {
                PlayEnvironmentImpact(collision);
                return;
            }
            if (targetDealer == this || targetDealer.Health == null) return;

            if (lastDamageTimes.TryGetValue(targetDealer, out float lastDamageTime) && Time.time - lastDamageTime < damageCooldown)
            {
                PlayLightContact(collision, targetDealer);
                return;
            }

            float attackerSpeed = controller.ImpactSpeedSample;
            if (attackerSpeed < minEffectiveSpeed)
            {
                PlayLightContact(collision, targetDealer);
                return;
            }

            Vector3 contactPoint = GetContactPoint(collision, targetDealer.transform.position);
            CarHitboxSection targetSection = targetDealer.GetRecentOrFallbackSection(this, contactPoint);
            CarHitboxSection attackerSection = GetRecentOrFallbackSection(targetDealer, contactPoint);
            bool headOn = attackerSection == CarHitboxSection.Front && targetSection == CarHitboxSection.Front;
            bool frontAttack = attackerSection == CarHitboxSection.Front || IsFacingTarget(targetDealer);

            float damage = CalculateDamage(attackerSpeed, targetSection, headOn);
            float normalizedDamage = Mathf.Clamp01(damage / Mathf.Max(0.01f, maxSingleHitDamage));
            // Trigger hitboxes overlap. Use the physical contact for the sound's
            // section, while keeping the existing combat multipliers unchanged.
            bool soundHandled = BumperCarAudio.PlayImpact(normalizedDamage, targetDealer.Controller,
                targetDealer.GetAudioSection(GetAudioContactPoint(collision, contactPoint)), controller);
            lastDamageTimes[targetDealer] = Time.time;
            // Queue the impact before a lethal hit can end and mute the match.
            targetDealer.Health.TakeDamage(damage);

            ApplyImpactFeedbackAndKnockback(collision, targetDealer, damage, !soundHandled);

            if (frontAttack && targetSection != CarHitboxSection.Front)
            {
                ApplyAttackerRecoilDamage(damage);
            }
        }

        private void PlayEnvironmentImpact(Collision collision)
        {
            if (controller == null || !controller.ControlsEnabled) return;
            float normalSpeed = 0f;
            Vector3 contactSum = Vector3.zero;
            int wallContacts = 0;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                // Road slopes and floor seams are rolling surfaces, not crashes.
                if (Mathf.Abs(contact.normal.y) >= 0.65f) continue;
                float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
                if (speed <= 2f) continue;
                normalSpeed = Mathf.Max(normalSpeed, speed);
                contactSum += contact.point;
                wallContacts++;
            }
            if (normalSpeed <= 2f) return;
            int surfaceId = collision.rigidbody != null ? collision.rigidbody.GetInstanceID() : collision.collider.GetInstanceID();
            BumperCarAudio.PlayImpact(Mathf.InverseLerp(2f, 24f, normalSpeed), controller,
                GetAudioSection(contactSum / wallContacts), null, surfaceId);
        }

        private void PlayLightContact(Collision collision, BumperCarCollisionDamage other)
        {
            float normalSpeed = 0f;
            for (int i = 0; i < collision.contactCount; i++)
                normalSpeed = Mathf.Max(normalSpeed, Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(i).normal)));
            if (normalSpeed < 0.8f) return;
            Vector3 point = GetAudioContactPoint(collision, other.transform.position);
            BumperCarAudio.PlayImpact(Mathf.Min(0.18f, normalSpeed / 100f), other.Controller,
                other.GetAudioSection(point), controller);
        }

        private static Vector3 GetAudioContactPoint(Collision collision, Vector3 fallback)
        {
            if (collision.contactCount == 0) return fallback;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < collision.contactCount; i++) sum += collision.GetContact(i).point;
            return sum / collision.contactCount;
        }

        private CarHitboxSection GetAudioSection(Vector3 worldPoint)
        {
            if (chassisCollider == null) return GuessSectionFromContactPoint(worldPoint);
            // Imported cars have offset pivots and very different scales. Measure
            // against the actual chassis center and dimensions, in local space.
            Vector3 local = transform.InverseTransformPoint(worldPoint) - chassisCollider.center;
            float longitudinal = local.z / Mathf.Max(0.001f, chassisCollider.size.z * 0.5f);
            float lateral = Mathf.Abs(local.x) / Mathf.Max(0.001f, chassisCollider.size.x * 0.5f);
            if (Mathf.Abs(longitudinal) < 0.65f || Mathf.Abs(longitudinal) < lateral * 0.85f)
                return CarHitboxSection.Body;
            float forwardSign = Vector3.Dot(controller.DriveForwardDirection, transform.forward) >= 0f ? 1f : -1f;
            return longitudinal * forwardSign >= 0f ? CarHitboxSection.Front : CarHitboxSection.Rear;
        }

        private float CalculateDamage(float attackerSpeed, CarHitboxSection targetSection, bool headOn)
        {
            float sectionMultiplier = GetDamageMultiplier(targetSection);
            float damage = attackerSpeed * damageMultiplier * sectionMultiplier;
            if (headOn)
            {
                damage *= headOnDamageReduction;
            }

            return Mathf.Clamp(damage, 0f, maxSingleHitDamage);
        }

        private void ApplyAttackerRecoilDamage(float dealtDamage)
        {
            if (health == null || frontAttackRecoilDamageMultiplier <= 0f)
            {
                return;
            }

            float recoilDamage = Mathf.Min(maxSingleHitDamage, dealtDamage * frontAttackRecoilDamageMultiplier);
            health.TakeDamage(recoilDamage);
        }

        private void ApplyImpactFeedbackAndKnockback(Collision collision, BumperCarCollisionDamage targetDealer, float damage, bool playAudio)
        {
            Vector3 hitDirection = GetHitDirection(collision, targetDealer.transform.position - transform.position);
            float impulseMagnitude = knockbackBaseImpulse + damage * knockbackPerDamage;
            float normalizedDamage = Mathf.Clamp01(damage / Mathf.Max(0.01f, maxSingleHitDamage));

            targetDealer.Controller.ReceiveImpact(hitDirection * impulseMagnitude, normalizedDamage);
            controller.ReceiveImpact(-hitDirection * impulseMagnitude * selfRecoilMultiplier, normalizedDamage * 0.5f);

            if (impactFeedback != null)
            {
                impactFeedback.Play(GetContactPoint(collision, targetDealer.transform.position), normalizedDamage, playAudio);
            }
        }

        private CarHitboxSection GetRecentOrFallbackSection(BumperCarCollisionDamage otherDealer, Vector3 contactPoint)
        {
            if (recentHitboxContacts.TryGetValue(otherDealer, out HitboxContact contact)
                && Time.time - contact.Time <= contactRecordLifetime)
            {
                return contact.Section;
            }

            return GuessSectionFromContactPoint(contactPoint);
        }

        private CarHitboxSection GuessSectionFromContactPoint(Vector3 contactPoint)
        {
            Vector3 centerToContact = contactPoint - transform.position;
            centerToContact.y = 0f;

            Vector3 forward = controller.DriveForwardDirection;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                return CarHitboxSection.Body;
            }

            float forwardDistance = Vector3.Dot(centerToContact, forward.normalized);
            if (forwardDistance >= fallbackFrontRearDistance)
            {
                return CarHitboxSection.Front;
            }

            if (forwardDistance <= -fallbackFrontRearDistance)
            {
                return CarHitboxSection.Rear;
            }

            return CarHitboxSection.Body;
        }

        private bool IsFacingTarget(BumperCarCollisionDamage targetDealer)
        {
            Vector3 toTarget = targetDealer.transform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.001f)
            {
                return false;
            }

            Vector3 forward = controller.DriveForwardDirection;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.001f && Vector3.Dot(forward.normalized, toTarget.normalized) >= frontAttackFacingDot;
        }

        private float GetDamageMultiplier(CarHitboxSection section)
        {
            switch (section)
            {
                case CarHitboxSection.Front:
                    return frontDamageMultiplier;
                case CarHitboxSection.Rear:
                    return rearDamageMultiplier;
                default:
                    return bodyDamageMultiplier;
            }
        }

        private static int GetSectionPriority(CarHitboxSection section)
        {
            switch (section)
            {
                case CarHitboxSection.Rear:
                    return 3;
                case CarHitboxSection.Body:
                    return 2;
                default:
                    return 1;
            }
        }

        private static Vector3 GetContactPoint(Collision collision, Vector3 fallback)
        {
            return collision.contactCount > 0 ? collision.GetContact(0).point : fallback;
        }

        private static Vector3 GetHitDirection(Collision collision, Vector3 fallback)
        {
            Vector3 direction = fallback;
            if (collision.contactCount > 0)
            {
                direction = -collision.GetContact(0).normal;
            }

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = fallback;
                direction.y = 0f;
            }

            return direction.sqrMagnitude < 0.001f ? Vector3.forward : direction.normalized;
        }

        private readonly struct HitboxContact
        {
            public HitboxContact(CarHitboxSection section, float time)
            {
                Section = section;
                Time = time;
            }

            public CarHitboxSection Section { get; }
            public float Time { get; }
        }
    }
}
