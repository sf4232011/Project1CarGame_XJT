using UnityEngine;
using UnityEngine.SceneManagement;

namespace BumperCars
{
    /// <summary>A straight shot whose lifetime is determined by the level boundary.</summary>
    public readonly struct InkFlightPath
    {
        public readonly Vector3 Origin;
        public readonly Vector3 Direction;
        public readonly Bounds ArenaBounds;
        public readonly float Speed;
        public readonly float Distance;
        public float Duration => Distance / Speed;

        private InkFlightPath(Vector3 origin, Vector3 direction, float speed, float distance, Bounds bounds)
        {
            Origin = origin;
            Direction = direction;
            Speed = speed;
            Distance = distance;
            ArenaBounds = bounds;
        }

        public static InkFlightPath Create(Vector3 origin, Vector3 direction, float speed)
        {
            Scene scene = SceneManager.GetActiveScene();
            Collider[] colliders = Object.FindObjectsOfType<Collider>();
            Bounds bounds = default;
            bool found = false;
            for (int pass = 0; pass < 2 && !found; pass++)
            {
                foreach (Collider collider in colliders)
                {
                    if (!collider.enabled || collider.isTrigger || collider.gameObject.scene != scene
                        || collider.attachedRigidbody != null || collider.GetComponentInParent<BumperCarController>() != null)
                        continue;

                    // The original prototype floor extends far beyond the authored arena.
                    bool legacyFloor = collider.gameObject.name == "Plane" && collider.transform.parent == null;
                    if (pass == 0 && legacyFloor) continue;
                    Bounds candidate = collider.bounds;
                    if (candidate.size.x * candidate.size.z < 0.01f) continue;
                    if (!found) { bounds = candidate; found = true; }
                    else bounds.Encapsulate(candidate);
                }
            }

            if (!found) bounds = new Bounds(origin, new Vector3(500f, 100f, 500f));
            return FromBounds(origin, direction, speed, bounds, 10f);
        }

        public static InkFlightPath FromBounds(Vector3 origin, Vector3 direction, float speed, Bounds bounds, float clearance = 3f)
        {
            direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
            speed = Mathf.Max(1f, speed);
            // A cast made just outside the arena still travels away before it is cleaned up.
            bounds.Encapsulate(new Vector3(origin.x, bounds.center.y, origin.z));
            float exitX = float.PositiveInfinity;
            float exitZ = float.PositiveInfinity;
            if (Mathf.Abs(direction.x) > 0.0001f)
                exitX = ((direction.x > 0f ? bounds.max.x : bounds.min.x) - origin.x) / direction.x;
            if (Mathf.Abs(direction.z) > 0.0001f)
                exitZ = ((direction.z > 0f ? bounds.max.z : bounds.min.z) - origin.z) / direction.z;
            float distance = Mathf.Max(0f, Mathf.Min(exitX, exitZ)) + Mathf.Max(3f, clearance);
            return new InkFlightPath(origin, direction, speed, distance, bounds);
        }

        public bool IsInsideArena(Vector3 position)
        {
            return position.x >= ArenaBounds.min.x && position.x <= ArenaBounds.max.x
                && position.z >= ArenaBounds.min.z && position.z <= ArenaBounds.max.z;
        }
    }
}
