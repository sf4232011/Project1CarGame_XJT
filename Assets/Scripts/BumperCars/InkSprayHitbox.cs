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
        private BumperCarController owner;
        private float lifeTimer;

        private void Awake()
        {
            Collider hitboxCollider = GetComponent<Collider>();
            hitboxCollider.isTrigger = true;

            Rigidbody hitboxBody = GetComponent<Rigidbody>();
            if (hitboxBody == null)
            {
                hitboxBody = gameObject.AddComponent<Rigidbody>();
            }

            hitboxBody.useGravity = false;
            hitboxBody.isKinematic = true;
            hitboxBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        private void OnEnable()
        {
            lifeTimer = lifetime;
            hitTargets.Clear();
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
            TryHit(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryHit(other);
        }

        public void Configure(BumperCarController newOwner, float newLifetime, float newInkDuration, float newInkFadeTime)
        {
            owner = newOwner;
            lifetime = Mathf.Max(0.05f, newLifetime);
            inkDuration = Mathf.Max(0.05f, newInkDuration);
            inkFadeTime = Mathf.Max(0.01f, newInkFadeTime);
            lifeTimer = lifetime;
            hitTargets.Clear();

            Physics.SyncTransforms();
            ScanCurrentOverlaps();
        }

        private void TryHit(Collider other)
        {
            BumperCarController target = other.attachedRigidbody != null
                ? other.attachedRigidbody.GetComponent<BumperCarController>()
                : null;

            if (target == null)
            {
                target = other.GetComponentInParent<BumperCarController>();
            }

            if (target == null)
            {
                return;
            }

            if (ignoreOwner && target == owner)
            {
                return;
            }

            if (!hitTargets.Add(target))
            {
                return;
            }

            InkScreenOverlay.ShowForPlayer(target.Player, inkDuration, inkFadeTime);
        }

        private void ScanCurrentOverlaps()
        {
            Collider hitboxCollider = GetComponent<Collider>();
            Bounds bounds = hitboxCollider.bounds;
            Collider[] overlaps = Physics.OverlapBox(
                bounds.center,
                bounds.extents,
                transform.rotation,
                ~0,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < overlaps.Length; i++)
            {
                if (overlaps[i] != hitboxCollider)
                {
                    TryHit(overlaps[i]);
                }
            }
        }
    }
}
