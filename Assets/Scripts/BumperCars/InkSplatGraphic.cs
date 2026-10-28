using UnityEngine;
using UnityEngine.UI;

namespace BumperCars
{
    /// <summary>Texture-free, cached liquid silhouette for the split-screen ink hit.</summary>
    [AddComponentMenu("")]
    public sealed class InkSplatGraphic : MaskableGraphic
    {
        private const int Segments = 64;
        private Vector2[] outline;
        private bool droplet;
        private int seed;

        public void Configure(int shapeSeed, bool isDroplet)
        {
            seed = shapeSeed;
            droplet = isDroplet;
            outline = new Vector2[Segments];
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * (Mathf.PI * 2f / Segments);
                float sine = Mathf.Sin(angle);
                if (droplet)
                {
                    float width = Mathf.Lerp(1.0f, 0.18f, (sine + 1f) * 0.5f);
                    outline[i] = new Vector2(Mathf.Cos(angle) * 0.5f * width, sine * 0.5f);
                }
                else
                {
                    float phase = shapeSeed * 0.73f;
                    float radius = 0.43f + Mathf.Sin(angle * 5f + phase) * 0.046f
                        + Mathf.Sin(angle * 8f - phase) * 0.025f + Mathf.Cos(angle * 3f + phase * 0.5f) * 0.018f;
                    outline[i] = new Vector2(Mathf.Cos(angle), sine) * radius;
                }
            }

            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            if (outline == null)
            {
                Configure(seed, droplet);
            }

            Rect rect = GetPixelAdjustedRect();
            Color body = color;
            Color rim = new Color(body.r * 0.45f, body.g * 0.45f, body.b * 0.55f, body.a);
            Color center = new Color(body.r + 0.035f, body.g + 0.008f, body.b + 0.05f, body.a);
            AddVertex(vertices, rect.center, center);
            for (int i = 0; i < Segments; i++)
            {
                AddVertex(vertices, ToPoint(outline[i] * 0.94f, rect), body);
                AddVertex(vertices, ToPoint(outline[i], rect), rim);
            }

            for (int i = 0; i < Segments; i++)
            {
                int next = (i + 1) % Segments;
                int inner = 1 + i * 2;
                int nextInner = 1 + next * 2;
                vertices.AddTriangle(0, inner, nextInner);
                vertices.AddTriangle(inner, inner + 1, nextInner + 1);
                vertices.AddTriangle(inner, nextInner + 1, nextInner);
            }

            // Short magenta reflections suggest wet ink without outlining the entire splat.
            Color glint = new Color(0.57f, 0.19f, 0.49f, body.a * 0.45f);
            int start = 10 + seed % 5;
            int length = droplet ? 7 : 10;
            for (int i = 0; i < length; i++)
            {
                int a = (start + i) % Segments;
                int b = (a + 1) % Segments;
                int offset = vertices.currentVertCount;
                Color tip = glint;
                tip.a *= Mathf.Sin((i + 0.5f) / length * Mathf.PI);
                AddVertex(vertices, ToPoint(outline[a] * 0.82f, rect), tip);
                AddVertex(vertices, ToPoint(outline[a] * 0.86f, rect), tip);
                AddVertex(vertices, ToPoint(outline[b] * 0.82f, rect), tip);
                AddVertex(vertices, ToPoint(outline[b] * 0.86f, rect), tip);
                vertices.AddTriangle(offset, offset + 1, offset + 3);
                vertices.AddTriangle(offset, offset + 3, offset + 2);
            }
        }

        private static Vector2 ToPoint(Vector2 point, Rect rect)
        {
            return rect.center + Vector2.Scale(point, rect.size);
        }

        private static void AddVertex(VertexHelper vertices, Vector2 point, Color tint)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = new Vector3(point.x, point.y, 0f);
            vertex.color = tint;
            vertices.AddVert(vertex);
        }
    }
}
