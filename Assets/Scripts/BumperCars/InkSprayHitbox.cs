using System.Collections.Generic;
using UnityEngine;

namespace BumperCars
{
    [RequireComponent(typeof(Collider), typeof(Rigidbody))]
    public sealed class InkSprayHitbox : MonoBehaviour
    {
        [SerializeField] private float lifetime = 0.35f;
        [SerializeField] private float inkDuration = 2.5f;
        [SerializeField] private float inkFadeTime = 0.5f;
        [SerializeField] private bool ignoreOwner = true;

        private readonly HashSet<BumperCarController> hitTargets = new HashSet<BumperCarController>();
        private Collider[] overlapBuffer = new Collider[32];
        private BumperCarController owner;
        private Collider hitboxCollider;
        private Rigidbody hitboxBody;
        private float lifeTimer;
        private bool isFlying;
        private Vector3 flightOriginCenter;
        private Vector3 flightDirection;
        private float flightSpeed;
        private float flightDistance;
        private float travelledDistance;

        private void Awake()
        {
            EnsureComponents();
        }

        private void EnsureComponents()
        {
            if (hitboxCollider == null) hitboxCollider = GetComponent<Collider>();
            hitboxCollider.isTrigger = true;
            MeshRenderer placeholder = GetComponent<MeshRenderer>();
            if (placeholder != null) placeholder.enabled = false;

            if (hitboxBody == null) hitboxBody = GetComponent<Rigidbody>();
            if (hitboxBody == null) hitboxBody = gameObject.AddComponent<Rigidbody>();
            hitboxBody.useGravity = false;
            hitboxBody.isKinematic = true;
            hitboxBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        private void OnEnable()
        {
            lifeTimer = lifetime;
            isFlying = false;
            hitTargets.Clear();
        }

        private void Update()
        {
            lifeTimer -= Time.deltaTime;
            if (lifeTimer <= 0f) Destroy(gameObject);
        }

        private void FixedUpdate()
        {
            if (!isFlying) return;

            float nextDistance = Mathf.Min(flightDistance, travelledDistance + flightSpeed * Time.fixedDeltaTime);
            Vector3 previousPosition = flightOriginCenter + flightDirection * travelledDistance;
            Vector3 nextPosition = flightOriginCenter + flightDirection * nextDistance;
            // Cover the complete step, rather than a whole arena-long invisible trigger.
            // This also catches fast targets without synchronizing every scene transform.
            ScanSweptOverlaps(previousPosition, nextPosition);
            hitboxBody.MovePosition(nextPosition);
            travelledDistance = nextDistance;
            if (travelledDistance >= flightDistance) isFlying = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            TryHit(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryHit(other);
        }

        public void Configure(BumperCarController newOwner, float newLifetime, float newInkDuration, float newInkFadeTime)
        {
            EnsureComponents();
            isFlying = false;
            ConfigureHit(newOwner, newLifetime, newInkDuration, newInkFadeTime);
            Physics.SyncTransforms();
            ScanSweptOverlaps(transform.position, transform.position);
        }

        public void ConfigureFlight(BumperCarController newOwner, InkFlightPath path, float newInkDuration, float newInkFadeTime)
        {
            EnsureComponents();
            flightDirection = path.Direction.normalized;
            flightSpeed = Mathf.Max(0.01f, path.Speed);
            flightDistance = Mathf.Max(0f, path.Distance);
            travelledDistance = 0f;
            Quaternion rotation = Quaternion.LookRotation(flightDirection, Vector3.up);
            transform.rotation = rotation;
            BoxCollider box = hitboxCollider as BoxCollider;
            float packetHalfDepth = box != null
                ? Mathf.Abs(box.size.z * transform.lossyScale.z) * 0.5f
                : hitboxCollider.bounds.extents.z;
            flightOriginCenter = path.Origin + flightDirection * packetHalfDepth;
            transform.SetPositionAndRotation(flightOriginCenter, rotation);
            hitboxBody.position = flightOriginCenter;
            hitboxBody.rotation = rotation;
            // The extra physics step lets the packet reach its final, off-arena pose.
            ConfigureHit(newOwner, Mathf.Max(0.05f, path.Duration) + 0.05f, newInkDuration, newInkFadeTime);
            isFlying = flightDistance > 0f;
            Physics.SyncTransforms();
            ScanSweptOverlaps(flightOriginCenter, flightOriginCenter);
        }

        private void ConfigureHit(BumperCarController newOwner, float newLifetime, float newInkDuration, float newInkFadeTime)
        {
            owner = newOwner;
            lifetime = Mathf.Max(0.05f, newLifetime);
            inkDuration = Mathf.Max(0.05f, newInkDuration);
            inkFadeTime = Mathf.Max(0.01f, newInkFadeTime);
            lifeTimer = lifetime;
            hitTargets.Clear();
        }

        private void TryHit(Collider other)
        {
            BumperCarController target = other.attachedRigidbody != null
                ? other.attachedRigidbody.GetComponent<BumperCarController>()
                : null;
            if (target == null) target = other.GetComponentInParent<BumperCarController>();
            if (target == null || (ignoreOwner && target == owner) || !hitTargets.Add(target)) return;
            InkScreenOverlay.ShowForPlayer(target.Player, inkDuration, inkFadeTime);
        }

        private void ScanSweptOverlaps(Vector3 previousPosition, Vector3 nextPosition)
        {
            BoxCollider box = hitboxCollider as BoxCollider;
            Vector3 centerOffset;
            Vector3 halfExtents;
            Quaternion rotation;
            if (box != null)
            {
                Vector3 scale = transform.lossyScale;
                centerOffset = transform.TransformVector(box.center);
                halfExtents = Vector3.Scale(box.size * 0.5f,
                    new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                rotation = transform.rotation;
            }
            else
            {
                Bounds bounds = hitboxCollider.bounds;
                centerOffset = bounds.center - transform.position;
                halfExtents = bounds.extents;
                rotation = Quaternion.identity;
            }

            Vector3 step = Quaternion.Inverse(rotation) * (nextPosition - previousPosition);
            halfExtents += new Vector3(Mathf.Abs(step.x), Mathf.Abs(step.y), Mathf.Abs(step.z)) * 0.5f;
            Vector3 center = (previousPosition + nextPosition) * 0.5f + centerOffset;
            int count;
            while (true)
            {
                count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuffer, rotation,
                    ~0, QueryTriggerInteraction.Collide);
                if (count < overlapBuffer.Length) break;
                if (overlapBuffer.Length >= 1024)
                {
                    Collider[] allOverlaps = Physics.OverlapBox(center, halfExtents, rotation,
                        ~0, QueryTriggerInteraction.Collide);
                    foreach (Collider other in allOverlaps)
                        if (other != hitboxCollider) TryHit(other);
                    return;
                }
                System.Array.Resize(ref overlapBuffer, overlapBuffer.Length * 2);
            }

            for (int i = 0; i < count; i++)
            {
                Collider other = overlapBuffer[i];
                if (other != hitboxCollider) TryHit(other);
                overlapBuffer[i] = null;
            }
        }
    }
}
