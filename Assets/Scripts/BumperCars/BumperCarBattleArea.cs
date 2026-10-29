using System.Collections.Generic;
using UnityEngine;

namespace BumperCars
{
    /// <summary>World-space driving footprint, including the two spawn routes.</summary>
    public sealed class BumperCarBattleArea : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float edgePadding = 0.15f;
        [SerializeField, Tooltip("Allowed rectangles in world X/Z coordinates. Overlapping rectangles form one continuous area.")]
        private Rect[] regions =
        {
            Rect.MinMaxRect(-105f, -135f, 161f, 133f),
            Rect.MinMaxRect(-124f, -46f, -76f, -10f),
            Rect.MinMaxRect(-160f, -59.8f, -120f, -1.8f),
            Rect.MinMaxRect(139f, -15f, 183f, 13f),
            Rect.MinMaxRect(179f, -23.6f, 203f, 22.4f)
        };

        private const float Tolerance = 0.0001f;
        private float[] xEdges;
        private float[] zEdges;

        private void Awake() => CacheEdges();
        private void OnValidate() => CacheEdges();

        public Bounds GetFootprint(Rigidbody body, BoxCollider chassis)
        {
            Vector3 scale = chassis.transform.lossyScale;
            Vector3 half = Vector3.Scale(chassis.size, Abs(scale)) * 0.5f;
            Quaternion rotation = body.rotation;
            Vector3 extent = Abs(rotation * Vector3.right) * half.x
                + Abs(rotation * Vector3.up) * half.y + Abs(rotation * Vector3.forward) * half.z;
            Vector3 center = body.position + rotation * Vector3.Scale(chassis.center, scale);
            return new Bounds(center, extent * 2f);
        }

        public bool ContainsFootprint(Bounds footprint)
        {
            EnsureEdges();
            return Contains(footprint.center.x, footprint.center.z,
                footprint.extents.x + edgePadding, footprint.extents.z + edgePadding);
        }

        public bool Constrain(Rigidbody body, BoxCollider chassis)
        {
            if (!isActiveAndEnabled || body == null || chassis == null) return false;
            Bounds footprint = GetFootprint(body, chassis);
            if (ContainsFootprint(footprint)) return false;

            float halfX = footprint.extents.x + edgePadding;
            float halfZ = footprint.extents.z + edgePadding;
            Vector3 center = footprint.center;
            Vector3 correction = Vector3.zero;
            float nearest = float.PositiveInfinity;

            // The legal centers form axis-aligned cells. Their edges occur when
            // either side of the chassis crosses a region edge. Include the
            // requested coordinates to preserve free movement along a boundary.
            for (int x = -1; x < xEdges.Length * 2; x++)
            {
                float candidateX = x < 0 ? center.x
                    : xEdges[x / 2] + ((x & 1) == 0 ? halfX : -halfX);
                float dx = candidateX - center.x;
                if (dx * dx >= nearest) continue;
                for (int z = -1; z < zEdges.Length * 2; z++)
                {
                    float candidateZ = z < 0 ? center.z
                        : zEdges[z / 2] + ((z & 1) == 0 ? halfZ : -halfZ);
                    float dz = candidateZ - center.z;
                    float distance = dx * dx + dz * dz;
                    if (distance >= nearest || !Contains(candidateX, candidateZ, halfX, halfZ)) continue;
                    nearest = distance;
                    correction = new Vector3(dx, 0f, dz);
                }
            }

            if (float.IsPositiveInfinity(nearest)) return false;
            Vector3 position = body.position + correction;
            body.position = position;
            // Correct the rendered transform too, including interpolated bodies.
            body.transform.position = position;
            if (!body.isKinematic)
            {
                Vector3 velocity = body.velocity;
                if (Mathf.Abs(correction.x) > Tolerance && velocity.x * correction.x < 0f) velocity.x = 0f;
                if (Mathf.Abs(correction.z) > Tolerance && velocity.z * correction.z < 0f) velocity.z = 0f;
                body.velocity = velocity;
            }
            return true;
        }

        private bool Contains(float centerX, float centerZ, float halfX, float halfZ)
        {
            if (xEdges.Length < 2) return false;
            float left = centerX - halfX, right = centerX + halfX;
            if (left < xEdges[0] - Tolerance || right > xEdges[xEdges.Length - 1] + Tolerance) return false;

            // Check every vertical strip of the whole chassis, not just its
            // corners. This also catches the concave corners beside each ramp.
            for (int i = 1; i < xEdges.Length; i++)
            {
                float start = Mathf.Max(left, xEdges[i - 1]);
                float end = Mathf.Min(right, xEdges[i]);
                if (end - start <= Tolerance) continue;
                if (!CoversVertical((start + end) * 0.5f, centerZ - halfZ, centerZ + halfZ)) return false;
            }
            return true;
        }

        private bool CoversVertical(float x, float bottom, float top)
        {
            float covered = bottom;
            for (int pass = 0; pass < regions.Length; pass++)
            {
                float next = covered;
                foreach (Rect region in regions)
                    if (x >= region.xMin && x <= region.xMax
                        && region.yMin <= covered + Tolerance && region.yMax >= covered)
                        next = Mathf.Max(next, region.yMax);
                if (next >= top - Tolerance) return true;
                if (next <= covered) return false;
                covered = next;
            }
            return false;
        }

        private void EnsureEdges()
        {
            if (xEdges == null || zEdges == null) CacheEdges();
        }

        private void CacheEdges()
        {
            var xs = new SortedSet<float>();
            var zs = new SortedSet<float>();
            if (regions != null)
                foreach (Rect region in regions)
                {
                    if (region.width <= 0f || region.height <= 0f) continue;
                    xs.Add(region.xMin); xs.Add(region.xMax);
                    zs.Add(region.yMin); zs.Add(region.yMax);
                }
            xEdges = new float[xs.Count]; xs.CopyTo(xEdges);
            zEdges = new float[zs.Count]; zs.CopyTo(zEdges);
        }

        private void OnDrawGizmosSelected()
        {
            if (regions == null) return;
            Gizmos.color = new Color(1f, 0.65f, 0.1f, 0.8f);
            foreach (Rect region in regions)
                Gizmos.DrawWireCube(new Vector3(region.center.x, 43f, region.center.y),
                    new Vector3(region.width, 34f, region.height));
        }

        private static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
}
